-- =============================================================================
-- InitialCreate (up)
--
-- Creates the CV content store described in docs/adr/0001-cv-content-model.md.
--
-- Division of labour:
--   * Structure (tables, columns, keys, foreign keys, plain indexes) is mirrored
--     1:1 in the EF Core model so future `dotnet ef migrations add` diffs are
--     correct.
--   * Invariants EF cannot express (CHECK constraints, partial/expression
--     indexes, deferrable FKs, triggers, functions, views, grants, seed data)
--     live only here.
--
-- Runs inside the EF migration transaction: either all of it applies or none.
-- Requires PostgreSQL 15+ (NULLS NOT DISTINCT, security_invoker views).
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS cv;

-- Application role. NOLOGIN: a separate login role is granted membership
-- (see docs/cv-database.md), so no password ever lives in a migration.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'cv_app') THEN
        CREATE ROLE cv_app NOLOGIN;
    END IF;
END
$$;

-- -----------------------------------------------------------------------------
-- Reference data
-- -----------------------------------------------------------------------------

CREATE TABLE cv.users (
    id           uuid                     NOT NULL,
    email        text                     NOT NULL,
    display_name text                     NOT NULL,
    created_at   timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT ck_users_email_format CHECK (email ~ '^[^@\s]+@[^@\s]+$' AND length(email) <= 254),
    CONSTRAINT ck_users_display_name_not_blank CHECK (length(btrim(display_name)) > 0)
);
CREATE UNIQUE INDEX ux_users_email_ci ON cv.users (lower(email));

CREATE TABLE cv.locales (
    code           text    NOT NULL,   -- BCP 47
    name           text    NOT NULL,   -- endonym
    is_source      boolean NOT NULL,
    is_publishable boolean NOT NULL,
    CONSTRAINT pk_locales PRIMARY KEY (code),
    -- Target for cv_publications' composite FK: only publishable locales can be published.
    CONSTRAINT ak_locales_code_is_publishable UNIQUE (code, is_publishable),
    CONSTRAINT ck_locales_source_is_publishable CHECK (is_publishable OR NOT is_source),
    CONSTRAINT ck_locales_code_format CHECK (code ~ '^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$'),
    CONSTRAINT ck_locales_name_not_blank CHECK (length(btrim(name)) > 0)
);
CREATE UNIQUE INDEX ux_locales_single_source ON cv.locales ((true)) WHERE is_source;

CREATE TABLE cv.node_types (
    code        text    NOT NULL,
    description text    NOT NULL,
    has_text    boolean NOT NULL,     -- false = structural container (root)
    CONSTRAINT pk_node_types PRIMARY KEY (code),
    CONSTRAINT ck_node_types_code_format CHECK (code ~ '^[a-z][a-z_]*$')
);

-- The document grammar: which node types may contain which.
CREATE TABLE cv.node_type_children (
    parent_type text NOT NULL,
    child_type  text NOT NULL,
    CONSTRAINT pk_node_type_children PRIMARY KEY (parent_type, child_type),
    CONSTRAINT fk_node_type_children_parent_type FOREIGN KEY (parent_type)
        REFERENCES cv.node_types (code) ON DELETE RESTRICT,
    CONSTRAINT fk_node_type_children_child_type FOREIGN KEY (child_type)
        REFERENCES cv.node_types (code) ON DELETE RESTRICT,
    CONSTRAINT ck_node_type_children_root_is_never_a_child CHECK (child_type <> 'root')
);
CREATE INDEX ix_node_type_children_child_type ON cv.node_type_children (child_type);

-- -----------------------------------------------------------------------------
-- Versions: immutable, linear history
-- -----------------------------------------------------------------------------

