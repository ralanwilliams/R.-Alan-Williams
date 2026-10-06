-- =============================================================================
-- StoreRenderContent (up)
--
-- Public CV downloads, ADR 0003 §1 and §3:
--   * cv_renders stores the rendered file itself (content) instead of a key
--     pointing at object storage (storage_key).
--   * cv.cv_public_render() returns a published file, and nothing else.
--   * cv_public, a NOLOGIN role that may call only that function. The public
--     site's login (cv_web) is a member, created by hand (docs/cv-database.md).
--
-- The rule that a version can only be published once its files exist (ADR 0003
-- §2) comes in a later migration, together with the editor code that stores
-- them, so publishing keeps working in between.
-- =============================================================================

-- Nothing has written cv_renders yet. If something has, its rows point at files
-- elsewhere and cannot be converted, so stop rather than guess.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM cv.cv_renders) THEN
        RAISE EXCEPTION 'cv.cv_renders is not empty: its rows reference stored files and cannot be converted'
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;
END
$$;

-- -----------------------------------------------------------------------------
-- Rendered files live in the table
-- -----------------------------------------------------------------------------

DROP INDEX cv.ux_cv_renders_storage_key;
ALTER TABLE cv.cv_renders DROP CONSTRAINT ck_cv_renders_storage_key_not_blank;
ALTER TABLE cv.cv_renders DROP COLUMN storage_key;

ALTER TABLE cv.cv_renders ADD COLUMN content bytea NOT NULL;
-- PDFs are already compressed; don't spend CPU trying again. Large values still
-- move out of line (TOAST), so reading other columns stays cheap.
ALTER TABLE cv.cv_renders ALTER COLUMN content SET STORAGE EXTERNAL;
ALTER TABLE cv.cv_renders
    ADD CONSTRAINT ck_cv_renders_content_not_empty CHECK (octet_length(content) > 0),
    ADD CONSTRAINT ck_cv_renders_content_hash_matches CHECK (content_hash = sha256(content));

-- -----------------------------------------------------------------------------
-- Public read access: one function, one role
-- -----------------------------------------------------------------------------

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'cv_public') THEN
        CREATE ROLE cv_public NOLOGIN;
    END IF;
END
$$;

-- The file a public URL serves, or no row.
--   p_version_number NULL: the version cv_published lists for the locale now.
--   p_version_number n:    version n, only if it was ever published for the
--                          locale, so a draft is never served (ADR 0001 §7).
-- The newest render of that version, locale and format wins, so re-rendering
-- after a renderer change replaces what is served without touching history.
-- SECURITY DEFINER: callers need no access to any table. Unknown locales,
-- formats and versions simply return nothing.
CREATE FUNCTION cv.cv_public_render(p_locale text, p_format text, p_version_number integer DEFAULT NULL)
RETURNS TABLE (
    version_number integer,
    name           text,       -- the CV's name line in this locale, for the download's file name
    content        bytea,
    content_hash   bytea,
    rendered_at    timestamp with time zone
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = ''
AS $$
    WITH target AS (
        SELECT v.id, v.version_number
          FROM cv.cv_versions v
         WHERE CASE
                   WHEN p_version_number IS NULL THEN
                       v.id = (SELECT p.version_id FROM cv.cv_published p WHERE p.locale = p_locale)
                   ELSE
                       v.version_number = p_version_number
                       AND EXISTS (SELECT 1
                                     FROM cv.cv_publications p
                                    WHERE p.locale = p_locale AND p.version_id = v.id)
               END
    )
    SELECT t.version_number,
           (SELECT x.content
              FROM cv.cv_version_localized(t.id) x
             WHERE x.locale = p_locale AND x.type_code = 'name' AND x.content IS NOT NULL
             ORDER BY x.sort_path COLLATE "C"
             LIMIT 1),
           r.content,
           r.content_hash,
           r.created_at
      FROM target t
      JOIN cv.cv_renders r
        ON r.version_id = t.id AND r.locale = p_locale AND r.format = p_format
     ORDER BY r.created_at DESC, r.renderer_version DESC
     LIMIT 1
$$;

-- New functions are executable by PUBLIC by default; this one is for cv_public only.
REVOKE ALL ON FUNCTION cv.cv_public_render(text, text, integer) FROM PUBLIC;
GRANT USAGE ON SCHEMA cv TO cv_public;   -- to resolve the function's name; grants no table access
GRANT EXECUTE ON FUNCTION cv.cv_public_render(text, text, integer) TO cv_public;

-- Supabase: keep the Data API roles out, as InitialCreate does.
DO $$
DECLARE
    r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['anon', 'authenticated']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = r) THEN
            EXECUTE format('REVOKE ALL ON FUNCTION cv.cv_public_render(text, text, integer) FROM %I', r);
        END IF;
    END LOOP;
END
$$;
