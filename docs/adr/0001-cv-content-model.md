# ADR 0001: CV content model: an immutable, versioned tree with per-locale text

- **Status:** Accepted
- **Date:** 2026-10-06
- **Implementation:** `src/Cv.Data` (EF Core model and migration), `src/Cv.Data/Migrations/Sql/*.sql`, `tests/db/schema-tests.sql`

## Context

The CV on ralanwilliams.com should be a single source of truth. Every download link (`/cv/en.pdf` and so on) must always serve the current published CV, so nothing downstream ever needs updating.

The editor shows the CV as one plain-looking document on the left, where every line is its own input, and a live rendered preview on the right. The actions are Save, Save & Publish, Discard and Download.

Requirements:

1. **Typed, hierarchical content.** Each piece of text has a type (heading, entry, bullet...) and usually a parent.
2. **Full history.** Every save is kept. It must be possible to see what changed between any two versions and to restore any earlier version.
3. **Multiple languages:** English, Norwegian bokmål and French, with room for more.
4. **Draft/publish split.** Saving must not change what the public sees. Each language publishes on its own schedule.
5. **One author** (me), but two open browser tabs must not be able to corrupt anything.
6. **Hosting:** Supabase (managed PostgreSQL). Schema changes go through EF Core migrations.

This is also a portfolio piece, so the reasoning matters as much as the result.

## Decision

### 1. Immutable snapshots, not a mutable table

Every Save inserts a new **version**: a complete copy of the tree and all its text. Rows are never updated or deleted. History is therefore just the list of versions, and there is nothing like `modified_by` or `is_deleted` to keep in sync. A node removed in version 5 is simply absent from version 5.

A CV has a few hundred short strings, so copying everything per save costs kilobytes. That buys trivially correct history, restores and diffs.

### 2. Stable node identity across versions

Each node has a `node_id` that never changes. The primary key of `cv_nodes` is `(version_id, node_id)`. This one choice makes three things simple:

- **Diffs** are a full outer join on `node_id` (`cv.cv_version_diff`).
- **Restores** copy rows verbatim, and parent links stay correct because `parent_node_id` refers to stable ids.
- **Per-line history** is possible: follow one `node_id` through the versions.

The parent reference is a **composite foreign key** `(version_id, parent_node_id) → (version_id, node_id)`, so a node can never have a parent in another version. It is `DEFERRABLE INITIALLY DEFERRED`, so a version's nodes can be inserted in any order within the save transaction.

### 3. Linear history enforced by the database

- `version_number` is unique, and version 1 is the only version without a predecessor (CHECK).
- `previous_version_id` is **unique**: a version has at most one successor, so history cannot branch.
- A trigger requires `version_number = previous + 1` and rejects a save whose `content_hash` equals its predecessor's (a no-op save).

Together these mean a save based on anything other than the latest version fails. That is how **two open tabs** are handled. The second tab's save is rejected with a unique violation, and the editor reloads it, instead of silently overwriting the first tab's work. This costs no locking code.

"Current version" is not stored. It is the highest `version_number` (view `cv.cv_latest_version`), so it can never disagree with the history.

### 4. Restore creates a new version

Restoring version 3 while version 7 is current creates version 8, a verbatim copy of version 3 with `restored_from_version_id` set. History reads honestly ("v8: restored from v3"), and the restore stays a draft until it is published.

### 5. One structure shared by all languages; text per locale

- `cv_nodes` holds the tree: type, position, and **language-neutral attributes** (`attrs` jsonb, e.g. `{"start":"2021-03","end":null}`). The renderer formats dates per locale.
- `cv_node_contents` holds text: one row per `(version, node, locale)`.

Adding a job in English therefore shows up immediately as *missing* in Norwegian and French. The three CVs cannot drift apart.

Text that is the same in every language (name, email, company names, URLs) is stored once under the BCP 47 code **`zxx`** ("no linguistic content"). It applies to every locale unless a locale overrides it. Norwegian uses **`nb`**, not the macrolanguage `no`.

### 6. Missing, omitted and stale are three different states

| State | Meaning | Effect |
|---|---|---|
| **Missing** | Needs text in this locale and has none (no locale row, no `zxx` row) | Blocks publishing that locale |
| **Omitted** | Deliberately left out of this locale (`is_omitted = true`) | Hides the node **and its whole subtree** in that locale |
| **Stale** | Translated from English text that has since changed | Publishing allowed; the editor warns |

