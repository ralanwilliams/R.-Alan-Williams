# CV database: setup and migrations

The CV content store is a PostgreSQL schema (`cv`) on Supabase, managed with EF Core migrations. The design and its reasoning are in [ADR 0001](adr/0001-cv-content-model.md).

```
Cv.slnx
├── src/Cv.Data/                  EF Core model (entities, configurations, DbContext)
│   └── Migrations/
│       ├── 20261006120000_InitialCreate.cs
│       ├── CvDbContextModelSnapshot.cs
│       └── Sql/                  reviewed SQL run by the migration (up + down)
├── src/Cv.Migrator/              console app: applies migrations; startup project for dotnet-ef
├── scripts/Import-DotEnv.ps1     loads .env into the current PowerShell session
├── .env.example                  template for the git-ignored .env (secrets)
└── tests/db/schema-tests.sql     56 checks that try to break every rule (rolls back)
```

## 1. Tooling

| Tool | Why | Install (Windows) |
|---|---|---|
| **.NET 10 SDK** (10.0.100 or later) | Builds the projects | `winget install Microsoft.DotNet.SDK.10` |
| **dotnet-ef 10.0.12** | Runs EF migrations. Pinned in `.config/dotnet-tools.json` | `dotnet tool restore` (from the repo root) |
| psql *(optional)* | Runs the schema tests | Comes with the PostgreSQL client tools |

Check the SDK with `dotnet --list-sdks`; you need a `10.0.1xx` or later entry. `global.json` requires 10.0.100 and rolls forward to any newer 10.0 feature band (e.g. 10.0.401).

Package versions are pinned centrally in `Directory.Packages.props`:

| Package | Version |
|---|---|
| Microsoft.EntityFrameworkCore, .Relational, .Design | 10.0.12 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 |

Keep `dotnet-ef` in `.config/dotnet-tools.json` on the same version as the EF Core packages. To upgrade, bump both together: `dotnet tool update dotnet-ef --version <x>` plus the `PackageVersion` entries.

## 2. Build and sanity-check (no database needed)

From the repo root in PowerShell:

```powershell
dotnet tool restore
dotnet build Cv.slnx
dotnet ef migrations has-pending-model-changes --project src/Cv.Data --startup-project src/Cv.Migrator
```

The last command should report that no changes have been made to the model since the last migration. It proves the hand-written model snapshot matches the EF model. If it reports changes, see *Troubleshooting*.

The tool prints "`CV_DB_CONNECTION is not set; using a placeholder`". That is expected here.

Optional: preview the exact SQL that will run:

```powershell
dotnet ef migrations script --project src/Cv.Data --startup-project src/Cv.Migrator -o cv-migration.sql
```

## 3. Get the Supabase connection string

1. Open your project in the Supabase dashboard and click **Connect** at the top.
2. Choose **Session pooler**. Don't choose:
   - **Direct connection**: IPv6-only unless you have the IPv4 add-on, and many home networks can't reach it.
   - **Transaction pooler** (port 6543): it doesn't support the session features migrations need.
3. Note the host (`aws-0-<region>.pooler.supabase.com`; newer projects may show `aws-1-...`, so copy whatever the dashboard shows), the port (`5432`) and the user (`postgres.<project-ref>`). Use the database password you set when you created the project. If you've lost it, reset it under **Database settings**.

Npgsql needs key/value format, not a `postgresql://` URI. Put it in `.env` at the repo root, which is git-ignored (start from `.env.example`):

```dotenv
CV_DB_CONNECTION=Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<db-password>;SSL Mode=Require
```

If the password contains `;` or `"`, wrap it in single quotes inside the string: `Password='p;ss'`.

.NET doesn't read `.env` files itself, so load it into each new PowerShell window before running the commands below:

```powershell
. ./scripts/Import-DotEnv.ps1      # note the leading dot + space
```

The variables last only for that window. Never commit `.env`.

### In GitHub Actions

