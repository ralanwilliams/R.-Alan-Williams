-- =============================================================================
-- RequireRendersToPublish (up)
--
-- ADR 0003 §2: a version can be published for a locale only once cv_renders
-- holds its file in every format. The editor stores the files before the
-- publication rows, in the same transaction, so a publish whose rendering
-- failed publishes nothing.
--
-- The trigger only checks new publication rows. Versions published before
-- this migration keep their rows; the editor's backfill command stores their
-- files (docs/cv-editor.md).
-- =============================================================================

-- Same function as in InitialCreate, plus the files rule at the end.
-- CREATE OR REPLACE keeps the trigger, the owner and the privileges.
CREATE OR REPLACE FUNCTION cv.check_publication()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = ''
AS $$
DECLARE
    v_missing bigint;
    v_missing_formats text;
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

    -- A locale that can never be published is fk_cv_publications_locale's to
    -- refuse: its message names the real problem.
    IF NOT EXISTS (SELECT 1 FROM cv.locales l WHERE l.code = NEW.locale AND l.is_publishable) THEN
        RETURN NEW;
    END IF;

    -- Every format that ck_cv_renders_format allows. A file from any renderer
    -- version counts; cv_public_render serves the newest.
    SELECT string_agg(f.format, ', ' ORDER BY f.format) INTO v_missing_formats
      FROM unnest(ARRAY['html', 'md', 'pdf']) AS f(format)
     WHERE NOT EXISTS (SELECT 1
                         FROM cv.cv_renders r
                        WHERE r.version_id = NEW.version_id
                          AND r.locale = NEW.locale
                          AND r.format = f.format);

    IF v_missing_formats IS NOT NULL THEN
        RAISE EXCEPTION 'Cannot publish locale %: no stored file in format(s) %.', NEW.locale, v_missing_formats
            USING ERRCODE = 'check_violation',
                  HINT = 'Store the rendered files in cv.cv_renders first, in the same transaction.';
    END IF;

    RETURN NEW;
END
$$;