Staleness works through `source_hash`: each translation stores the SHA-256 of the English text it was made from. If the English text changes, the hashes no longer match. A translation with no `source_hash` counts as stale, because its origin is unknown.

`cv.cv_version_localized(version)` computes all three states in one recursive query: zxx fallback, omission inherited down the tree, missing and stale flags, and document order. `cv.cv_version_readiness(version)` summarises it per locale.

**English is the source locale.** Every `source_hash` refers to English text, so changing the source locale later is a deliberate migration, not a setting.

### 7. Publishing is an append-only pointer log

`cv_publications` records "locale X now serves version Y" (or nothing: `version_id` NULL means unpublished). The latest row per locale wins (view `cv.cv_published`). As a result:

- **Save** never affects the public site.
- **Publishing is per locale**: English can go live while French is still being translated.
- **Reverting what is public** means publishing an older version. That is instant and creates no new version.
- Publication history is never lost.

A trigger refuses to publish a version that has *missing* text in that locale. A composite FK to `locales(code, is_publishable)` makes it impossible to publish `zxx`.

Public URLs:

- `/cv/{locale}.pdf` serves the published version for that locale.
- `/cv.pdf` serves a fixed default locale. It does not use `Accept-Language`, because a link sent to a recruiter must always return the same file.
- `/cv/v/{version_id}/{locale}.pdf` is a permalink to exactly what a given employer received. It is served only if that version was ever published for that locale, so drafts never leak through a guessable URL.

### 8. Sibling order uses fractional indexing

`sort_key` is a base62 fractional index (`a0`, `a1`; inserting between them gives `a0V`), compared with the `"C"` collation. Inserting a bullet at the top changes one row instead of renumbering every sibling, so a diff reports one change, not ten. Siblings must have distinct keys: a unique index with `NULLS NOT DISTINCT`.

Document order is `ORDER BY sort_path COLLATE "C"`. The explicit collation matters: under a linguistic collation such as `en_US`, `Zz` sorts after `a0`, but in base62 order it comes before.

### 9. The document grammar is data

`node_types` lists the kinds of node. `node_type_children` lists which type may contain which (a `section` may contain `entry`; a `bullet` contains nothing). A deferred constraint trigger checks every new node against the grammar and walks up to the root to reject cycles. Exactly one `root` per version is enforced by a partial unique index, plus a CHECK that only the root has no parent.

The seeded grammar is acyclic, so cycles are impossible today. The cycle check still exists because the grammar is data and may change; the test suite proves it works by temporarily allowing a recursive type.

It has changed once so far: a `location` type under `entry` was added by a migration ([ADR 0002](0002-cv-editor.md) §11).

### 10. Integrity lives in the database, workflow in the application

Every rule above is enforced by PostgreSQL: CHECKs, keys, triggers and grants. Application bugs, manual SQL and future clients all hit the same walls.

Immutability is enforced twice:

- **Grants:** `cv_app` has only `SELECT` and `INSERT` on history tables.
- **Triggers:** `BEFORE UPDATE OR DELETE` and `BEFORE TRUNCATE` triggers stop even the table owner.

The one escape hatch is deliberate and visible: `ALTER TABLE ... DISABLE TRIGGER`, for example for a GDPR erasure.

Workflow (build the tree from the editor, compute hashes, decide when to publish) belongs in application code, where it can be unit tested. Stored procedures are kept to read-side helpers.

### 11. PostgreSQL on Supabase, isolated from the Data API

Everything lives in a dedicated **`cv` schema**, including EF's `__EFMigrationsHistory`. Supabase's auto-generated REST and GraphQL APIs only expose schemas listed in the API settings (by default `public`), so this data is not reachable through them. The migration also revokes all access from Supabase's `anon` and `authenticated` roles, as a second layer.

Row Level Security is not used. RLS protects tables that the Data API exposes; these tables are not exposed, and access is controlled by the `cv_app` role instead. **If the `cv` schema is ever added to the exposed schemas, RLS must be added first.**

