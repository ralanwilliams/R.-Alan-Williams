// The keep-alive Worker (ADR 0003 §6): once a day, one query as cv_web through Hyperdrive, so
// Supabase never sees a quiet week. It has no URL; Cron Triggers are its only way in.
// Deploy: npm run deploy:keep-alive (docs/cv-public.md).
import { keepAlive } from '../../src/Cv.Public/keep-alive.js';
import { ping } from '../../src/Cv.Public/database.js';

export default {
  async scheduled(controller, env, ctx) {
    await keepAlive(() => ping(env.CV_DB.connectionString, p => ctx.waitUntil(p)));
  },
};