CREATE TABLE cv.cv_versions (
    id                       uuid                     NOT NULL,
    version_number           integer                  NOT NULL,
    previous_version_id      uuid                     NULL,
    restored_from_version_id uuid                     NULL,
    summary                  text                     NULL,
    content_hash             bytea                    NOT NULL,  -- SHA-256 of structure + all locales
    created_by               uuid                     NOT NULL,
    created_at               timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_cv_versions PRIMARY KEY (id),
    CONSTRAINT fk_cv_versions_previous_version FOREIGN KEY (previous_version_id)
        REFERENCES cv.cv_versions (id) ON DELETE RESTRICT,
    CONSTRAINT fk_cv_versions_restored_from_version FOREIGN KEY (restored_from_version_id)
        REFERENCES cv.cv_versions (id) ON DELETE RESTRICT,
    CONSTRAINT fk_cv_versions_created_by FOREIGN KEY (created_by)
        REFERENCES cv.users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_cv_versions_version_number_positive CHECK (version_number > 0),
    CONSTRAINT ck_cv_versions_only_first_has_no_previous CHECK ((version_number = 1) = (previous_version_id IS NULL)),
    CONSTRAINT ck_cv_versions_not_own_previous CHECK (previous_version_id IS DISTINCT FROM id),
    CONSTRAINT ck_cv_versions_not_restored_from_self CHECK (restored_from_version_id IS DISTINCT FROM id),
    CONSTRAINT ck_cv_versions_content_hash_sha256 CHECK (octet_length(content_hash) = 32),
    CONSTRAINT ck_cv_versions_summary_not_blank CHECK (summary IS NULL OR length(btrim(summary)) > 0)
);
CREATE UNIQUE INDEX ux_cv_versions_version_number ON cv.cv_versions (version_number);
-- At most one successor per version: history cannot branch, and a save from a
-- stale editor tab (based on an already-superseded version) is rejected.
CREATE UNIQUE INDEX ux_cv_versions_previous_version_id ON cv.cv_versions (previous_version_id);
CREATE INDEX ix_cv_versions_restored_from_version_id ON cv.cv_versions (restored_from_version_id);
CREATE INDEX ix_cv_versions_created_by ON cv.cv_versions (created_by);

-- -----------------------------------------------------------------------------
-- Structure: the tree, shared by all languages
-- -----------------------------------------------------------------------------

CREATE TABLE cv.cv_nodes (
    version_id     uuid             NOT NULL,
    node_id        uuid             NOT NULL,  -- stable across versions
    parent_node_id uuid             NULL,      -- NULL only for the root
    type_code      text             NOT NULL,
    sort_key       text COLLATE "C" NOT NULL,  -- fractional index, base62
    attrs          jsonb            NOT NULL DEFAULT '{}'::jsonb,  -- language-neutral data (dates etc.)
    CONSTRAINT pk_cv_nodes PRIMARY KEY (version_id, node_id),
    CONSTRAINT fk_cv_nodes_version FOREIGN KEY (version_id)
        REFERENCES cv.cv_versions (id) ON DELETE RESTRICT,
    -- Composite FK: a parent must belong to the same version.
    -- Deferred so a version's nodes can be inserted in any order.
    CONSTRAINT fk_cv_nodes_parent FOREIGN KEY (version_id, parent_node_id)
        REFERENCES cv.cv_nodes (version_id, node_id) ON DELETE RESTRICT
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT fk_cv_nodes_type FOREIGN KEY (type_code)
        REFERENCES cv.node_types (code) ON DELETE RESTRICT,
    CONSTRAINT ck_cv_nodes_not_own_parent CHECK (parent_node_id IS DISTINCT FROM node_id),
    CONSTRAINT ck_cv_nodes_root_iff_no_parent CHECK ((type_code = 'root') = (parent_node_id IS NULL)),
    CONSTRAINT ck_cv_nodes_sort_key_base62 CHECK (sort_key ~ '^[0-9A-Za-z]+$'),
    CONSTRAINT ck_cv_nodes_attrs_is_object CHECK (jsonb_typeof(attrs) = 'object')
);
CREATE UNIQUE INDEX ux_cv_nodes_sibling_order
    ON cv.cv_nodes (version_id, parent_node_id, sort_key) NULLS NOT DISTINCT;
CREATE UNIQUE INDEX ux_cv_nodes_single_root
    ON cv.cv_nodes (version_id) WHERE parent_node_id IS NULL;
CREATE INDEX ix_cv_nodes_type_code ON cv.cv_nodes (type_code);

-- -----------------------------------------------------------------------------
-- Text: one row per node per locale
-- -----------------------------------------------------------------------------

