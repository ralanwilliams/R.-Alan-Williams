// Reads a published file through cv.cv_public_render (ADR 0003 §3), as cv_web.
//
// The connection string comes from the Hyperdrive binding, which pools connections to Supabase,
// so a new client per request is cheap: Hyperdrive has already done the TLS handshake and login.
import pg from 'pg';

const QUERY = 'SELECT version_number, name, content, content_hash FROM cv.cv_public_render($1, $2, $3)';

/**
 * @returns {Promise<{ versionNumber: number, name: string | null, content: Uint8Array, contentHash: Uint8Array } | null>}
 */
export async function findFile(connectionString, lang, format, version, waitUntil = p => p) {
  const client = new pg.Client({ connectionString });
  await client.connect();
  try {
    const { rows } = await client.query(QUERY, [lang, format, version]);
    if (rows.length === 0) {
      return null;
    }
    const [row] = rows;
    return { versionNumber: row.version_number, name: row.name, content: row.content, contentHash: row.content_hash };
  } finally {
    waitUntil(client.end()); // closing needn't delay the response
  }
}
