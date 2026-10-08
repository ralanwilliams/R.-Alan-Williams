# Public CV downloads: setup and use

The site serves the published CV at `/cv` URLs, for example `/cv/en.pdf`. A Cloudflare Pages Function reads the files the editor stored when publishing (see [cv-editor.md](cv-editor.md#4-using-the-editor)) from Supabase, through Hyperdrive, as the read-only `cv_web` login. A small Worker queries the database once a day, so Supabase's free plan never pauses it. The design is in [ADR 0003](adr/0003-public-cv-downloads.md).

```
functions/                    Pages routes: /cv.pdf, /cv, /cv/* (each re-exports the handler)
src/Cv.Public/
├── download.js               URL forms, validation, headers, caching (no Cloudflare or database code)
├── database.js               cv.cv_public_render through node-postgres
├── keep-alive.js             the daily query and its log line
└── pages.js                  the Pages Function: download.js + Hyperdrive + the edge cache
workers/keep-alive/           the keep-alive Worker: a Cron Trigger, its own wrangler.toml
wrangler.toml                 Pages settings: output folder, nodejs_compat, the CV_DB Hyperdrive binding
package.json                  pg (runtime), wrangler (local dev, deploying the Worker, CI bundle checks)
tests/Cv.Public.Js/           Node tests for download.js and keep-alive.js
```

## URLs

| URL | Serves |
|---|---|
| `/cv`, `/cv.pdf`, `/cv/en.pdf` | The current English PDF |
| `/cv/{lang}.{format}` | The current CV in `en`, `nb` or `fr`, as `pdf`, `html` or `md` |
| `/cv/v/{version}/{lang}.{format}` | Version `{version}`, if it was ever published in that language: a permalink to what someone received |
| `/cv?lang=nb&format=md&version=7` | The same, as parameters. Each one is optional (`en`, `pdf`, latest) |

- **Path forms take everything from the path.** `lang`, `format` or `version` in their query string is a `400`. Other parameters, such as `utm_source`, are ignored everywhere.
- **Responses:**
  - An invalid value is `400`.
  - Nothing published, or a version never published in that language, is `404`. Drafts are never served (ADR 0003 §3).
  - Any method other than `GET` or `HEAD` is `405`.
  - If the database can't be reached, the response is `503` with `Retry-After: 60`. The cause is logged, not shown.
- **Headers:**
  - `Content-Disposition`: the file name in full plus an ASCII fallback, e.g. `R. Alan Williams – CV (nb).pdf`. PDF and HTML open in the browser (`inline`); Markdown downloads (`attachment`).
  - `ETag`: the file's SHA-256, so `If-None-Match` gets a `304`.
  - `CV-Version`: the version number that was served.
  - `X-Content-Type-Options: nosniff` on everything.
  - HTML also gets a Content Security Policy that allows no scripts.
- **Caching:**
  - Current-version URLs are cached for 5 minutes, permalinks for a day, at Cloudflare's edge and in browsers. A publish shows up within 5 minutes; nothing needs purging.
  - Every URL form of the same file shares one cache entry.
  - Errors are never cached.
  - The edge cache only works on the custom domain. On `*.pages.dev` every download queries the database.

## One-time setup

Do this **before merging** the PR that adds `wrangler.toml`. The first build after the merge reads it, and needs the Hyperdrive id in it.

### 1. The `cv_web` login

Create it as in [cv-database.md §6](cv-database.md#6-one-time-setup-after-the-migration) if you haven't already. It can only call `cv.cv_public_render`, so it can only read what is already public.

### 2. Create the Hyperdrive config

Hyperdrive keeps a pool of connections to Supabase, so a download doesn't pay for a new TLS handshake and login. It holds the `cv_web` connection string, encrypted. The free plan allows 100,000 queries a day; past that, queries fail until midnight UTC rather than being billed.

```powershell
npm install                     # once: installs wrangler (and pg)
npx wrangler login              # opens the browser; pick the account that owns the Pages project
$pw = [uri]::EscapeDataString('<cv_web password>')   # single quotes: PowerShell leaves # ] $ alone
npx wrangler hyperdrive create cv-web --caching-disabled --connection-string="postgres://cv_web.<project-ref>:$pw@aws-0-<region>.pooler.supabase.com:5432/postgres"
```

- Use the **session pooler** host and port `5432`, as everywhere else ([cv-database.md §3](cv-database.md#3-get-the-supabase-connection-string)). The user is `cv_web.<project-ref>`.
- This is a URI, not the Npgsql key/value format, so the password must be percent-encoded. `EscapeDataString` does that. Otherwise characters such as `#`, `]` or `@` make wrangler fail with *Invalid URL*. A password of letters and digits needs no encoding at all.
- `--caching-disabled`: the Function caches at the edge already, and the keep-alive (ADR 0003 §6) must reach the database every time. Hyperdrive wouldn't cache this query anyway, because `cv_public_render` is `STABLE`, but this makes it explicit.
- Hyperdrive tries the connection when you create the config, so a typo fails here and not in production.

The command prints an `id`. Put it in `wrangler.toml` in place of `<hyperdrive-config-id>`. The id is not a secret: it means nothing without your Cloudflare account.

### 3. Set the build command, then merge

Pages installs npm packages only when the project has a build command, and the Function needs `pg`. In the dashboard, open **Workers & Pages → ralanwilliams → Settings → Build → Build configuration** and set **Build command** to `npm ci --omit=dev`. This installs exactly what `package-lock.json` pins and skips wrangler, which the build doesn't need. A Pages build command can't be set in `wrangler.toml`.

Pages builds from `master` as before. Because `wrangler.toml` exists, the build takes the output folder (`public`), the `nodejs_compat` flag and the `CV_DB` binding from it. The dashboard then shows those settings but can't edit them. Preview builds of other branches get the same binding, so they serve the same published files.

### 4. Rate-limit `/cv`

Caching keeps repeated downloads away from the database. A rate-limiting rule limits the rest (ADR 0003 §5). The free plan includes one rule. In the dashboard, open the domain's **Security → WAF → Rate limiting rules → Create rule**:

| Setting | Value |
|---|---|
| If incoming requests match | *URI Path* *starts with* `/cv` |
| With the same characteristics | IP (the only choice on the free plan) |
| When rate exceeds | 30 requests per 10 seconds |
| Then | Block, for 10 seconds |

A person downloading the CV in three languages and three formats sends nine requests, so 30 leaves plenty of room.

### 5. Let browsers keep the Function's cache times

Cloudflare's **Browser Cache TTL** (4 hours by default) raises any shorter `max-age` to its own value. That would turn the current version's 5 minutes into 4 hours, so a browser could keep showing an old CV for hours after a publish. In the dashboard, open the domain's **Caching → Configuration → Browser Cache TTL** and choose **Respect Existing Headers**. The rest of the site already sends its own headers (`max-age=0, must-revalidate`), so nothing else changes.

### 6. Deploy the keep-alive

Supabase's free plan pauses a project after 7 days without activity. Downloads served from the cache never reach the database, so even a CV that people download could go quiet. The keep-alive Worker queries the database every day at 04:17 UTC as `cv_web`, through the same Hyperdrive config (ADR 0003 §6).

Pages doesn't build Workers, so deploy it by hand. Do it once now, and again whenever `workers/keep-alive/` or `src/Cv.Public/database.js` changes:

```powershell
npm run deploy:keep-alive       # wrangler deploy --config workers/keep-alive/wrangler.toml
```

It has no URL (`workers_dev = false`): the schedule is its only trigger. It uses one of the free plan's five Cron Triggers, and one Hyperdrive query a day.

To check it, open **Workers & Pages → cv-keep-alive**. Its settings list the cron trigger `17 4 * * *`. After the first run, at the next 04:17 UTC, its logs show `keep-alive: database reached; en serves vN`. To see a run without waiting, use the local run under *Local development*.

### 7. Check it

```powershell
curl.exe -I https://<your domain>/cv/en.pdf     # 200, application/pdf, CV-Version: n, Cache-Control: public, max-age=300
curl.exe -I https://<your domain>/cv/v/999/en.pdf   # 404
```

If the current version returns `404`, that language isn't published, or its files were never stored: run the backfill ([cv-editor.md](cv-editor.md#backfilling-public-files)).

## Local development

```powershell
npm install
$env:CLOUDFLARE_HYPERDRIVE_LOCAL_CONNECTION_STRING_CV_DB = "postgres://<user>:<password>@127.0.0.1:5432/<database>"
npm run dev                      # http://localhost:8788/cv/en.pdf
```

`npm run dev` runs the site and the Function in workerd, Cloudflare's runtime. Hyperdrive isn't involved locally: the Function connects straight to the local connection string. Point it at a local or throwaway PostgreSQL with the migrations applied, files stored, and a login in `cv_public`. Don't point it at Supabase. Wrangler also loads the variables from `.env` into the local Function. The Function doesn't use them, and they never leave your computer.

To run the keep-alive locally, with the same variable set:

```powershell
npm run dev:keep-alive                                          # serves on http://localhost:8787
curl.exe "http://localhost:8787/__scheduled?cron=17+4+*+*+*"    # runs the cron handler once; the log line appears in the first window
```

## Tests

```powershell
npm test                         # editor model, /cv endpoint and keep-alive (Node test runner)
npm run build:functions          # bundles the Functions the way Pages does
npm run build:keep-alive         # bundles the keep-alive Worker (a deploy dry run)
```

The endpoint's tests cover every URL form, validation, headers, `HEAD`, `304`, errors, and the shared cache entries, with a fake database and cache. The keep-alive's tests check its log lines, and that a failure fails the run. The database function behind it is tested in `tests/Cv.Data.Tests` (`PublicRenderTests`) and `tests/db/schema-tests.sql`. CI runs all of them.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Pages build fails with *Could not resolve "pg"*, and the log says *No build command specified* | Pages didn't install the npm packages. Set the build command (step 3). |
| Pages build fails with *Could not resolve "string_decoder"* (or another Node module) | The `nodejs_compat` flag is missing. It comes from `wrangler.toml`, so check that the file is in the build. |
| Pages build fails about the Hyperdrive binding or id | `wrangler.toml` still has `<hyperdrive-config-id>`, or the id is from another account. See step 2. |
| Every download is `503` | The function log (**Workers & Pages → ralanwilliams → Functions → Real-time logs**) has the cause. It is usually a wrong password or host in the Hyperdrive config (`npx wrangler hyperdrive update <id> --connection-string=…`), a paused Supabase project (resume it in the dashboard), or the daily Hyperdrive quota. |
| `404` for a language that is published | Its files were never stored. Run the backfill ([cv-editor.md](cv-editor.md#backfilling-public-files)). |
| A new publish doesn't show up | Current-version URLs are cached for up to 5 minutes, at the edge and in the browser. The `CV-Version` header says which version you got. The new version's permalink (`/cv/v/{n}/…`) works at once. |
| `Cache-Control: public, max-age=14400` on `/cv/en.pdf` instead of `max-age=300` | Cloudflare's Browser Cache TTL is overriding the Function. Set it to *Respect Existing Headers* (step 5). |
| `wrangler hyperdrive create` fails with *Invalid URL* | The password has characters that must be percent-encoded. Use `EscapeDataString` (step 2). |
| The keep-alive's log shows *could not reach the database* | Same causes as a `503` above. The run is marked failed, but the project only pauses after 7 quiet days, so there is time to fix it. If Supabase has paused already, resume the project in its dashboard. |
| `npm run deploy:keep-alive` says you're not logged in, or picks the wrong account | `npx wrangler login`, or set `CLOUDFLARE_ACCOUNT_ID` (it's in `.env`; load it first). |