CREATE TABLE cv.cv_node_contents (
    version_id  uuid    NOT NULL,
    node_id     uuid    NOT NULL,
    locale      text    NOT NULL,
    content     text    NULL,
    is_omitted  boolean NOT NULL,   -- deliberately left out of this locale (hides the subtree)
    source_hash bytea   NULL,       -- SHA-256 of the source-locale text this was translated from
    CONSTRAINT pk_cv_node_contents PRIMARY KEY (version_id, node_id, locale),
    CONSTRAINT fk_cv_node_contents_node FOREIGN KEY (version_id, node_id)
        REFERENCES cv.cv_nodes (version_id, node_id) ON DELETE RESTRICT,
    CONSTRAINT fk_cv_node_contents_locale FOREIGN KEY (locale)
        REFERENCES cv.locales (code) ON DELETE RESTRICT,
    CONSTRAINT ck_cv_node_contents_omitted_xor_content CHECK (is_omitted = (content IS NULL)),
    CONSTRAINT ck_cv_node_contents_content_not_blank CHECK (content IS NULL OR length(btrim(content)) > 0),
    CONSTRAINT ck_cv_node_contents_neutral_never_omitted CHECK (locale <> 'zxx' OR NOT is_omitted),
    CONSTRAINT ck_cv_node_contents_neutral_has_no_source_hash CHECK (locale <> 'zxx' OR source_hash IS NULL),
    CONSTRAINT ck_cv_node_contents_omitted_has_no_source_hash CHECK (NOT is_omitted OR source_hash IS NULL),
    CONSTRAINT ck_cv_node_contents_source_hash_sha256 CHECK (source_hash IS NULL OR octet_length(source_hash) = 32)
);
CREATE INDEX ix_cv_node_contents_locale ON cv.cv_node_contents (locale);

-- -----------------------------------------------------------------------------
-- Publishing: append-only pointer log ("locale X serves version Y")
-- -----------------------------------------------------------------------------

CREATE TABLE cv.cv_publications (
    seq                bigint GENERATED ALWAYS AS IDENTITY,
    locale             text                     NOT NULL,
    locale_publishable boolean                  NOT NULL,
    version_id         uuid                     NULL,      -- NULL = unpublish
    published_by       uuid                     NOT NULL,
    published_at       timestamp with time zone NOT NULL DEFAULT now(),
    note               text                     NULL,
    CONSTRAINT pk_cv_publications PRIMARY KEY (seq),
    -- Only rows with is_publishable = true match, so 'zxx' can never be published.
    CONSTRAINT fk_cv_publications_locale FOREIGN KEY (locale, locale_publishable)
        REFERENCES cv.locales (code, is_publishable) ON DELETE RESTRICT,
    CONSTRAINT fk_cv_publications_version FOREIGN KEY (version_id)
        REFERENCES cv.cv_versions (id) ON DELETE RESTRICT,
    CONSTRAINT fk_cv_publications_published_by FOREIGN KEY (published_by)
        REFERENCES cv.users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_cv_publications_locale_publishable CHECK (locale_publishable),
    CONSTRAINT ck_cv_publications_note_not_blank CHECK (note IS NULL OR length(btrim(note)) > 0)
);
CREATE INDEX ix_cv_publications_locale_history
    ON cv.cv_publications (locale, locale_publishable, seq DESC);
CREATE INDEX ix_cv_publications_version_id ON cv.cv_publications (version_id);
CREATE INDEX ix_cv_publications_published_by ON cv.cv_publications (published_by);

-- -----------------------------------------------------------------------------
-- Rendered output cache
-- -----------------------------------------------------------------------------

