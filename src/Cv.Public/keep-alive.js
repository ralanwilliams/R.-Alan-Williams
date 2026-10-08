// The keep-alive (ADR 0003 §6): Supabase's free plan pauses a project after 7 days without
// activity, and cached downloads never reach the database. workers/keep-alive runs this daily.

/**
 * Queries the database once and logs what it found. A failure is rethrown, so Cloudflare marks
 * the cron run as failed and it shows up in the Worker's logs.
 *
 * @param {() => Promise<number | null>} ping database.js ping, bound to the connection string.
 */
export async function keepAlive(ping, log = console) {
  let version;
  try {
    version = await ping();
  } catch (error) {
    log.error('keep-alive: could not reach the database', error);
    throw error;
  }
  log.log(version === null
    ? 'keep-alive: database reached; nothing is published in en'
    : `keep-alive: database reached; en serves v${version}`);
  return version;
}
