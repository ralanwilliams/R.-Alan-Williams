-- =============================================================================
-- StoreRenderContent (down)
--
-- Restores storage_key. Fails (by design) once any file has been stored: the
-- files exist only in this table, and history is never rewritten.
-- Dropping cv_public also revokes cv_web's membership; drop the cv_web login
-- by hand afterwards, since it can no longer do anything.
-- =============================================================================

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM cv.cv_renders) THEN
        RAISE EXCEPTION 'cv.cv_renders holds stored files; rolling back would delete them'
            USING ERRCODE = 'object_not_in_prerequisite_state';
    END IF;
END
$$;

DROP FUNCTION cv.cv_public_render(text, text, integer);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'cv_public') THEN
        REVOKE ALL ON SCHEMA cv FROM cv_public;
        DROP ROLE cv_public;
    END IF;
END
$$;

ALTER TABLE cv.cv_renders
    DROP CONSTRAINT ck_cv_renders_content_hash_matches,
    DROP CONSTRAINT ck_cv_renders_content_not_empty,
    DROP COLUMN content;

ALTER TABLE cv.cv_renders ADD COLUMN storage_key text NOT NULL;
ALTER TABLE cv.cv_renders
    ADD CONSTRAINT ck_cv_renders_storage_key_not_blank CHECK (length(btrim(storage_key)) > 0);
CREATE UNIQUE INDEX ux_cv_renders_storage_key ON cv.cv_renders (storage_key);
