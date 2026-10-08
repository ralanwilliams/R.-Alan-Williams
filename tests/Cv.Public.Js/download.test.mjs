// Tests for the public /cv endpoint's request handling (src/Cv.Public/download.js), with a fake
// database and a fake edge cache. The database function itself is tested in Cv.Data.Tests.
//   node --test "tests/Cv.Public.Js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { handle, parseTarget, contentDisposition, HTML_CSP, LATEST_MAX_AGE, PINNED_MAX_AGE } from '../../src/Cv.Public/download.js';

const ORIGIN = 'https://example.com';

/** Published: en v1 and v2 (latest), nb v1 (latest). */
function fakeDatabase() {
  const files = new Map();
  const add = (version, lang, format, text) => {
    const content = new TextEncoder().encode(text);
    files.set(`${lang}/${format}/${version}`, {
      versionNumber: version,
      name: 'Ådne Øvrebø',
      content,
      contentHash: createHash('sha256').update(content).digest(),
    });
  };
  for (const format of ['pdf', 'html', 'md']) {
    add(1, 'en', format, `v1 en ${format}`);
    add(2, 'en', format, `v2 en ${format}`);
    add(1, 'nb', format, `v1 nb ${format}`);
  }
  const latest = { en: 2, nb: 1 };
  const calls = [];
  const findFile = async (lang, format, version) => {
    calls.push([lang, format, version]);
    const v = version ?? latest[lang];
    return files.get(`${lang}/${format}/${v}`) ?? null;
  };
  return { findFile, calls };
}

function fakeCache() {
  const entries = new Map();
  return {
    entries,
    match: async request => entries.get(request.url)?.clone(),
    put: async (request, response) => { entries.set(request.url, response); },
  };
}

async function get(path, { method = 'GET', headers = {}, db = fakeDatabase(), cache, findFile } = {}) {
  const pending = [];
  const response = await handle(new Request(`${ORIGIN}${path}`, { method, headers }), {
    findFile: findFile ?? db.findFile,
    cache,
    waitUntil: p => pending.push(p),
    log: { error: () => {} },
  });
  await Promise.all(pending);
  return response;
}

// --- URL forms (ADR 0003 §4) ---------------------------------------------------------------

test('every URL form names the same file', () => {
  const latestEnPdf = { lang: 'en', format: 'pdf', version: null };
  for (const path of ['/cv', '/cv.pdf', '/cv/en.pdf', '/cv?lang=en', '/cv?format=pdf', '/cv?utm_source=linkedin']) {
    assert.deepEqual(parseTarget(new URL(path, ORIGIN)), latestEnPdf, path);
  }
  assert.deepEqual(parseTarget(new URL('/cv?lang=nb&format=md&version=7', ORIGIN)), { lang: 'nb', format: 'md', version: 7 });
  assert.deepEqual(parseTarget(new URL('/cv/v/7/nb.md', ORIGIN)), { lang: 'nb', format: 'md', version: 7 });
  assert.deepEqual(parseTarget(new URL('/cv/fr.html', ORIGIN)), { lang: 'fr', format: 'html', version: null });
});

test('invalid values are 400', () => {
  for (const path of [
    '/cv?lang=de', '/cv?lang=EN', '/cv?lang=zxx', '/cv?lang=', '/cv?format=docx', '/cv/en.PDF',
    '/cv?version=0', '/cv?version=07', '/cv?version=-1', '/cv?version=1.5', '/cv?version=abc', '/cv?version=',
    '/cv?version=2147483648', '/cv?version=99999999999', '/cv/v/007/en.pdf', '/cv/de.pdf',
    '/cv?lang=en&lang=nb',                        // ambiguous
    '/cv/en.pdf?lang=nb', '/cv.pdf?version=1',     // a path form says it all already
  ]) {
    assert.equal(parseTarget(new URL(path, ORIGIN)).status, 400, path);
  }
});

test('other paths under /cv are 404', () => {
  for (const path of ['/cv/', '/cv/en', '/cv/v/7', '/cv/v/7/en', '/cv/en/pdf', '/cv/a/b.pdf', '/cv/v/7/x/en.pdf']) {
    assert.equal(parseTarget(new URL(path, ORIGIN)).status, 404, path);
  }
});

// --- responses ---------------------------------------------------------------------------------

test('the latest PDF is served inline with its name, hash and a 5 minute cache', async () => {
  const response = await get('/cv/en.pdf');

  assert.equal(response.status, 200);
  assert.equal(await response.text(), 'v2 en pdf');
  assert.equal(response.headers.get('Content-Type'), 'application/pdf');
  assert.equal(response.headers.get('Content-Length'), '9');
  assert.equal(response.headers.get('Cache-Control'), `public, max-age=${LATEST_MAX_AGE}`);
  assert.equal(response.headers.get('ETag'), `"${createHash('sha256').update('v2 en pdf').digest('hex')}"`);
  assert.equal(response.headers.get('CV-Version'), '2');
  assert.equal(response.headers.get('X-Content-Type-Options'), 'nosniff');
  assert.equal(response.headers.get('Content-Disposition'),
    `inline; filename="Adne Ovrebo - CV (en).pdf"; filename*=UTF-8''%C3%85dne%20%C3%98vreb%C3%B8%20%E2%80%93%20CV%20%28en%29.pdf`);
  assert.equal(response.headers.get('Content-Security-Policy'), null);
});

