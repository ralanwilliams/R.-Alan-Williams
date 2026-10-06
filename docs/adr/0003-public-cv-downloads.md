# ADR 0003: Public CV downloads: files stored on publish, served from the database

- **Status:** Accepted
- **Date:** 2026-10-06
- **Implementation:** `src/Cv.Core/Rendering` (`MarkdownRenderer`); planned: `src/Cv.Data/Migrations` (stored renders, `cv_public` role), `src/Cv.Editor` (rendering on publish), `functions/cv` (the public endpoint), a keep-alive Worker

## Context

[ADR 0001](0001-cv-content-model.md) §7 promises public download links that always serve the current published CV, per language, plus permalinks to exactly what an employer received. [ADR 0002](0002-cv-editor.md) built the editor and a renderer, but downloads exist only inside the editor, and its follow-ups leave open how the public side gets the files: object storage written on publish, or the site querying Supabase per request.

The constraints:

- **PDFs need Chromium.** They are printed by the author's installed Chrome or Edge (ADR 0002 §8). The site is static files on Cloudflare Pages with JavaScript Functions, which cannot run a browser. So files must be produced when publishing, not when downloading.
- **No running costs, and no bill from an attack.** Every service on the public path must be free, and its free tier must refuse or limit traffic past its quota rather than charge for it.
- **Drafts never leak** (ADR 0001 §7), even through a guessable URL.
- **Supabase's free plan pauses a project after 7 days without activity.** A paused database serves nothing until it is resumed by hand.

## Decision

### 1. Rendered files live in the database

When a version is published for a locale, the editor renders it in every format and stores the bytes in `cv_renders`, the cache table ADR 0001 already defined. Its `storage_key` (a pointer to a file stored elsewhere) is replaced by a `content` column holding the file itself. A CHECK requires `content_hash = sha256(content)`.

The formats are the three `cv_renders` already allows: **PDF**, **HTML** (standalone, with its stylesheet inlined) and **Markdown**. One publish of three locales is nine files, well under a megabyte. The free plan's 500 MB holds hundreds of publishes.

The site reads files from the database through a Pages Function. Object storage (Cloudflare R2) was the alternative. It keeps the database out of the download path but needs a payment method and bills past its free tier, which an attacker could exploit (see *Alternatives considered*).

### 2. Publishing requires files; the database enforces it

The publish trigger (`cv.check_publication`, ADR 0001 §7) gains one rule: a version can be published for a locale only if `cv_renders` holds that version and locale in every format. Unpublishing (`version_id` NULL) is unaffected.

So the editor renders and inserts the files **before** the publication rows, in the same transaction (the order ADR 0002 §7 already uses for Save & Publish). A publish whose rendering fails publishes nothing. Republishing an older version needs no new files, because it already has them.

When the renderer's output changes, `RendererVersion` is bumped and a backfill command renders the published versions again. The new rows sit beside the old ones, because `cv_renders` is append-only. The newest row per version, locale and format is served.

### 3. A public role that can read published files and nothing else

A new NOLOGIN role, **`cv_public`**, has no access to any table. It may only EXECUTE one function, `cv.cv_public_render(locale, format, version_number)`:

- With no version number it serves the version `cv.cv_published` lists for that locale.
- With a version number it serves that version only if a `cv_publications` row has **ever** published it for that locale. A draft returns nothing, whether or not someone guesses its number.
- It returns the newest render's bytes, hash and version number, or no row.

The function is `SECURITY DEFINER` with `search_path = ''`, owned by the schema owner, like the read-side helpers in ADR 0001 §11. The site logs in as **`cv_web`**, a member of `cv_public`, created by hand like `cv_api` (it has a password, so it does not belong in a migration). Its connection string is a Cloudflare secret. If it leaks, the holder can read what is already public.

`cv_public` is a new database role, not Supabase's `anon` role, and the `cv` schema stays out of the Data API (ADR 0001 §11).

### 4. URLs

One endpoint with optional parameters:

```
/cv?lang=nb&format=md&version=7
```

| Parameter | Values | Default |
|---|---|---|
| `lang` | `en`, `nb`, `fr` (the publishable locales) | `en` |
| `format` | `pdf`, `html`, `md` | `pdf` |
| `version` | a version number: digits, no leading zero | the latest version published for `lang` |

Path forms are served the same way (not redirected), so links stay short:

| Path | Same as |
|---|---|
| `/cv.pdf` | `/cv` |
| `/cv/{lang}.{format}` | `/cv?lang={lang}&format={format}` |
| `/cv/v/{version}/{lang}.{format}` | `/cv?lang={lang}&format={format}&version={version}` |

