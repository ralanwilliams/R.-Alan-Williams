// Public CV downloads (ADR 0003 §4-5): turns a /cv URL into a stored file, or an error.
//
// Pure request handling with its dependencies passed in, so the Node tests can run it without
// Cloudflare or a database. functions/ wires it to Hyperdrive and the edge cache (pages.js).

/** The publishable locales in cv.locales. Adding a language there means adding it here too. */
export const LOCALES = ['en', 'nb', 'fr'];
export const DEFAULT_LOCALE = 'en';

/** The formats cv_renders stores (ck_cv_renders_format). */
export const FORMATS = {
  pdf: { contentType: 'application/pdf', disposition: 'inline' },
  html: { contentType: 'text/html; charset=utf-8', disposition: 'inline' },
  md: { contentType: 'text/markdown; charset=utf-8', disposition: 'attachment' },
};
export const DEFAULT_FORMAT = 'pdf';

/** Seconds a response may be cached: a publish shows up within 5 minutes, with no purge (§5). */
export const LATEST_MAX_AGE = 300;
/** Not immutable: a renderer change re-renders old versions (§5). */
export const PINNED_MAX_AGE = 86400;

const MAX_VERSION = 2147483647; // cv_versions.version_number is an integer

// The stored HTML inlines its stylesheet and has no scripts, images or forms.
export const HTML_CSP = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

const QUERY_KEYS = ['lang', 'format', 'version'];

/**
 * What a URL asks for (§4): `{ lang, format, version }`, where `version` is a number for a
 * permalink or null for the latest, or `{ status, detail }` when it can't be served.
 *
 *   /cv?lang=nb&format=md&version=7    each parameter optional
 *   /cv.pdf                            same as /cv
 *   /cv/{lang}.{format}
 *   /cv/v/{version}/{lang}.{format}
 *
 * Path forms take everything from the path, so lang, format or version in their query is an
 * error rather than silently ignored. Other parameters (utm_source, ...) are always ignored.
 */
export function parseTarget(url) {
  const { pathname, searchParams } = url;
  let lang, format, version;

  if (pathname === '/cv') {
    for (const key of QUERY_KEYS) {
      if (searchParams.getAll(key).length > 1) {
        return { status: 400, detail: `Give ${key} only once.` };
      }
    }
    lang = searchParams.get('lang') ?? DEFAULT_LOCALE;
    format = searchParams.get('format') ?? DEFAULT_FORMAT;
    version = searchParams.get('version');
  } else {
    if (QUERY_KEYS.some(key => searchParams.has(key))) {
      return { status: 400, detail: `${pathname} already says what to serve; use /cv?lang=…&format=…&version=… for parameters.` };
    }
    let match;
    if (pathname === '/cv.pdf') {
      [lang, format, version] = [DEFAULT_LOCALE, 'pdf', null];
    } else if ((match = /^\/cv\/([^/]+)\.([^/.]+)$/.exec(pathname))) {
      [, lang, format] = match;
      version = null;
    } else if ((match = /^\/cv\/v\/([^/]+)\/([^/]+)\.([^/.]+)$/.exec(pathname))) {
      [, version, lang, format] = match;
    } else {
      return { status: 404, detail: 'There is nothing at this address. Try /cv/en.pdf.' };
    }
  }

  if (!LOCALES.includes(lang)) {
    return { status: 400, detail: `The language must be one of ${LOCALES.join(', ')}.` };
  }
  if (!Object.hasOwn(FORMATS, format)) {
    return { status: 400, detail: `The format must be one of ${Object.keys(FORMATS).join(', ')}.` };
  }
  if (version !== null) {
    if (!/^[1-9][0-9]{0,9}$/.test(version) || Number(version) > MAX_VERSION) {
      return { status: 400, detail: 'The version must be a version number such as 7.' };
    }
    version = Number(version);
  }
  return { lang, format, version };
}

/** One cache entry per file, whichever URL form asked for it. */
export function canonicalUrl(origin, { lang, format, version }) {
  return `${origin}/cv?lang=${lang}&format=${format}${version === null ? '' : `&version=${version}`}`;
}

/**
 * Serves a request.
 *
 * @param {Request} request
 * @param {object} deps
 * @param {(lang: string, format: string, version: number | null) => Promise<StoredFile | null>} deps.findFile
 *   cv.cv_public_render: `{ versionNumber, name, content, contentHash }` or null. May throw.
 * @param {Cache} [deps.cache] Cloudflare's edge cache (caches.default), or none.
 * @param {(promise: Promise<unknown>) => void} [deps.waitUntil] Work that may finish after the response.
 * @param {{ error: Function }} [deps.log]
 */
