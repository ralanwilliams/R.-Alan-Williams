-- =============================================================================
-- RequireRendersToPublish (down)
--
-- Restores the publish trigger function from InitialCreate: missing text still
-- blocks publishing, missing files no longer do. Always safe to run.
-- =============================================================================

CREATE OR REPLACE FUNCTION cv.check_publication()
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