CREATE TABLE cv.cv_renders (
    version_id       uuid                     NOT NULL,
    locale           text                     NOT NULL,
    format           text                     NOT NULL,
    renderer_version text                     NOT NULL,
    content_hash     bytea                    NOT NULL,
    storage_key      text                     NOT NULL,
    created_at       timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_cv_renders PRIMARY KEY (version_id, locale, format, renderer_version),
    CONSTRAINT fk_cv_renders_version FOREIGN KEY (version_id)
        REFERENCES cv.cv_versions (id) ON DELETE RESTRICT,
    CONSTRAINT fk_cv_renders_locale FOREIGN KEY (locale)
        REFERENCES cv.locales (code) ON DELETE RESTRICT,
    CONSTRAINT ck_cv_renders_format CHECK (format IN ('pdf', 'md', 'html')),
    CONSTRAINT ck_cv_renders_not_neutral CHECK (locale <> 'zxx'),
    CONSTRAINT ck_cv_renders_renderer_version_not_blank CHECK (length(btrim(renderer_version)) > 0),
    CONSTRAINT ck_cv_renders_storage_key_not_blank CHECK (length(btrim(storage_key)) > 0),
    CONSTRAINT ck_cv_renders_content_hash_sha256 CHECK (octet_length(content_hash) = 32)
);
CREATE UNIQUE INDEX ux_cv_renders_storage_key ON cv.cv_renders (storage_key);
CREATE INDEX ix_cv_renders_locale ON cv.cv_renders (locale);

-- -----------------------------------------------------------------------------
-- Audit log (logins, restores, publishes, downloads). Version history is
-- already its own audit trail, so content edits are not logged here.
-- -----------------------------------------------------------------------------

CREATE TABLE cv.audit_log (
    id         uuid                     NOT NULL,
    action     text                     NOT NULL,
    subject_id uuid                     NULL,
    details    jsonb                    NOT NULL DEFAULT '{}'::jsonb,
    actor_id   uuid                     NULL,   -- NULL = anonymous or system
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_audit_log PRIMARY KEY (id),
    CONSTRAINT fk_audit_log_actor FOREIGN KEY (actor_id)
        REFERENCES cv.users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_audit_log_action_format CHECK (action ~ '^[A-Z][A-Z_]*$'),
    CONSTRAINT ck_audit_log_details_is_object CHECK (jsonb_typeof(details) = 'object')
);
CREATE INDEX ix_audit_log_actor_id ON cv.audit_log (actor_id);
CREATE INDEX ix_audit_log_created_at ON cv.audit_log (created_at);

-- =============================================================================
-- Functions
-- All functions pin search_path and schema-qualify every reference.
-- =============================================================================

-- Resolves every node of a version for every publishable locale:
-- applies the 'zxx' fallback, propagates omission down the tree, and flags
-- missing and stale translations.
CREATE FUNCTION cv.cv_version_localized(p_version_id uuid)
RETURNS TABLE (
    version_id     uuid,
    locale         text,
    node_id        uuid,
    parent_node_id uuid,
    type_code      text,
    sort_key       text,
    sort_path      text[],    -- ORDER BY sort_path COLLATE "C" gives document order
    depth          integer,
    attrs          jsonb,
    content        text,
    is_omitted     boolean,   -- this node or an ancestor is omitted in this locale
    is_missing     boolean,   -- needs text in this locale and has none (blocks publishing)
    is_stale       boolean    -- translated from source text that has since changed
)
LANGUAGE sql
STABLE
SET search_path = ''
AS $$
    WITH RECURSIVE tree AS (
        SELECT n.node_id, n.parent_node_id, n.type_code, n.sort_key, n.attrs,
               l.code                         AS locale,
               COALESCE(c.is_omitted, false)  AS hidden,
               0                              AS depth,
               ARRAY[n.sort_key::text]        AS sort_path
        FROM cv.cv_nodes n
        CROSS JOIN cv.locales l
        LEFT JOIN cv.cv_node_contents c
               ON c.version_id = n.version_id AND c.node_id = n.node_id AND c.locale = l.code
        WHERE n.version_id = p_version_id
          AND n.parent_node_id IS NULL
          AND l.is_publishable

        UNION ALL

        SELECT n.node_id, n.parent_node_id, n.type_code, n.sort_key, n.attrs,
               t.locale,
               t.hidden OR COALESCE(c.is_omitted, false),
               t.depth + 1,
               t.sort_path || n.sort_key::text
        FROM tree t
        JOIN cv.cv_nodes n
          ON n.version_id = p_version_id AND n.parent_node_id = t.node_id
        LEFT JOIN cv.cv_node_contents c
               ON c.version_id = n.version_id AND c.node_id = n.node_id AND c.locale = t.locale
    )
    SELECT p_version_id,
           t.locale,
           t.node_id,
           t.parent_node_id,
           t.type_code,
           t.sort_key,
           t.sort_path,
           t.depth,
           t.attrs,
           CASE WHEN t.hidden OR NOT nt.has_text THEN NULL
                ELSE COALESCE(c.content, z.content) END,
           t.hidden,
           nt.has_text AND NOT t.hidden AND c.content IS NULL AND z.content IS NULL,
           nt.has_text AND NOT t.hidden AND NOT l.is_source
               AND c.content IS NOT NULL AND s.content IS NOT NULL
               AND c.source_hash IS DISTINCT FROM pg_catalog.sha256(pg_catalog.convert_to(s.content, 'UTF8'))
    FROM tree t
    JOIN cv.node_types nt ON nt.code = t.type_code
    JOIN cv.locales l     ON l.code = t.locale
    LEFT JOIN cv.locales src ON src.is_source
    LEFT JOIN cv.cv_node_contents c
           ON c.version_id = p_version_id AND c.node_id = t.node_id AND c.locale = t.locale
    LEFT JOIN cv.cv_node_contents z
           ON z.version_id = p_version_id AND z.node_id = t.node_id AND z.locale = 'zxx'
    LEFT JOIN cv.cv_node_contents s
           ON s.version_id = p_version_id AND s.node_id = t.node_id AND s.locale = src.code