export async function handle(request, { findFile, cache, waitUntil = () => {}, log = console }) {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    return problem(405, 'Only GET and HEAD are supported.', { Allow: 'GET, HEAD' });
  }

  const url = new URL(request.url);
  const target = parseTarget(url);
  if (target.status) {
    return finish(request, problem(target.status, target.detail));
  }

  const key = new Request(canonicalUrl(url.origin, target));
  let response = cache ? await cache.match(key) : undefined;
  if (!response) {
    let file;
    try {
      file = await findFile(target.lang, target.format, target.version);
    } catch (error) {
      log.error('cv download: the database query failed', error);
      return finish(request, problem(503, 'Downloads are unavailable right now. Try again in a minute.', { 'Retry-After': '60' }));
    }
    if (!file) {
      return finish(request, problem(404, target.version === null
        ? `No CV is published in ${target.lang} right now.`
        : `Version ${target.version} has not been published in ${target.lang}.`));
    }

    response = fileResponse(file, target);
    if (cache) {
      waitUntil(cache.put(key, response.clone()));
    }
  }

  return finish(request, response);
}

/** The stored file with the headers of §4. */
export function fileResponse(file, { lang, format, version }) {
  const type = FORMATS[format];
  const fileName = `${file.name ? `${file.name} – CV` : 'CV'} (${lang}).${format}`;
  const headers = new Headers({
    'Content-Type': type.contentType,
    'Content-Length': String(file.content.byteLength),
    'Content-Disposition': contentDisposition(type.disposition, fileName),
    'ETag': `"${hex(file.contentHash)}"`,
    'Cache-Control': `public, max-age=${version === null ? LATEST_MAX_AGE : PINNED_MAX_AGE}`,
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'no-referrer',
    'CV-Version': String(file.versionNumber), // which version "latest" was
  });
  if (format === 'html') {
    headers.set('Content-Security-Policy', HTML_CSP);
  }
  return new Response(file.content, { status: 200, headers });
}

/**
 * RFC 6266: an ASCII `filename` for old clients and an RFC 5987 `filename*` for the real name
 * ("Ada Lovelace – CV (nb).pdf"), as ADR 0001 asks.
 */
export function contentDisposition(disposition, fileName) {
  const ascii = fileName
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')       // é -> e, å -> a
    .replace(/[æøœÆØŒß]/g, c => LETTERS[c]) // letters NFKD doesn't decompose
    .replace(/[–—]/g, '-')        // – -> -
    .replace(/[^\x20-\x7e]|["\\/:*?<>|]/g, '_');
  const encoded = encodeURIComponent(fileName).replace(/['()*]/g, c => `%${c.charCodeAt(0).toString(16).toUpperCase()}`);
  return `${disposition}; filename="${ascii}"; filename*=UTF-8''${encoded}`;
}

const LETTERS = { æ: 'ae', ø: 'o', œ: 'oe', Æ: 'AE', Ø: 'O', Œ: 'OE', ß: 'ss' };

/** Answers conditional and HEAD requests from a full response. */
function finish(request, response) {
  const etag = response.headers.get('ETag');
  if (etag && response.status === 200 && matches(request.headers.get('If-None-Match'), etag)) {
    const headers = new Headers();
    for (const name of ['ETag', 'Cache-Control', 'CV-Version']) {
      if (response.headers.has(name)) headers.set(name, response.headers.get(name));
    }
    return new Response(null, { status: 304, headers });
  }
  if (request.method === 'HEAD') {
    return new Response(null, { status: response.status, headers: response.headers });
  }
  return response;
}

function matches(ifNoneMatch, etag) {
  if (!ifNoneMatch) return false;
  return ifNoneMatch.split(',').map(t => t.trim().replace(/^W\//, '')).some(t => t === '*' || t === etag);
}

function problem(status, detail, extraHeaders = {}) {
  return new Response(`${detail}\n`, {
    status,
    headers: {
      'Content-Type': 'text/plain; charset=utf-8',
      'Cache-Control': 'no-store',
      'X-Content-Type-Options': 'nosniff',
      ...extraHeaders,
    },
  });
}

function hex(bytes) {
  return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
}