The application connects as a login role that is a member of `cv_app`, never as `postgres`. All functions pin `search_path = ''` and schema-qualify everything (Supabase's linter flags mutable search paths). Views use `security_invoker = true`.

### 12. Identifiers: UUIDv7 generated by the application

Primary keys are native `uuid` (16 bytes), not `CHAR(36)`. They are **UUIDv7**, which is time-ordered and therefore index-friendly, generated with `Guid.CreateVersion7()` in .NET.

This deviates from the earlier sketch, which used a `uuidv7()` column default. That function only arrived in PostgreSQL 18, and Supabase runs 15 or 17. Generating ids in the application also lets the editor assign a node's id before it is saved, which the stable-identity model needs anyway.

Lookup tables use readable natural keys (`'bullet'`, `'nb'`), because a GUID for a code adds nothing.

Timestamps are `timestamptz`.

### 13. EF Core owns structure; reviewed SQL owns invariants

EF Core cannot express CHECK constraints across tables, expression or partial indexes, deferrable FKs, triggers, views, functions or grants. So:

- The **EF model** (entities and configurations) mirrors every table, column, key, foreign key and plain index, with explicit names. The model snapshot matches it, so future `dotnet ef migrations add` produces correct diffs.
- The **initial migration** runs one reviewed SQL script, embedded in the assembly, inside EF's migration transaction. Either all of it applies or none of it does.
- `tests/db/schema-tests.sql` builds a three-language CV and tries to break every invariant (54 checks; 2 of them only apply on Supabase). It runs in a transaction and rolls back.

Later migrations can be ordinary EF migrations. When one needs a constraint, trigger or view, it adds `migrationBuilder.Sql(...)` the same way.

## Consequences

**Positive**

- History, diffs and restores are correct by construction, not by careful coding.
- An unfinished translation cannot go live, and the editor can show a precise to-do list per locale (missing and stale nodes).
- Public links never change and never serve a draft.
- Two-tab conflicts and no-op saves are rejected by the database with zero application code.
- The schema states its own rules, and the test suite proves them.

**Negative / costs**

- Each save copies the whole CV. This is fine at CV scale and would be wrong for large documents. The fix there would be structural sharing: copy only changed nodes and resolve the rest through the predecessor chain.
- Triggers make some rules less visible than application code would. Mitigated by naming them clearly, raising readable errors, and covering each with a test.
- Correcting a mistake means saving a new version. There is no in-place fix. This is intended.
- The initial migration is raw SQL, so its correctness depends on the SQL tests plus the model/snapshot consistency check (`dotnet ef migrations has-pending-model-changes`), not on EF generating it.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Mutable `cv_sections` table plus a changes/audit table | Two sources of truth that drift; an "update current rows" save overwrites the history it is meant to keep |
| New UUID for every node in every version | No identity across versions, so diffs and per-line history are impossible, and restore must rebuild parent links |
| `is_current` flag or `replaced_by` pointer on versions | Duplicates "latest version" and needs updates to old rows; "exactly one current" is hard to enforce |
| Integer `position` | Inserting one item renumbers all following siblings and floods diffs |
| One tree per language | Structure drifts between languages; adding a job means three separate edits with nothing checking them |
| Translations as JSON on the node | Cannot enforce per-locale rules, query missing text, or attach `source_hash` cleanly |
| Stored procedures for save/restore | Hard to test and version; constraints belong in the database, workflow in code |
| Tables in Supabase's `public` schema with RLS | Exposes the data through the Data API by default; RLS policies are one more thing to get exactly right for no benefit here |
| MySQL | No partial or `NULLS NOT DISTINCT` indexes, deferrable FKs, native `uuid`, or transactional DDL |

## Follow-ups

- **Editor save path:** compute `content_hash` over a canonical serialisation of nodes and contents; on a unique violation of `ux_cv_versions_version_number` or `ux_cv_versions_previous_version_id`, reload instead of retrying. *Done: [ADR 0002](0002-cv-editor.md) §4, §5 and §7.*
- **Renderer:** template strings in message files; `lang` set on the HTML before PDF generation for hyphenation; fonts covering æ ø å and French accents; a narrow no-break space before `: ; ! ?` in French; `Content-Disposition` with `filename*=UTF-8''…` and an ASCII fallback; `renderer_version` bumped whenever output changes. *Done for the editor's preview and downloads: [ADR 0002](0002-cv-editor.md) §8. Storing published renders in `cv_renders` is still open.*
- **Caching:** short `Cache-Control` on `/cv/*.pdf` and a CDN purge on publish.
- **Auth:** passkey, OAuth or an access proxy in front of the editor. No password table. *Deferred: the editor runs locally, loopback only ([ADR 0002](0002-cv-editor.md) §1–2). Needed if it is ever hosted.*
- **Downloads:** count them in `audit_log` without storing IP addresses (personal data under GDPR).