$$;

-- One row per publishable locale: is this version ready to publish there?
CREATE FUNCTION cv.cv_version_readiness(p_version_id uuid)
RETURNS TABLE (locale text, missing_count bigint, stale_count bigint, can_publish boolean)
LANGUAGE sql
STABLE
SET search_path = ''
AS $$
    SELECT x.locale,
           count(*) FILTER (WHERE x.is_missing),
           count(*) FILTER (WHERE x.is_stale),
           NOT bool_or(x.is_missing)
    FROM cv.cv_version_localized(p_version_id) x
    GROUP BY x.locale
    ORDER BY x.locale
$$;

-- What changed between two versions. Structural changes have locale = NULL;
-- text changes are reported per locale. A node can produce several rows.
CREATE FUNCTION cv.cv_version_diff(p_from_version_id uuid, p_to_version_id uuid)
RETURNS TABLE (node_id uuid, locale text, change text, old_value text, new_value text)
LANGUAGE sql
STABLE
SET search_path = ''
AS $$
    SELECT COALESCE(o.node_id, n.node_id),
           NULL::text,
           ch.change,
           CASE ch.change
               WHEN 'retyped'       THEN o.type_code
               WHEN 'moved'         THEN o.parent_node_id::text || '/' || o.sort_key
               WHEN 'attrs_changed' THEN o.attrs::text
           END,
           CASE ch.change
               WHEN 'retyped'       THEN n.type_code
               WHEN 'moved'         THEN n.parent_node_id::text || '/' || n.sort_key
               WHEN 'attrs_changed' THEN n.attrs::text
           END
    FROM (SELECT * FROM cv.cv_nodes WHERE version_id = p_from_version_id) o
    FULL JOIN (SELECT * FROM cv.cv_nodes WHERE version_id = p_to_version_id) n
           ON n.node_id = o.node_id
    CROSS JOIN LATERAL unnest(ARRAY[
        CASE WHEN o.node_id IS NULL THEN 'added' END,
        CASE WHEN n.node_id IS NULL THEN 'removed' END,
        CASE WHEN o.node_id IS NOT NULL AND n.node_id IS NOT NULL
                  AND o.type_code <> n.type_code THEN 'retyped' END,
        CASE WHEN o.node_id IS NOT NULL AND n.node_id IS NOT NULL
                  AND (o.parent_node_id IS DISTINCT FROM n.parent_node_id
                       OR o.sort_key <> n.sort_key) THEN 'moved' END,
        CASE WHEN o.node_id IS NOT NULL AND n.node_id IS NOT NULL
                  AND o.attrs <> n.attrs THEN 'attrs_changed' END
    ]) AS ch(change)
    WHERE ch.change IS NOT NULL

    UNION ALL

    SELECT COALESCE(o.node_id, n.node_id),
           COALESCE(o.locale, n.locale),
           ch.change,
           o.content,
           n.content
    FROM (SELECT * FROM cv.cv_node_contents WHERE version_id = p_from_version_id) o
    FULL JOIN (SELECT * FROM cv.cv_node_contents WHERE version_id = p_to_version_id) n
           ON n.node_id = o.node_id AND n.locale = o.locale
    CROSS JOIN LATERAL (SELECT CASE
        WHEN o.node_id IS NULL                 THEN 'text_added'
        WHEN n.node_id IS NULL                 THEN 'text_removed'
        WHEN n.is_omitted AND NOT o.is_omitted THEN 'omitted'
        WHEN o.is_omitted AND NOT n.is_omitted THEN 'unomitted'
        WHEN o.content <> n.content            THEN 'text_edited'
    END AS change) ch
    WHERE ch.change IS NOT NULL
