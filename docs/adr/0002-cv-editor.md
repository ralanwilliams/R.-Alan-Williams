# ADR 0002: CV editor: a local web app over the versioned store

- **Status:** Accepted
- **Date:** 2026-10-06
- **Implementation:** `src/Cv.Core` (document workflow, rendering), `src/Cv.Data/Store` (persistence), `src/Cv.Editor` (web app), `src/Cv.Data/Migrations/*_AddLocationNodeType.cs` (§11), `tests/`

## Context

[ADR 0001](0001-cv-content-model.md) defines how the CV is stored: immutable versions of one tree, text per locale, per-locale publishing, with every rule enforced by PostgreSQL. It describes the editor only in outline. The left side shows the CV as one plain-looking document, where every line is its own input. The right side is a live rendered preview. The actions are Save, Save & Publish, Discard and Download.

ADR 0001 also leaves the editor's side of several decisions open:

- **Save path:** compute `content_hash` over a canonical serialisation, and reload on a version conflict.
- **Renderer:** message files, `lang` for hyphenation, French typography, `Content-Disposition` with `filename*`, and `renderer_version`.
- **Workflow placement:** workflow belongs in application code "where it can be unit tested".
- **Auth:** a passkey, OAuth or an access proxy in front of the editor, with no password table.

The constraints are the same as before. There is one author, two browser tabs must not corrupt anything, and Supabase is the database. The editor must also give feedback while typing, before anything is saved: which lines are missing or stale in which language, and what the result looks like.

## Decision

### 1. A local web app, not a hosted one (for now)

The editor is an ASP.NET Core app that the author runs on their own computer (`dotnet run --project src/Cv.Editor`) and opens at `http://localhost:5180`. It connects to Supabase directly, as the least-privilege `cv_api` login.

There is one author, who edits occasionally, so hosting buys little. It would also force the auth question that ADR 0001 deferred. Running locally keeps the database credentials on the author's machine and costs nothing to operate. The public side, serving `/cv/{locale}.pdf`, is a separate concern and stays open (see *Follow-ups*).

### 2. Security without a login

"Local" is only safe if it is enforced. Any web page open in the same browser can send requests to `localhost`, so the editor treats the browser as hostile:

| Threat | Defence |
|---|---|
| Access from another machine | Kestrel binds to loopback in code (`ListenLocalhost`), whatever `ASPNETCORE_URLS` says. A middleware also refuses non-loopback peers. |
| DNS rebinding (a hostile site rebinding its own name to 127.0.0.1) | Host filtering: only `localhost`, `127.0.0.1` and `[::1]` are accepted as `Host`. |
| CSRF (a hostile page POSTing to the editor) | Every state-changing request must carry an `Origin` equal to the editor's own. Browsers always send it on cross-origin POSTs, and pages cannot forge it. |
| Injected content | A CSP with no inline script or style (`script-src 'self'; style-src 'self'`), `frame-ancestors 'none'` and `nosniff`. The preview is a sandboxed `srcdoc` iframe that may not run scripts. The renderer encodes all text and only emits `http(s)`, `mailto` and `tel` links. |
| A compromised editor | It connects as `cv_api` (member of `cv_app`), which can only SELECT and INSERT. History cannot be altered even then. |

Each rule has a test in `tests/Cv.Editor.Tests/SecurityTests.cs`.

### 3. Plain HTML, CSS and JavaScript modules; no front-end build

The page is static files in `wwwroot`: three ES modules and one stylesheet. The document model (`model.js`) has no DOM code and is unit tested with Node's built-in test runner. The UI code (`editor.js`) is a thin DOM layer over it.

The UI is one page with a list of inputs, a preview and a few dialogs. A framework would add a toolchain, a `node_modules` tree and a build step to a .NET repository for little gain. Everything that is hard about the editor (ordering, hashing, staleness, validation, rendering) lives in C# where it is shared and tested.

### 4. The server derives sort keys and source hashes from the base version

The browser sends a **draft**: nodes in document order with parent ids, plus text per locale. It never sends sort keys or source hashes. `Cv.Core.Drafts.DraftBuilder` turns the draft into a version, relative to the version it was based on.

- **Text** is trimmed, line endings become `\n`, and it is normalised to Unicode NFC. Typing "é" two different ways gives the same text and the same hash. Empty text means no row, so the line is *missing* in that locale.
- **Sort keys** (ADR 0001 §8) use a C# port of base62 fractional indexing. The port is checked against the reference implementation on 4,600 randomised insertions. A sibling list is keyed by keeping a *longest increasing subsequence* of the base version's keys and generating new keys only for the rest. Inserting or moving one line changes one row, so a diff reports one change.
- **Source hashes** (ADR 0001 §6): a translation that is new, edited, or explicitly marked as reviewed records the hash of the *current* source text. An unchanged translation keeps its base hash, so it turns stale when the English changes, which is the point.

