// Reads published files through cv.cv_public_render (ADR 0003 §3), as cv_web: the only thing
// that login can do.
//
// The connection string comes from the Hyperdrive binding, which pools connections to Supabase,
// so a new client per request is cheap: Hyperdrive has already done the TLS handshake and login.
import pg from 'pg';

const FILE_QUERY = 'SELECT version_number, name, content, content_hash FROM cv.cv_public_render($1, $2, $3)';
const PING_QUERY = "SELECT version_number FROM cv.cv_public_render('en', 'pdf')";

/**
 * @returns {Promise<{ versionNumber: number, name: string | null, content: Uint8Array, contentHash: Uint8Array } | null>}
 */
export async function findFile(connectionString, lang, format, version, waitUntil = p => p) {
  const rows = await query(connectionString, FILE_QUERY, [lang, format, version], waitUntil);
  if (rows.length === 0) {
    return null;
  }
  const [row] = rows;
  return { versionNumber: row.version_number, name: row.name, content: row.content, contentHash: row.content_hash };
}

/**
 * A real query, so Supabase counts the project as active (ADR 0003 §6). Returns the version
 * English serves, or null when nothing is published; either way the database was reached.
 */
export async function ping(connectionString, waitUntil = p => p) {
  const rows = await query(connectionString, PING_QUERY, [], waitUntil);
  return rows[0]?.version_number ?? null;
}

async function query(connectionString, sql, parameters, waitUntil) {
  const client = new pg.Client({ connectionString });
  await client.connect();
  try {
    return (await client.query(sql, parameters)).rows;
  } finally {
    waitUntil(client.end()); // closing needn't delay the response
  }
}