$$;

-- -----------------------------------------------------------------------------
-- Trigger functions
-- -----------------------------------------------------------------------------

-- Versions, nodes, contents, publications, renders and the audit log are
-- append-only. Grants already stop cv_app; this also stops the table owner.
-- Deliberate escape hatch (e.g. a GDPR erasure): ALTER TABLE ... DISABLE TRIGGER.
CREATE FUNCTION cv.forbid_mutation()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = ''
AS $$
BEGIN
    RAISE EXCEPTION '% on %.% is not allowed: rows are immutable', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME
        USING ERRCODE = 'restrict_violation',
              HINT = 'Create a new version instead of changing an existing one.';
END
$$;

-- A new version must extend the latest one by exactly one, and must change something.
CREATE FUNCTION cv.check_version_chain()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = ''
AS $$
DECLARE
    v_previous_number integer;
    v_previous_hash   bytea;
    v_restored_number integer;
BEGIN
    IF NEW.previous_version_id IS NOT NULL THEN
        SELECT v.version_number, v.content_hash
          INTO v_previous_number, v_previous_hash
          FROM cv.cv_versions v
         WHERE v.id = NEW.previous_version_id;

        IF FOUND THEN
            IF NEW.version_number <> v_previous_number + 1 THEN
                RAISE EXCEPTION 'Version number must be % (previous is %), got %',
                    v_previous_number + 1, v_previous_number, NEW.version_number
                    USING ERRCODE = 'check_violation';
            END IF;
            IF NEW.content_hash = v_previous_hash THEN
                RAISE EXCEPTION 'No changes: content is identical to version %', v_previous_number
                    USING ERRCODE = 'check_violation';
            END IF;
        END IF;
    END IF;

    IF NEW.restored_from_version_id IS NOT NULL THEN
        SELECT v.version_number INTO v_restored_number
          FROM cv.cv_versions v
         WHERE v.id = NEW.restored_from_version_id;

        IF FOUND AND v_restored_number >= NEW.version_number THEN
            RAISE EXCEPTION 'Can only restore from an earlier version'
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;

    RETURN NEW;
END
$$;

-- Deferred to commit: every version must contain a root node.
CREATE FUNCTION cv.check_version_has_root()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = ''
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM cv.cv_nodes n
         WHERE n.version_id = NEW.id AND n.parent_node_id IS NULL
    ) THEN
        RAISE EXCEPTION 'Version % has no root node', NEW.version_number
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NULL;
END
$$;

-- Deferred to commit: the parent/child type pair must be allowed by the
-- grammar, and following parents must reach the root (no cycles).
CREATE FUNCTION cv.check_node_tree()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = ''
AS $$
DECLARE
    v_parent_type text;
    v_current     uuid;
    v_steps       integer := 0;