Store the same value as a repository secret (**Settings → Secrets and variables → Actions → New repository secret**, name `CV_DB_CONNECTION`) and pass it to the step:

```yaml
- run: dotnet run --project src/Cv.Migrator
  env:
    CV_DB_CONNECTION: ${{ secrets.CV_DB_CONNECTION }}
```

GitHub-hosted runners have no IPv6, which is another reason to use the session pooler.

## 4. Apply the migration

```powershell
dotnet ef database update --project src/Cv.Data --startup-project src/Cv.Migrator
```

Alternatively, without the EF tool (the same thing, and handy in CI):

```powershell
dotnet run --project src/Cv.Migrator -- --list   # show applied/pending, change nothing
dotnet run --project src/Cv.Migrator             # apply
```

The whole migration runs in one transaction. If anything fails, nothing is left half-created.

## 5. Verify in Supabase

In the dashboard's **SQL editor**:

```sql
SELECT table_name FROM information_schema.tables WHERE table_schema = 'cv' ORDER BY 1;
-- 11 rows: __EFMigrationsHistory, audit_log, cv_node_contents, cv_nodes, ...

SELECT * FROM cv.locales;          -- en (source), nb, fr, zxx
SELECT * FROM cv.node_type_children ORDER BY 1, 2;
```

In **Table Editor**, pick the `cv` schema from the schema dropdown to browse the tables. Leave `cv` **out** of *API settings → Exposed schemas*; see ADR §11.

## 6. One-time setup after the migration

Run these in the SQL editor.

**Create your owner user** (every version records who created it):

```sql
INSERT INTO cv.users (id, email, display_name)
VALUES (gen_random_uuid(), '<your email>', 'R. Alan Williams')
RETURNING id;
```

**Create a login for the future app**, so it never connects as `postgres`. The app gets only what `cv_app` allows: read everything, insert history, and nothing else.

```sql
CREATE ROLE cv_api LOGIN PASSWORD '<long random password>' IN ROLE cv_app;
```

Through the session pooler, the app's username is then `cv_api.<project-ref>`.

## 7. Run the schema tests (optional)

The script builds a three-language CV and tries to break every invariant. It runs in a single transaction and **rolls back**, so it leaves nothing behind. Run it as the schema owner (`postgres`):

```powershell
psql "host=aws-0-<region>.pooler.supabase.com port=5432 dbname=postgres user=postgres.<project-ref> sslmode=require" -f tests/db/schema-tests.sql
```

You should see 56 `ok` lines and `All schema tests passed. Rolling back.`

## Making future schema changes

1. Change the entities and configurations in `src/Cv.Data`.
2. `dotnet ef migrations add <Name> --project src/Cv.Data --startup-project src/Cv.Migrator`
3. For anything EF can't express (CHECK constraints, triggers, views, grants), add `migrationBuilder.Sql(...)` to the generated migration. Every new table also needs grants for `cv_app`, and immutable tables need the `cv.forbid_mutation()` triggers.
4. Extend `tests/db/schema-tests.sql`, then `dotnet ef database update`.

To undo the initial migration (**this deletes all CV data**):

```powershell
dotnet ef database update 0 --project src/Cv.Data --startup-project src/Cv.Migrator
```

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `Tenant or user not found` | The pooler username must include the project ref: `postgres.<project-ref>`. |
| Connection timeout | You're using the direct host on an IPv4-only network. Use the session pooler. |
| `The model for context 'CvDbContext' has pending changes` | The snapshot and model disagree. Run `dotnet ef migrations add SnapshotSync ...`, inspect the generated `Up()`: it shows the exact difference. If the database already matches (it should), empty the `Up`/`Down` bodies, keep the regenerated snapshot, and apply. |
| `permission denied to create role` | The migration must run as the project's `postgres` user, which may create roles. |
| `UPDATE on cv.cv_nodes is not allowed: rows are immutable` | Working as intended. Save a new version instead. |
