// The Pages Function behind every /cv URL (functions/cv.pdf.js, functions/cv/...): the request
// handling in download.js, with the database behind the CV_DB Hyperdrive binding (wrangler.toml)
// and Cloudflare's edge cache.
import { handle } from './download.js';
import { findFile } from './database.js';

export function onRequest(context) {
  const waitUntil = promise => context.waitUntil(promise);
  return handle(context.request, {
    findFile: (lang, format, version) => findFile(context.env.CV_DB.connectionString, lang, format, version, waitUntil),
    cache: caches.default,
    waitUntil,
  });
}
