-- =============================================================================
-- InitialCreate (down)
--
-- Removes everything the up script created. The cv schema itself is kept
-- because EF's __EFMigrationsHistory table lives in it.
-- DROP TABLE is not blocked by the immutability triggers, so this works
-- without disabling them, but it DELETES ALL CV DATA. Back up first.
-- =============================================================================

DROP VIEW IF EXISTS cv.cv_published;
DROP VIEW IF EXISTS cv.cv_latest_version;

DROP TABLE IF EXISTS cv.audit_log;
DROP TABLE IF EXISTS cv.cv_renders;
DROP TABLE IF EXISTS cv.cv_publications;
DROP TABLE IF EXISTS cv.cv_node_contents;
DROP TABLE IF EXISTS cv.cv_nodes;
DROP TABLE IF EXISTS cv.cv_versions;
DROP TABLE IF EXISTS cv.node_type_children;
DROP TABLE IF EXISTS cv.node_types;
DROP TABLE IF EXISTS cv.locales;
DROP TABLE IF EXISTS cv.users;

DROP FUNCTION IF EXISTS cv.check_publication();
DROP FUNCTION IF EXISTS cv.check_node_content();
DROP FUNCTION IF EXISTS cv.check_node_tree();
DROP FUNCTION IF EXISTS cv.check_version_has_root();
DROP FUNCTION IF EXISTS cv.check_version_chain();
DROP FUNCTION IF EXISTS cv.forbid_mutation();
DROP FUNCTION IF EXISTS cv.cv_version_diff(uuid, uuid);
DROP FUNCTION IF EXISTS cv.cv_version_readiness(uuid);
DROP FUNCTION IF EXISTS cv.cv_version_localized(uuid);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'cv_app') THEN
        REVOKE ALL ON ALL TABLES IN SCHEMA cv FROM cv_app;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cv FROM cv_app;
        REVOKE ALL ON SCHEMA cv FROM cv_app;
        DROP ROLE cv_app;
    END IF;
END
$$;