BEGIN
    IF NEW.parent_node_id IS NULL THEN
        RETURN NULL;
    END IF;

    SELECT n.type_code INTO v_parent_type
      FROM cv.cv_nodes n
     WHERE n.version_id = NEW.version_id AND n.node_id = NEW.parent_node_id;

    IF FOUND AND NOT EXISTS (
        SELECT 1 FROM cv.node_type_children g
         WHERE g.parent_type = v_parent_type AND g.child_type = NEW.type_code
    ) THEN
        RAISE EXCEPTION 'A % node cannot contain a % node', v_parent_type, NEW.type_code
            USING ERRCODE = 'check_violation';
    END IF;

    v_current := NEW.parent_node_id;
    WHILE v_current IS NOT NULL LOOP
        IF v_current = NEW.node_id THEN
            RAISE EXCEPTION 'Cycle detected: node % is its own ancestor', NEW.node_id
                USING ERRCODE = 'check_violation';
        END IF;

        v_steps := v_steps + 1;
        IF v_steps > 1000 THEN
            RAISE EXCEPTION 'Node % does not reach the root within 1000 levels (cycle among its ancestors?)', NEW.node_id
                USING ERRCODE = 'check_violation';
        END IF;

        SELECT n.parent_node_id INTO v_current
          FROM cv.cv_nodes n
         WHERE n.version_id = NEW.version_id AND n.node_id = v_current;
        IF NOT FOUND THEN
            EXIT;  -- missing parent: reported by fk_cv_nodes_parent
        END IF;
    END LOOP;

    RETURN NULL;
END
$$;

-- Text rules that depend on other tables.
CREATE FUNCTION cv.check_node_content()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = ''
AS $$
DECLARE
    v_has_text  boolean;
    v_is_source boolean;
BEGIN
    SELECT t.has_text INTO v_has_text
      FROM cv.cv_nodes n
      JOIN cv.node_types t ON t.code = n.type_code
     WHERE n.version_id = NEW.version_id AND n.node_id = NEW.node_id;

    IF FOUND AND NOT v_has_text AND NOT NEW.is_omitted THEN
        RAISE EXCEPTION 'Nodes of this type carry no text; they can only be omitted'
            USING ERRCODE = 'check_violation';
    END IF;

    SELECT l.is_source INTO v_is_source FROM cv.locales l WHERE l.code = NEW.locale;
    IF v_is_source AND NEW.source_hash IS NOT NULL THEN
        RAISE EXCEPTION 'The source locale (%) cannot have a source_hash', NEW.locale
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END
$$;

-- A version can only be published for a locale once nothing is missing there.
-- Stale translations are allowed (the app warns and asks for confirmation).
CREATE FUNCTION cv.check_publication()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = ''
AS $$
DECLARE
    v_missing bigint;
BEGIN
    IF NEW.version_id IS NULL THEN
        RETURN NEW;  -- unpublishing is always allowed
    END IF;

    SELECT count(*) INTO v_missing
      FROM cv.cv_version_localized(NEW.version_id) x
     WHERE x.locale = NEW.locale AND x.is_missing;

    IF v_missing > 0 THEN
        RAISE EXCEPTION 'Cannot publish locale %: % node(s) have no text', NEW.locale, v_missing
            USING ERRCODE = 'check_violation',
                  HINT = 'Translate them or mark them as omitted for this locale.';
    END IF;

    RETURN NEW;
END
$$;

-- -----------------------------------------------------------------------------
-- Triggers
-- -----------------------------------------------------------------------------

CREATE TRIGGER trg_cv_versions_chain
    BEFORE INSERT ON cv.cv_versions
    FOR EACH ROW EXECUTE FUNCTION cv.check_version_chain();

CREATE CONSTRAINT TRIGGER trg_cv_versions_has_root
    AFTER INSERT ON cv.cv_versions
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION cv.check_version_has_root();

CREATE CONSTRAINT TRIGGER trg_cv_nodes_tree
    AFTER INSERT ON cv.cv_nodes
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION cv.check_node_tree();

CREATE TRIGGER trg_cv_node_contents_rules
    BEFORE INSERT ON cv.cv_node_contents
    FOR EACH ROW EXECUTE FUNCTION cv.check_node_content();

CREATE TRIGGER trg_cv_publications_ready
    BEFORE INSERT ON cv.cv_publications
    FOR EACH ROW EXECUTE FUNCTION cv.check_publication();