Building a loaded version unchanged reproduces its content hash exactly. This is tested, and it is what makes "no changes" detection reliable.

### 5. A canonical content hash

`content_hash` is SHA-256 over a canonical JSON serialisation, format `cv-content/1`. Nodes are sorted by id and texts by node and locale, keys are in a fixed order, and there is no whitespace. Version metadata such as the summary or author is excluded. The format is versioned and pinned by a test, so any change to it is deliberate.

### 6. C# twins of the database's rules, with the database as the authority

The editor needs answers for an unsaved draft on every keystroke, which the database cannot give. Two C# components therefore mirror SQL:

- `Localizer` mirrors `cv.cv_version_localized`: `zxx` fallback, inherited omission, and the missing and stale flags.
- `DocumentValidator` mirrors the constraints and triggers, and points each problem at a line.

Duplicated logic can drift. The database stays authoritative: it rejects anything invalid, and publishing is checked by its trigger. An integration test runs the C# localizer and the SQL function on the same stored version and requires them to agree on every flag, text and position. A second test checks that the seed data matches the catalog the unit tests use.

### 7. Saving, conflicts and publishing

- **Base version.** Every save names the version it was based on. The store inserts `version_number = base + 1` with `previous_version_id = base`. A save from a superseded base hits the unique indexes (ADR 0001 §3) and becomes **409 Conflict**. The page then offers to reload; it never retries.
- **No-op saves.** A save whose hash equals its base's is answered "nothing to save" before reaching the database. The database trigger would refuse it anyway.
- **Save & Publish is one transaction.** The version, its rows and its publications commit together or not at all. Publications are inserted only after the version's rows exist. The publish trigger counts missing text, so a publication inserted first would see an empty tree and pass vacuously.
- **Missing text blocks publishing; stale text needs confirmation.** The API refuses missing text the same way the trigger does. For stale translations it answers `confirm-stale`, and the page asks before resending.
- **"Save & Publish" with nothing to save** publishes the base version. Republishing an older version (reverting) and unpublishing go through the history panel.
- **Restore** appends a verbatim copy of an old version as the newest, unpublished (ADR 0001 §4).

Errors are RFC 7807 problem documents with a stable `code`: `conflict`, `invalid`, `no-changes`, `not-ready`, `confirm-stale` or `rejected`.

### 8. One renderer for the preview and the PDF

`HtmlRenderer` produces a standalone HTML document per locale. The preview and the download use the same code, so what the author sees is what gets downloaded.

- Semantic markup with `lang` set, so the browser hyphenates per language.
- The layout follows the author's original Word CV: a centred name and contact line, bold uppercase section headings, and each entry as "ORGANISATION | Location" over "Title | dates". Skill groups render as "Label: a, b, c". The page is A4 (the original is US Letter) with the original's margins, in Calibri.
- Per-locale strings (month names, "present", the title, the label colon) come from message files in `Rendering/Messages/{locale}.json`. A test checks that every publishable locale has one.
- Dates are stored language-neutrally in `attrs` (`YYYY` or `YYYY-MM`) and formatted per locale.
- French gets a narrow no-break space before `: ; ! ?` and inside `« »`. Only existing spaces are converted, so `10:30` is untouched.
- The preview marks missing and stale text. A download leaves out lines with no text rather than printing empty ones.
- `RendererVersion` (`html/2` since the layout above) is ready for `cv_renders.renderer_version`. *`html/3` adds a viewport tag and phone padding for the public HTML download ([ADR 0003](0003-public-cv-downloads.md)); PDFs are unchanged.*

PDFs are printed by a **Chromium-based browser already on the machine** (Chrome, Edge or Chromium, or `CV_CHROMIUM_PATH`), driven through PuppeteerSharp. Chromium's print engine supports the CSS this relies on (`@page`, hyphenation), and using the installed browser avoids a 150 MB download. The stylesheet uses local fonts only (Calibri, falling back to Carlito and other system sans-serifs), so PDF generation needs no network and covers æ ø å and French accents.

### 9. Attributes are a small, typed schema

The database only requires `attrs` to be a JSON object. The application is stricter (`NodeAttributes`): a flat map of strings with an allow-list per node type. An entry has `start` and `end` (`YYYY` or `YYYY-MM`, with the end not before the start); a contact has `kind` (`email`, `phone`, `url` or `location`). The editor builds its inputs from this schema.

### 10. Testing at every layer