- **`version` is the version number, not the UUID.** ADR 0001 §7 used `/cv/v/{version_id}/` so that a permalink could not be guessed. A number is guessable, but §3 means guessing only finds versions that were already public for that language. A number is also something a person can read and type.
- **The default language is fixed** (English). As ADR 0001 §7 says, `Accept-Language` is not used, so a link always returns the same file.
- **Responses:** an invalid value is `400`. A version or locale with nothing to serve is `404`. Methods other than `GET` and `HEAD` are `405`. Unknown parameters such as `utm_source` are ignored, because link-sharing sites add them.
- **Headers:** the format's `Content-Type`; `Content-Disposition` with `filename*=UTF-8''…` and an ASCII fallback (ADR 0001 follow-up), `inline` for PDF and HTML and `attachment` for Markdown; `ETag` from `content_hash`; `X-Content-Type-Options: nosniff`. HTML also gets a Content-Security-Policy that allows no scripts.

### 5. Caching instead of purging

The Function caches responses at Cloudflare's edge:

- **Latest-version URLs:** 5 minutes. A publish shows up within 5 minutes, with no purge call, so the editor needs no Cloudflare API token.
- **Pinned-version URLs:** 1 day. Not `immutable`, because a renderer bump re-renders old versions.

Caching keeps repeated downloads away from the database. A rate-limiting rule on `/cv*` (the free plan includes one) limits what is not cached.

### 6. A keep-alive keeps Supabase awake

A small Cloudflare Worker with a daily Cron Trigger calls `cv.cv_public_render` as `cv_web`. Without it, a quiet week pauses the database and every download link fails until the project is resumed by hand. Cached downloads make this more likely, because they never reach the database. Pages Functions cannot run on a schedule, so this is a separate Worker.

### 7. What this ADR does not cover

- **Counting downloads** (ADR 0001 follow-up) is deferred. Counting from the Function would need `INSERT` on `audit_log` for `cv_public`, which widens a role that is now read-only. Cloudflare's own analytics show request counts in the meantime.
- **Hosting the editor** is a separate decision. Whichever editor publishes, local or hosted, renders and stores the files as in §2.

## Consequences

**Positive**

- Download links never change and never go stale: `/cv/en.pdf` always serves the current English CV.
- No drafts through the public path, enforced by the database rather than by the Function.
- Every service on the public path refuses traffic past its free quota instead of billing for it. No payment method is involved.
- Downloads match what the editor printed, byte for byte, because they are the editor's own output.
- Integrity stays in the database (ADR 0001 §10): a version cannot be public without its files.

**Negative / costs**

- Downloads depend on Supabase. If it is down or paused, so are the links. The keep-alive covers pausing; nothing covers an outage.
- A database credential now lives in Cloudflare. It is read-only and can only reach public data.
- Publishing needs Chromium, so it fails on a machine with no Chrome, Edge or Chromium. It used to need Chromium only for Download PDF.
- File bytes in Postgres make the database larger than text alone would. At about a megabyte per publish this is irrelevant here, but it would not scale to many large files.
- The keep-alive exists only to satisfy a free-tier rule, and that rule could change.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Write files to Cloudflare R2 on publish | Independent of the database at download time, but R2 needs a payment method and charges for reads past its free tier, so an attack could cost money. |
| Publish files as static assets on Pages (a deployment per publish) | Free and fast, but the editor would need Cloudflare's deployment API and an API token, and every publish would be a site deployment. |
| Render on request in a hosted service | PDFs need Chromium, so this means a public container. Every request reaching it is billed, so it reopens the attack-cost problem and adds cold starts. |
| Supabase's Data API with RLS | Exposes the `cv` schema that ADR 0001 §11 deliberately keeps out of it, and RLS policies are one more thing to get exactly right. |
| Grant `cv_public` SELECT on a view | A view cannot take parameters. The "ever published for this locale" rule is simpler and safer inside one function than spread across view and query. |
| UUID permalinks (ADR 0001 §7) | Safe already without being unguessable (§3), and impossible to type. |
| Purge the cache on publish | Needs a Cloudflare API token in the editor for an improvement measured in minutes. |

## Follow-ups

- **Download counts:** an append-only counter the Function can write without reading anything, or Cloudflare Analytics Engine.
- **Hosting the editor** behind Cloudflare Access, so it can publish from anywhere: its own ADR.