DO $$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['cv_versions', 'cv_nodes', 'cv_node_contents',
                             'cv_publications', 'cv_renders', 'audit_log']
    LOOP
        EXECUTE format(
            'CREATE TRIGGER trg_%1$s_immutable BEFORE UPDATE OR DELETE ON cv.%1$I
                 FOR EACH ROW EXECUTE FUNCTION cv.forbid_mutation()', t);
        EXECUTE format(
            'CREATE TRIGGER trg_%1$s_no_truncate BEFORE TRUNCATE ON cv.%1$I
                 FOR EACH STATEMENT EXECUTE FUNCTION cv.forbid_mutation()', t);
    END LOOP;
END
$$;

-- =============================================================================
-- Views
-- =============================================================================

CREATE VIEW cv.cv_latest_version WITH (security_invoker = true) AS
SELECT v.*
FROM cv.cv_versions v
ORDER BY v.version_number DESC
LIMIT 1;

-- What each locale serves right now. A locale whose latest publication row
-- is an unpublish (version_id NULL) does not appear.
CREATE VIEW cv.cv_published WITH (security_invoker = true) AS
SELECT latest.locale, latest.version_id, latest.published_at, latest.published_by, latest.seq
FROM (
    SELECT DISTINCT ON (p.locale) p.*
    FROM cv.cv_publications p
    ORDER BY p.locale, p.seq DESC
) latest
WHERE latest.version_id IS NOT NULL;

-- =============================================================================
-- Seed data
-- =============================================================================

INSERT INTO cv.locales (code, name, is_source, is_publishable) VALUES
    ('en',  'English',          true,  true),
    ('nb',  'Norsk bokmål',     false, true),   -- 'nb', not the macrolanguage 'no'
    ('fr',  'Français',         false, true),
    ('zxx', 'Language-neutral', false, false);  -- BCP 47: no linguistic content

INSERT INTO cv.node_types (code, description, has_text) VALUES
    ('root',        'The document itself. Exactly one per version.',                      false),
    ('name',        'Your name.',                                                         true),
    ('headline',    'One-line professional title under the name.',                        true),
    ('contact',     'Email, phone, URL or location. attrs.kind says which.',              true),
    ('section',     'Top-level heading such as Experience or Education.',                 true),
    ('entry',       'A role, degree or project. attrs holds start/end dates.',            true),
    ('subtitle',    'Organisation and place under an entry.',                             true),
    ('paragraph',   'Free text.',                                                         true),
    ('bullet',      'One bullet point.',                                                  true),
    ('skill_group', 'A labelled group of skills, e.g. Languages.',                        true),
    ('skill',       'A single skill.',                                                    true);

INSERT INTO cv.node_type_children (parent_type, child_type) VALUES
    ('root',        'name'),
    ('root',        'headline'),
    ('root',        'contact'),
    ('root',        'section'),
    ('section',     'entry'),
    ('section',     'paragraph'),
    ('section',     'bullet'),
    ('section',     'skill_group'),
    ('section',     'skill'),
    ('entry',       'subtitle'),
    ('entry',       'paragraph'),
    ('entry',       'bullet'),
    ('skill_group', 'skill');

-- =============================================================================
-- Privileges
-- =============================================================================

REVOKE ALL ON SCHEMA cv FROM PUBLIC;
REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA cv FROM PUBLIC;

GRANT USAGE ON SCHEMA cv TO cv_app;
GRANT SELECT ON ALL TABLES IN SCHEMA cv TO cv_app;   -- includes views
GRANT INSERT ON cv.cv_versions, cv.cv_nodes, cv.cv_node_contents,
                cv.cv_publications, cv.cv_renders, cv.audit_log TO cv_app;
GRANT INSERT, UPDATE (email, display_name) ON cv.users TO cv_app;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA cv TO cv_app;
GRANT EXECUTE ON FUNCTION cv.cv_version_localized(uuid),
                          cv.cv_version_readiness(uuid),
                          cv.cv_version_diff(uuid, uuid) TO cv_app;

-- Supabase: the Data API roles must never reach this schema. The schema is not
-- in the API's exposed list, and these revokes make that belt-and-braces.
-- (Skipped on plain PostgreSQL, where these roles do not exist.)
DO $$
DECLARE
    r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['anon', 'authenticated']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = r) THEN
            EXECUTE format('REVOKE ALL ON SCHEMA cv FROM %I', r);
            EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA cv FROM %I', r);
            EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA cv FROM %I', r);
            EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cv FROM %I', r);
        END IF;
    END LOOP;
END
$$;