| Layer | How | Where |
|---|---|---|
| Domain | xUnit, no I/O: ordering, hashing, drafts, validation, localisation, rendering | `tests/Cv.Core.Tests` |
| Database | Real PostgreSQL 17, one migrated template database cloned per test, connecting as a `cv_app` member | `tests/Cv.Data.Tests` |
| HTTP | The real app in-process (`WebApplicationFactory`) with an in-memory store: API contract and every security rule | `tests/Cv.Editor.Tests` |
| Browser model | `node --test`, no dependencies | `tests/Cv.Editor.Js` |
| Schema | The SQL suite from ADR 0001 | `tests/db/schema-tests.sql` |

The integration tests use Testcontainers when Docker is available, or `CV_TEST_POSTGRES` otherwise. CI runs everything on every push and pull request.

### 11. Seeding the first version from an outline file; a `location` line type

The author's existing CV had to become version 1. Two choices were made:

- **An outline file, opened by the editor, not an import command.** The CV is transcribed into a nested JSON outline (`DraftOutline`): one object per line, with text given per locale code. It is a hand-editable format that allows comments. When no version exists and `CV_SEED_FILE` names an outline, the editor opens it instead of the starter document. Nothing is written until the author reviews it and presses Save, so v1 goes through the normal validated save path (§4–7) as the `cv_api` login. Version 1 can never be deleted, so this review step is the point. The outline parser rejects unknown properties with their JSON path, so a typo is never silently dropped. The seed file itself is personal data and lives in the git-ignored `seed/` folder.
- **A `location` node type** (migration `AddLocationNodeType`). In the original CV, an entry's organisation and place share a line ("NORWEGIAN MARITIME AUTHORITY | Haugesund, Norway"). They translate differently: the organisation is usually the same in every language (`zxx`), the place is not ("Norge", "Norvège"). So `subtitle` now means the organisation, and the new `location` holds the place. Both are children of `entry`. This is the case ADR 0001 §9 anticipated: the grammar is data, so the change is a seed-data migration plus renderer support, with no change to the EF model.

## Consequences

**Positive**

- Unsaved drafts get the same answers the database will give (missing, stale, invalid), line by line, while typing.
- The editor cannot corrupt history, even if it is buggy or compromised. The database and the `cv_app` grants still stop it.
- Two tabs, no-op saves and half-published saves are handled by construction and covered by tests.
- No hosting, no front-end toolchain, and no secrets outside the author's machine.

**Negative / costs**

- The rules exist twice (SQL and C#). The agreement test catches drift, but a change to one must be made in both.
- The editor only runs where the .NET SDK, the repository and a Chromium-based browser are. Editing from a phone is not possible.
- Without a login, safety depends on the loopback, Host and Origin checks above. Hosting the editor would need real authentication first (see *Follow-ups*).
- PDF output depends on the installed browser's print engine, so output can differ slightly between browser versions. `renderer_version` exists to record this once renders are stored.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Host the editor (e.g. on Cloudflare) behind Cloudflare Access or passkeys | Real auth and hosting work for a single occasional author. Kept as the follow-up path if editing from anywhere becomes worth it. |
| Blazor (Server or WebAssembly) | One language end to end, but heavier at runtime and harder to reason about for a page this small. The C# that matters is shared anyway. |
| React or another SPA framework with a build step | A Node toolchain in a .NET repository for one page. |
| Compute missing and stale only in the database | Would need a save, or a throwaway transaction, per keystroke. |
| Let the browser assign sort keys and hashes | Puts the rules that keep history clean in the least trustworthy, least testable place. |
| QuestPDF or another layout library for PDFs | A second layout model to keep in sync with the HTML preview. Printing the same HTML guarantees they match. |
| Download Chromium on demand (PuppeteerSharp's `BrowserFetcher`) | A 150 MB download on first use. Kept as a fallback idea for machines without Chrome or Edge. |
| Import the Word CV automatically (parse `.docx`) | Word's structure is presentational (bold runs, tabs, list styles), so a parser would guess at line types and break on the next edit. A one-time transcription into a reviewable outline is exact and checkable. |
| A seed-import command that writes v1 directly | Skips the review in the editor and the save path's checks, for a version that can never be deleted. |
| Keep "organisation, place" in one `subtitle` line | The place would need translating, which would duplicate the organisation in every locale. |

## Follow-ups

- **Public PDFs:** store rendered PDFs on publish (`cv_renders`) and serve `/cv/{locale}.pdf` from the site. Open choice: either publishing writes files to object storage (e.g. Cloudflare R2), so the site holds no database secret, or the site queries Supabase per request. ADR 0001 §7 and its *Caching* follow-up apply. *Decided in [ADR 0003](0003-public-cv-downloads.md): files are stored in the database on publish and the site queries it.*
- **Auth**, if the editor is ever hosted: an access proxy or passkeys, as in ADR 0001.
- **Autosave** of unsaved drafts in the browser, so a crash or a conflict reload loses nothing.
- **Accessibility review** of the editor with a screen reader. Keyboard use, labels, focus states and reduced motion are in place, but have not been audited.
