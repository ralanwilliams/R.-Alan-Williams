# CV editor: setup and use

The editor is a small web app that runs on your own computer. The CV is on the left, one input per line, and a live preview of the result is on the right. It saves versions to the Supabase database described in [cv-database.md](cv-database.md). The design and its trade-offs are in [ADR 0002](adr/0002-cv-editor.md).

```
src/Cv.Core/      document workflow: drafts, ordering, hashing, validation, localisation, HTML rendering
src/Cv.Data/      EF Core model, migrations, and the store the editor uses (Store/)
src/Cv.Editor/    the web app: HTTP API, security middleware, PDF output, public files and backfill (Publishing/), and the page (wwwroot/)
tests/            Cv.Core.Tests, Cv.Data.Tests (PostgreSQL), Cv.Editor.Tests (HTTP), Cv.Editor.Js (browser model)
```

## Tooling

| Tool | Why | Notes |
|---|---|---|
| **.NET 10 SDK** (10.0.100 or later) | Builds and runs everything | See [cv-database.md §1](cv-database.md#1-tooling) |
| **Chrome, Edge or Chromium** | Prints PDFs (Download PDF, publishing) | Found automatically in the usual install locations, or set `CV_CHROMIUM_PATH` |
| Node.js 22+ *(optional)* | Runs the browser-side unit tests | Not needed to use the editor |
| Docker *(optional)* | Starts PostgreSQL for the integration tests | Or point `CV_TEST_POSTGRES` at a disposable server |

Package versions are pinned in `Directory.Packages.props`:

| Package | Version | Used by |
|---|---|---|
| PuppeteerSharp | 25.12.0 | PDF output (drives the installed browser) |
| xunit.v3 | 4.0.1 | Tests (runs on Microsoft.Testing.Platform, enabled in `global.json`) |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | HTTP tests |
| Testcontainers.PostgreSql | 4.15.0 | Integration tests |

## 1. Before the first run

Follow [cv-database.md](cv-database.md) up to §6. The editor needs:

- the migration applied (§4);
- your row in `cv.users` (§6). The editor saves every version under it;
- the `cv_api` login (§6). The editor connects as `cv_api` and never as `postgres`.

## 2. Configure

Add the editor's settings to `.env` in the repository root (git-ignored; `.env.example` lists every key):

```dotenv
# Required: the cv_api login, through the Session pooler.
CV_API_CONNECTION=Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=cv_api.<project-ref>;Password=<cv_api password>;SSL Mode=Require

# Optional: which cv.users row is you. Only needed if the table has more than one row.
CV_EDITOR_USER_EMAIL=

# Optional: the browser used for PDFs, if it isn't found automatically.
CV_CHROMIUM_PATH=

# Optional: a CV outline to start from before the first save (see "Seeding the first version").
CV_SEED_FILE=seed/cv.json
```

## 3. Run

```powershell
. ./scripts/Import-DotEnv.ps1          # loads .env into this window (note the leading dot + space)
dotnet run --project src/Cv.Editor
```

Open <http://localhost:5180>. The editor only listens on this computer (loopback) and only answers to `localhost`, so it can't be reached from your network. To use another port, set `Editor__Port`, e.g. `$env:Editor__Port = 5190`.

Stop it with <kbd>Ctrl</kbd>+<kbd>C</kbd>.

## Seeding the first version

While the database has no versions, the editor opens a **seed file** instead of the empty starter document, if `CV_SEED_FILE` names one. Nothing is saved until you press **Save**. Review the CV first, because version 1 can never be deleted. Edits to the seed file show up when you reload the page, until the first save. After that, the seed file is ignored.

A seed file is a JSON outline of the CV: one object per line, with the text keyed by locale code (`zxx` = the same in every language). Comments are allowed:

```jsonc
{
  "children": [
    { "type": "name", "zxx": "Ada Lovelace" },
    { "type": "contact", "attrs": { "kind": "email" }, "zxx": "ada@example.com" },
    { "type": "section", "en": "Experience", "children": [
      { "type": "entry", "en": "Lead engineer", "attrs": { "start": "2021-03" }, "children": [
        { "type": "subtitle", "zxx": "Analytical Engines Ltd" },   // the organisation
        { "type": "location", "en": "London, UK" },
        { "type": "bullet", "en": "Built the difference engine." }
      ] }
    ] }
  ]
}
```

Line types and what they may contain are listed in `GET /api/session` and in ADR 0001 §9. `"omit": ["fr"]` leaves a line out of a language. A mistake is reported with its position, e.g. `$.children[2].children[0].tittle: unknown property`.

Keep seed files in `seed/`, which is git-ignored: a CV is personal data and this repository is public.

The seed needs the `location` line type, so apply the `AddLocationNodeType` migration first ([cv-database.md §4](cv-database.md#4-apply-the-migration)).

## 4. Using the editor

**Writing.** Each line is one input. Line types follow the document grammar (ADR 0001 §9): name, headline and contacts at the top, then sections containing entries, paragraphs, bullets and skills. Use **⋯** on a line to add a line after it or inside it, move it, or delete it. The buttons under the last line add top-level lines. Entries take dates (`2021` or `2021-03`; leave *To* empty for "present"), and contacts take a type (email, phone, url, location), which decides how the line is linked.

**Languages.** The tabs switch between English (the source), Norsk bokmål and Français. Each tab shows how many lines are **missing** (red) or **out of date** (amber) in that language.

- In a translation tab, each line shows the English text under it. Type the translation into the input.
- **⋯ → Use the same text in every language** (in English) stores the line once for all languages. Use it for your name, email addresses, company names and URLs. A language can still override it.
- **⋯ → Leave out of {language}** hides a line and everything under it in that language. Left-out lines don't count as missing.
- **Out of date** means the English changed after the line was translated. Edit the translation, or use **⋯ → Mark translation as up to date** if it is still right.

**Saving.** **Save** stores a new version; nothing public changes. **Save & publish…** saves and makes the chosen languages live in one step. A language with missing text can't be published, and out-of-date translations ask for confirmation. **Discard** returns to the last saved version.

**What publishing stores.** Publishing renders the version in each chosen language as a PDF, a standalone HTML page and Markdown, and stores the files with the publication, in one transaction ([ADR 0003](adr/0003-public-cv-downloads.md) §2). These are the files the public download links serve. Publishing therefore needs the browser that prints PDFs and takes a second or two per language. If rendering fails, nothing is saved or published. Republishing a version that already has its files renders nothing.

**Download PDF** prints the current language as you see it, including unsaved changes, and leaves out lines without text.

**History.** **History** lists every version and shows what each language serves now. From there you can:

- see what changed in a version;
- publish an older version, which is how you roll back the public CV;
- unpublish a language;
- restore an old version. A restore is saved as a new, unpublished version.

**Two tabs.** If you save in one tab while another has the same CV open, the other tab's next save is refused with "Saved somewhere else" and offers to reload. Nothing is overwritten silently (ADR 0001 §3).

**Keyboard.** <kbd>Enter</kbd> adds another bullet, skill or contact. <kbd>Backspace</kbd> on an empty one removes it. <kbd>↑</kbd>/<kbd>↓</kbd> move between lines, <kbd>Alt</kbd>+<kbd>↑</kbd>/<kbd>↓</kbd> move the line itself, and <kbd>Ctrl</kbd>+<kbd>S</kbd> saves. Clicking a line in the preview jumps to its input.

## Backfilling public files

Every version that has ever been published needs its files, because the public links serve the current version and permalinks to older ones ([ADR 0003](adr/0003-public-cv-downloads.md) §3). Publishing stores them, but two cases need the backfill command:

- **Once, after the `RequireRendersToPublish` migration.** Versions published before it have no files.
- **After a renderer change.** When `HtmlRenderer.RendererVersion` or `MarkdownRenderer.RendererVersion` is bumped, published versions still serve files from the old renderer until they are rendered again.

The command uses the editor's settings (`CV_API_CONNECTION` and the browser), so load `.env` first. The editor doesn't need to be running.

```powershell
. ./scripts/Import-DotEnv.ps1
dotnet run --project src/Cv.Editor -- backfill --dry-run   # list what is missing, store nothing
dotnet run --project src/Cv.Editor -- backfill             # render and store it
```

It renders every format that has no file from the current renderer, for each version and language that has ever been published, including languages that were unpublished later. Drafts are skipped. Old files are kept, since stored files are never changed or deleted, and the public site serves the newest. Each version is stored in its own transaction, so if the command stops part-way, run it again to continue. A run with nothing to do prints *none*.

## Security model

The editor has no login. It relies on never being reachable by anyone but you (ADR 0002 §2):

- it listens on loopback only and rejects non-loopback peers;
- it rejects any `Host` other than `localhost`, which stops DNS rebinding;
- it requires its own `Origin` on every change, which stops other sites in your browser from posting to it (CSRF);
- it serves a strict Content Security Policy;
- it connects as `cv_api`, which can only read and append.

Don't expose it through a tunnel or a port forward: it would need real authentication first.

## Run the tests

```powershell
dotnet test                                          # all .NET tests (see below for the database)
node --test "tests/Cv.Editor.Js/*.test.mjs"         # browser-side model
```

| Suite | Needs |
|---|---|
| `tests/Cv.Core.Tests` | Nothing |
| `tests/Cv.Editor.Tests` | Nothing (in-memory store, fake PDF renderer) |
| `tests/Cv.Data.Tests` | PostgreSQL 15+: Docker running, **or** `CV_TEST_POSTGRES` set to an admin connection string for a **disposable** server, e.g. `Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres` |
| `tests/db/schema-tests.sql` | psql (see [cv-database.md §7](cv-database.md#7-run-the-schema-tests-optional)) |
| `tests/Cv.Public.Js` | Node.js: the public `/cv` endpoint. `npm test` runs it with the browser-model tests ([cv-public.md](cv-public.md#tests)) |

The integration tests create and drop databases named `cv_test_*` and the login roles `cv_test_app` and `cv_test_web`, so **never point `CV_TEST_POSTGRES` at Supabase**. Without Docker or `CV_TEST_POSTGRES` they are skipped. CI sets `CV_TEST_REQUIRE_DATABASE=1`, which turns a missing database into a failure instead.

CI (`.github/workflows/ci.yml`) runs on every push and pull request. It builds with warnings as errors, checks the EF model snapshot, runs every suite, and checks the README's ADR index.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `CV_API_CONNECTION is not set` at startup | Add it to `.env` and run `. ./scripts/Import-DotEnv.ps1` in the same window. |
| "The editor cannot start" with *cv.users does not have exactly one row* | Create your user (cv-database.md §6), or set `CV_EDITOR_USER_EMAIL` if there are several. |
| "Cannot reach the database" | Wrong host, user or password in `CV_API_CONNECTION`, or no network. The user must be `cv_api.<project-ref>` through the pooler. |
| `permission denied for schema cv` | The login isn't in `cv_app`: `GRANT cv_app TO cv_api;` as `postgres`. |
| "Cannot create PDFs" | No Chrome, Edge or Chromium was found. Install one or set `CV_CHROMIUM_PATH`. Publishing needs it too, so nothing was published. |
| *Cannot publish locale en: no stored file in format(s) …* | The editor stores the files before publishing, so this means something else tried to publish without them, or the editor is older than the `RequireRendersToPublish` migration. Pull and restart the editor. |
| `cv.cv_public_render` returns no row for a version published before the files migration | It has no files yet. Run the backfill (see *Backfilling public files*). |
| "Saved somewhere else" | Another tab saved first. Reload to continue from the newest version (copy any text you need first). |
| `403` from the API with a tool such as curl | Expected: changes must come from the editor's own page (`Origin`). |
| Port 5180 is in use | Set `Editor__Port` to another port. |
| "The editor cannot start" with *cv.json has a problem at $.children…* | Fix the seed file at that path and reload. |
| *Unknown node type 'location'* in the problems list after opening the seed | The `AddLocationNodeType` migration hasn't been applied: `dotnet run --project src/Cv.Migrator`, then restart the editor. |