test('a permalink serves what was published then, cached for a day', async () => {
  const response = await get('/cv/v/1/en.pdf');

  assert.equal(await response.text(), 'v1 en pdf');
  assert.equal(response.headers.get('Cache-Control'), `public, max-age=${PINNED_MAX_AGE}`);
});

test('HTML gets a policy that allows no scripts; Markdown downloads as a file', async () => {
  const html = await get('/cv?lang=nb&format=html');
  const md = await get('/cv/nb.md');

  assert.equal(html.headers.get('Content-Type'), 'text/html; charset=utf-8');
  assert.equal(html.headers.get('Content-Security-Policy'), HTML_CSP);
  assert.doesNotMatch(HTML_CSP, /script-src/);
  assert.match(HTML_CSP, /default-src 'none'/);
  assert.equal(md.headers.get('Content-Type'), 'text/markdown; charset=utf-8');
  assert.match(md.headers.get('Content-Disposition'), /^attachment; /);
  assert.equal(await md.text(), 'v1 nb md');
});

test('nothing published, or a version never published in that language, is 404 and not cached', async () => {
  const cache = fakeCache();
  const fr = await get('/cv/fr.pdf', { cache });
  const draft = await get('/cv/v/3/en.pdf', { cache });

  assert.equal(fr.status, 404);
  assert.match(await fr.text(), /No CV is published in fr/);
  assert.equal(draft.status, 404);
  assert.equal(draft.headers.get('Cache-Control'), 'no-store');
  assert.equal(cache.entries.size, 0);
});

test('a database failure is 503 with Retry-After, and is not cached', async () => {
  const cache = fakeCache();
  const response = await get('/cv/en.pdf', { cache, findFile: async () => { throw new Error('connection refused'); } });

  assert.equal(response.status, 503);
  assert.equal(response.headers.get('Retry-After'), '60');
  assert.doesNotMatch(await response.text(), /connection refused/); // details stay in the log
  assert.equal(cache.entries.size, 0);
});

test('methods other than GET and HEAD are 405', async () => {
  for (const method of ['POST', 'PUT', 'DELETE', 'OPTIONS']) {
    const response = await get('/cv/en.pdf', { method });
    assert.equal(response.status, 405, method);
    assert.equal(response.headers.get('Allow'), 'GET, HEAD');
  }
});

test('HEAD has the headers and no body', async () => {
  const response = await get('/cv/en.pdf', { method: 'HEAD' });

  assert.equal(response.status, 200);
  assert.equal(response.headers.get('Content-Length'), '9');
  assert.equal(await response.text(), '');
});

test('a matching If-None-Match is 304', async () => {
  const first = await get('/cv/en.pdf');
  const etag = first.headers.get('ETag');

  const same = await get('/cv/en.pdf', { headers: { 'If-None-Match': `"other", W/${etag}` } });
  const changed = await get('/cv/en.pdf', { headers: { 'If-None-Match': '"other"' } });

  assert.equal(same.status, 304);
  assert.equal(same.headers.get('ETag'), etag);
  assert.equal(await same.text(), '');
  assert.equal(changed.status, 200);
});

// --- caching (ADR 0003 §5) --------------------------------------------------------------------

test('every URL form of a file shares one cache entry, so the database is asked once', async () => {
  const db = fakeDatabase();
  const cache = fakeCache();

  for (const path of ['/cv', '/cv.pdf', '/cv/en.pdf', '/cv?lang=en&format=pdf&utm_source=x']) {
    const response = await get(path, { db, cache });
    assert.equal(await response.text(), 'v2 en pdf', path);
  }
  const head = await get('/cv/en.pdf', { db, cache, method: 'HEAD' });

  assert.equal(head.status, 200);
  assert.equal(db.calls.length, 1);
  assert.deepEqual([...cache.entries.keys()], [`${ORIGIN}/cv?lang=en&format=pdf`]);
});

test('latest and pinned URLs are cached separately', async () => {
  const db = fakeDatabase();
  const cache = fakeCache();

  await get('/cv/en.pdf', { db, cache });
  await get('/cv/v/2/en.pdf', { db, cache });

  assert.deepEqual([...cache.entries.keys()].sort(), [`${ORIGIN}/cv?lang=en&format=pdf`, `${ORIGIN}/cv?lang=en&format=pdf&version=2`]);
  assert.equal(db.calls.length, 2);
});

// --- file names ---------------------------------------------------------------------------------

test('file names fall back to ASCII without characters that are unsafe in a header or a path', () => {
  assert.equal(contentDisposition('inline', 'R. Alan Williams – CV (fr).pdf'),
    `inline; filename="R. Alan Williams - CV (fr).pdf"; filename*=UTF-8''R.%20Alan%20Williams%20%E2%80%93%20CV%20%28fr%29.pdf`);
  assert.match(contentDisposition('inline', 'a"b\\c/d.pdf'), /filename="a_b_c_d\.pdf"/);
});
