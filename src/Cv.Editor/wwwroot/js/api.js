// Thin client for the editor API. Failures arrive as RFC 7807 problem documents with a
// stable `code` (conflict, invalid, no-changes, not-ready, confirm-stale, rejected).

export class ApiError extends Error {
  constructor(status, problem) {
    super(problem?.detail || problem?.title || `Request failed (${status})`);
    this.status = status;
    this.problem = problem ?? {};
    this.code = this.problem.code ?? null;
  }
}

async function request(method, url, body) {
  const response = await fetch(url, {
    method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  const type = response.headers.get('Content-Type') ?? '';
  if (!response.ok) {
    const problem = type.includes('json') ? await response.json().catch(() => null) : null;
    throw new ApiError(response.status, problem);
  }
  if (type.includes('application/pdf')) {
    return { blob: await response.blob(), fileName: fileNameFrom(response.headers.get('Content-Disposition')) };
  }
  return type.includes('json') ? response.json() : null;
}

/** Prefers the RFC 5987 `filename*` (UTF-8) over the ASCII fallback. */
function fileNameFrom(disposition) {
  const extended = /filename\*=UTF-8''([^;]+)/i.exec(disposition ?? '');
  if (extended) return decodeURIComponent(extended[1]);
  return /filename="?([^";]+)"?/i.exec(disposition ?? '')?.[1] ?? 'cv.pdf';
}

export const api = {
  session: () => request('GET', '/api/session'),
  currentDocument: () => request('GET', '/api/document'),
  history: () => request('GET', '/api/versions'),
  version: id => request('GET', `/api/versions/${id}`),
  changes: (from, to) => request('GET', `/api/versions/${from}/changes/${to}`),
  analyze: (baseVersionId, document, locale) => request('POST', '/api/analyze', { baseVersionId, document, locale }),
  save: body => request('POST', '/api/versions', body),
  restore: (id, baseVersionId) => request('POST', `/api/versions/${id}/restore`, { baseVersionId }),
  publish: (versionId, locales, confirmStale = false) => request('POST', '/api/publications', { versionId, locales, confirmStale }),
  pdf: (baseVersionId, document, locale) => request('POST', '/api/pdf', { baseVersionId, document, locale }),
};
