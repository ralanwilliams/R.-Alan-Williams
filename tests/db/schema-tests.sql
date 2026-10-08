-- =============================================================================
-- Schema tests for the cv schema.
--
-- Builds a small multilingual CV, then tries to break every invariant and
-- checks that the database refuses with the expected SQLSTATE.
--
-- Everything runs in one transaction that is ROLLED BACK at the end, so the
-- script leaves no data behind. Still: run it against a local or throwaway
-- database, not production.
--
--   psql "<connection string>" -v ON_ERROR_STOP=1 -f tests/db/schema-tests.sql
--
-- Must run as the schema owner (the role that applied the migration).
-- Output: one "ok" NOTICE per check; any failure aborts with "FAIL".
-- =============================================================================

\set ON_ERROR_STOP on
SET client_min_messages = notice;
-- Query results are noise; only the NOTICE lines matter. Send them to the null
-- device, which is NUL on Windows. :VERSION is the psql client's own build string.
SELECT CASE WHEN :'VERSION' ~* '(windows|mingw|msvc)' THEN 'NUL' ELSE '/dev/null' END AS null_device \gset
\o :null_device

BEGIN;

-- --- helpers -----------------------------------------------------------------

-- Runs the statements in a subtransaction, forcing deferred constraints to fire,
-- and asserts that they fail with the given SQLSTATE.
CREATE FUNCTION pg_temp.expect_error(p_label text, p_state text, VARIADIC p_statements text[])
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    s text;
BEGIN
    BEGIN
        FOREACH s IN ARRAY p_statements LOOP
            EXECUTE s;
        END LOOP;
        SET CONSTRAINTS ALL IMMEDIATE;
    EXCEPTION WHEN OTHERS THEN
        SET CONSTRAINTS ALL DEFERRED;
        IF SQLSTATE = p_state THEN
            RAISE NOTICE 'ok   % (% %)', p_label, SQLSTATE, SQLERRM;
            RETURN;
        END IF;
        RAISE EXCEPTION 'FAIL % : expected %, got % (%)', p_label, p_state, SQLSTATE, SQLERRM;
    END;
    RAISE EXCEPTION 'FAIL % : statements succeeded but should have failed', p_label;
END $$;

CREATE FUNCTION pg_temp.expect(p_label text, p_condition boolean)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    IF p_condition IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'FAIL %', p_label;
    END IF;
    RAISE NOTICE 'ok   %', p_label;
END $$;

CREATE FUNCTION pg_temp.h(t text) RETURNS bytea LANGUAGE sql IMMUTABLE AS
$$ SELECT sha256(convert_to(t, 'UTF8')) $$;

-- Fixed ids keep the script readable.
--   user  ...0001        versions ...00a1 / 00a2 / 00a3
--   nodes ...01xx
CREATE TEMP TABLE ids (k text PRIMARY KEY, id uuid NOT NULL) ON COMMIT DROP;
INSERT INTO ids VALUES
    ('user',    '00000000-0000-7000-8000-000000000001'),
    ('v1',      '00000000-0000-7000-8000-0000000000a1'),
    ('v2',      '00000000-0000-7000-8000-0000000000a2'),
    ('v3',      '00000000-0000-7000-8000-0000000000a3'),
    ('root',    '00000000-0000-7000-8000-000000000100'),
    ('name',    '00000000-0000-7000-8000-000000000101'),
    ('email',   '00000000-0000-7000-8000-000000000102'),
    ('exp',     '00000000-0000-7000-8000-000000000110'),
    ('job',     '00000000-0000-7000-8000-000000000111'),
    ('company', '00000000-0000-7000-8000-000000000112'),
    ('b1',      '00000000-0000-7000-8000-000000000113'),
    ('b2',      '00000000-0000-7000-8000-000000000114'),
    ('oldjob',  '00000000-0000-7000-8000-000000000115'),
    ('certs',   '00000000-0000-7000-8000-000000000120'),
    ('cert',    '00000000-0000-7000-8000-000000000121');

CREATE FUNCTION pg_temp.id(p_key text) RETURNS uuid LANGUAGE sql STABLE AS
$$ SELECT id FROM ids WHERE k = p_key $$;

-- Stores a version's file in every format for a locale, as the editor does
-- before publishing (ADR 0003 §2). The content is '<version> <locale> <format>'.
CREATE FUNCTION pg_temp.add_files(p_version text, p_locale text) RETURNS void LANGUAGE sql AS $$
    INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content)
    SELECT pg_temp.id(p_version), p_locale, f.format, 'r/1', sha256(x.content), x.content
      FROM unnest(ARRAY['pdf', 'html', 'md']) AS f(format),
           LATERAL (SELECT convert_to(p_version || ' ' || p_locale || ' ' || f.format, 'UTF8')) AS x(content)
$$;

-- --- fixture: version 1 ------------------------------------------------------

INSERT INTO cv.users (id, email, display_name)
VALUES (pg_temp.id('user'), 'owner@example.com', 'CV Owner');

INSERT INTO cv.cv_versions (id, version_number, summary, content_hash, created_by)
VALUES (pg_temp.id('v1'), 1, 'First version', pg_temp.h('v1'), pg_temp.id('user'));

-- Children inserted before their parents on purpose: the parent FK is deferred.
INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key, attrs)
SELECT pg_temp.id('v1'), pg_temp.id(n), CASE WHEN p IS NULL THEN NULL ELSE pg_temp.id(p) END, t, s, a::jsonb
FROM (VALUES
    ('b1',      'job',   'bullet',  'a0', '{}'),
    ('b2',      'job',   'bullet',  'a1', '{}'),
    ('company', 'job',   'subtitle','Zz', '{}'),
    ('job',     'exp',   'entry',   'a0', '{"start":"2021-03","end":null}'),
    ('oldjob',  'exp',   'entry',   'a1', '{"start":"2018-01","end":"2021-02"}'),
    ('exp',     'root',  'section', 'a2', '{}'),
    ('certs',   'root',  'section', 'a3', '{}'),
    ('cert',    'certs', 'entry',   'a0', '{}'),
    ('name',    'root',  'name',    'a0', '{}'),
    ('email',   'root',  'contact', 'a1', '{"kind":"email"}'),
    ('root',    NULL,    'root',    'a0', '{}')
) AS x(n, p, t, s, a);

INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted, source_hash)
SELECT pg_temp.id('v1'), pg_temp.id(n), l, c, c IS NULL,
       CASE WHEN l IN ('nb', 'fr') AND c IS NOT NULL THEN pg_temp.h(src) END
FROM (VALUES
    ('name',    'zxx', 'R. Alan Williams',          NULL),
    ('email',   'zxx', 'owner@example.com',         NULL),
    ('company', 'zxx', 'Acme AS, Oslo',             NULL),
    ('exp',     'en',  'Experience',                NULL),
    ('exp',     'nb',  'Erfaring',                  'Experience'),
    ('exp',     'fr',  'Expérience',                'Experience'),
    ('job',     'en',  'Software Engineer',         NULL),
    ('job',     'nb',  'Programvareutvikler',       'Software Engineer'),
    ('job',     'fr',  'Ingénieur logiciel',        'Software Engineer'),
    ('oldjob',  'en',  'Junior Developer',          NULL),
    ('oldjob',  'nb',  'Juniorutvikler',            'Junior Developer'),
    ('oldjob',  'fr',  'Développeur junior',        'Junior Developer'),
    ('b1',      'en',  'Built the CV editor',       NULL),
    ('b1',      'nb',  'Bygde CV-editoren',         'Built the CV editor'),
    ('b1',      'fr',  'A construit l''éditeur',    'Built the CV editor'),
    ('b2',      'en',  'Shipped Squigglepig',       NULL),
    ('b2',      'nb',  'Lanserte Squigglepig',      'Shipped Squigglepig'),
    -- b2 has no French text yet: French must be blocked from publishing.
    ('certs',   'en',  'Certifications',            NULL),
    ('certs',   'nb',  'Sertifiseringer',           'Certifications'),
    ('certs',   'fr',  NULL,                        NULL),   -- omitted in French...
    ('cert',    'en',  'Azure Fundamentals',        NULL),
    ('cert',    'nb',  'Azure Fundamentals',        'Azure Fundamentals')
    -- ...so 'cert' needs no French text.
) AS x(n, l, c, src);

SET CONSTRAINTS ALL IMMEDIATE;   -- version 1 is a valid tree
SET CONSTRAINTS ALL DEFERRED;
SELECT pg_temp.expect('version 1 tree is valid', true);

-- --- tree rules ----------------------------------------------------------------

SELECT pg_temp.expect_error('second root in a version', '23505', format(
    $q$INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key)
       VALUES (%L, gen_random_uuid(), NULL, 'root', 'b0')$q$, pg_temp.id('v1')));

SELECT pg_temp.expect_error('non-root node without parent', '23514', format(
    $q$INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key)
       VALUES (%L, gen_random_uuid(), NULL, 'bullet', 'b0')$q$, pg_temp.id('v1')));

SELECT pg_temp.expect_error('root node with a parent', '23514', format(
    $q$INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key)
       VALUES (%L, gen_random_uuid(), %L, 'root', 'b0')$q$, pg_temp.id('v1'), pg_temp.id('root')));

SELECT pg_temp.expect_error('grammar: bullet directly under root', '23514', format(
    $q$INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key)
       VALUES (%L, gen_random_uuid(), %L, 'bullet', 'b0')$q$, pg_temp.id('v1'), pg_temp.id('root')));

SELECT pg_temp.expect_error('duplicate sibling sort key', '23505', format(
    $q$INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key)
       VALUES (%L, gen_random_uuid(), %L, 'bullet', 'a0')$q$, pg_temp.id('v1'), pg_temp.id('job')));

SELECT pg_temp.expect_error('parent does not exist in this version', '23503', format(
    $q$INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key)
       VALUES (%L, gen_random_uuid(), gen_random_uuid(), 'bullet', 'b0')$q$, pg_temp.id('v1')));

SELECT pg_temp.expect_error('invalid sort key characters', '23514', format(
    $q$INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key)
       VALUES (%L, gen_random_uuid(), %L, 'bullet', 'a 0')$q$, pg_temp.id('v1'), pg_temp.id('job')));

SELECT pg_temp.expect_error('attrs must be a JSON object', '23514', format(
    $q$INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key, attrs)
       VALUES (%L, gen_random_uuid(), %L, 'bullet', 'b0', '[1,2]')$q$, pg_temp.id('v1'), pg_temp.id('job')));

-- The seeded grammar is acyclic, so to prove cycle detection works we
-- temporarily allow bullets inside bullets (rolled back with the subtransaction).
SELECT pg_temp.expect_error('cycle between two nodes', '23514',
    $q$INSERT INTO cv.node_type_children VALUES ('bullet', 'bullet')$q$,
    format($q$INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key) VALUES
              (%1$L, '00000000-0000-7000-8000-0000000001f1', '00000000-0000-7000-8000-0000000001f2', 'bullet', 'b0'),
              (%1$L, '00000000-0000-7000-8000-0000000001f2', '00000000-0000-7000-8000-0000000001f1', 'bullet', 'b0')$q$,
           pg_temp.id('v1')));

-- --- text rules ----------------------------------------------------------------

SELECT pg_temp.expect_error('omitted row with text', '23514', format(
    $q$INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted)
       VALUES (%L, %L, 'fr', 'Hello', true)$q$, pg_temp.id('v1'), pg_temp.id('b2')));

SELECT pg_temp.expect_error('blank text', '23514', format(
    $q$INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted)
       VALUES (%L, %L, 'fr', '   ', false)$q$, pg_temp.id('v1'), pg_temp.id('b2')));

SELECT pg_temp.expect_error('language-neutral text cannot be omitted', '23514', format(
    $q$INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted)
       VALUES (%L, %L, 'zxx', NULL, true)$q$, pg_temp.id('v1'), pg_temp.id('b2')));

SELECT pg_temp.expect_error('text on the root node', '23514', format(
    $q$INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted)
       VALUES (%L, %L, 'en', 'My CV', false)$q$, pg_temp.id('v1'), pg_temp.id('root')));

SELECT pg_temp.expect_error('source_hash on the source locale', '23514', format(
    $q$INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted, source_hash)
       VALUES (%L, %L, 'en', 'x', false, sha256('x'))$q$, pg_temp.id('v1'), pg_temp.id('certs')));

SELECT pg_temp.expect_error('unknown locale', '23503', format(
    $q$INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted)
       VALUES (%L, %L, 'de', 'Erfahrung', false)$q$, pg_temp.id('v1'), pg_temp.id('exp')));

-- --- readiness and publishing ------------------------------------------------------

SELECT pg_temp.expect('v1 readiness: en and nb complete, fr missing exactly b2',
    (SELECT array_agg(locale || ':' || missing_count || ':' || can_publish ORDER BY locale)
       FROM cv.cv_version_readiness(pg_temp.id('v1')))
    = ARRAY['en:0:true', 'fr:1:false', 'nb:0:true']);

SELECT pg_temp.expect('omitted section hides its subtree in fr',
    (SELECT is_omitted AND NOT is_missing AND content IS NULL
       FROM cv.cv_version_localized(pg_temp.id('v1'))
      WHERE locale = 'fr' AND node_id = pg_temp.id('cert')));

SELECT pg_temp.expect('zxx text is used for every locale',
    (SELECT bool_and(content = 'Acme AS, Oslo')
       FROM cv.cv_version_localized(pg_temp.id('v1'))
      WHERE node_id = pg_temp.id('company')));

SELECT pg_temp.expect('document order follows sort keys (C collation)',
    (SELECT array_agg(node_id ORDER BY sort_path COLLATE "C")
       FROM cv.cv_version_localized(pg_temp.id('v1'))
      WHERE locale = 'en' AND parent_node_id = pg_temp.id('job'))
    = ARRAY[pg_temp.id('company'), pg_temp.id('b1'), pg_temp.id('b2')]);

SELECT pg_temp.expect_error('publish fr while b2 is untranslated', '23514', format(
    $q$INSERT INTO cv.cv_publications (locale, locale_publishable, version_id, published_by)
       VALUES ('fr', true, %L, %L)$q$, pg_temp.id('v1'), pg_temp.id('user')));

SELECT pg_temp.expect_error('publish the language-neutral locale', '23503', format(
    $q$INSERT INTO cv.cv_publications (locale, locale_publishable, version_id, published_by)
       VALUES ('zxx', true, %L, %L)$q$, pg_temp.id('v1'), pg_temp.id('user')));

SELECT pg_temp.expect_error('publication row claiming a non-publishable locale', '23514', format(
    $q$INSERT INTO cv.cv_publications (locale, locale_publishable, version_id, published_by)
       VALUES ('zxx', false, %L, %L)$q$, pg_temp.id('v1'), pg_temp.id('user')));

SELECT pg_temp.expect_error('publish en before its files are stored', '23514', format(
    $q$INSERT INTO cv.cv_publications (locale, locale_publishable, version_id, published_by)
       VALUES ('en', true, %L, %L)$q$, pg_temp.id('v1'), pg_temp.id('user')));

SELECT pg_temp.expect_error('publish en with its PDF but no HTML or Markdown', '23514',
    format($q$INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content)
              VALUES (%L, 'en', 'pdf', 'r/1', sha256('pdf'), 'pdf')$q$, pg_temp.id('v1')),
    format($q$INSERT INTO cv.cv_publications (locale, locale_publishable, version_id, published_by)
              VALUES ('en', true, %L, %L)$q$, pg_temp.id('v1'), pg_temp.id('user')));

SELECT pg_temp.add_files('v1', 'en');
SELECT pg_temp.add_files('v1', 'nb');
INSERT INTO cv.cv_publications (locale, locale_publishable, version_id, published_by)
VALUES ('en', true, pg_temp.id('v1'), pg_temp.id('user')),
       ('nb', true, pg_temp.id('v1'), pg_temp.id('user'));
SELECT pg_temp.expect('en and nb published', (SELECT count(*) FROM cv.cv_published) = 2);

-- --- version 2: edit English b1, add French b2, drop oldjob, reorder -------------

INSERT INTO cv.cv_versions (id, version_number, previous_version_id, summary, content_hash, created_by)
VALUES (pg_temp.id('v2'), 2, pg_temp.id('v1'), 'Reworded b1, French b2', pg_temp.h('v2'), pg_temp.id('user'));

INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key, attrs)
SELECT pg_temp.id('v2'), node_id, parent_node_id, type_code,
       CASE WHEN node_id = pg_temp.id('b2') THEN 'Zy' ELSE sort_key END,   -- b2 moves first
       attrs
FROM cv.cv_nodes
WHERE version_id = pg_temp.id('v1') AND node_id <> pg_temp.id('oldjob');

INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted, source_hash)
SELECT pg_temp.id('v2'), node_id, locale,
       CASE WHEN node_id = pg_temp.id('b1') AND locale = 'en'
            THEN 'Built a multilingual CV editor' ELSE content END,
       is_omitted, source_hash
FROM cv.cv_node_contents
WHERE version_id = pg_temp.id('v1') AND node_id <> pg_temp.id('oldjob');

INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted, source_hash)
VALUES (pg_temp.id('v2'), pg_temp.id('b2'), 'fr', 'A lancé Squigglepig', false, pg_temp.h('Shipped Squigglepig'));

SET CONSTRAINTS ALL IMMEDIATE;
SET CONSTRAINTS ALL DEFERRED;

SELECT pg_temp.expect('v2: nb and fr b1 are stale after the English edit, nothing missing',
    (SELECT array_agg(locale || ':' || missing_count || ':' || stale_count ORDER BY locale)
       FROM cv.cv_version_readiness(pg_temp.id('v2')))
    = ARRAY['en:0:0', 'fr:0:1', 'nb:0:1']);

SELECT pg_temp.expect('diff v1 -> v2',
    (SELECT array_agg(k.k || ':' || COALESCE(d.locale, '-') || ':' || d.change
                      ORDER BY k.k, d.locale NULLS FIRST, d.change)
       FROM cv.cv_version_diff(pg_temp.id('v1'), pg_temp.id('v2')) d
       JOIN ids k ON k.id = d.node_id)
    = ARRAY['b1:en:text_edited', 'b2:-:moved', 'b2:fr:text_added',
            'oldjob:-:removed', 'oldjob:en:text_removed', 'oldjob:fr:text_removed', 'oldjob:nb:text_removed']);

SELECT pg_temp.expect_error('v1''s files do not count for v2', '23514', format(
    $q$INSERT INTO cv.cv_publications (locale, locale_publishable, version_id, published_by)
       VALUES ('en', true, %L, %L)$q$, pg_temp.id('v2'), pg_temp.id('user')));

-- Stale translations do not block publishing; the app warns instead.
SELECT pg_temp.add_files('v2', 'fr');
INSERT INTO cv.cv_publications (locale, locale_publishable, version_id, published_by)
VALUES ('fr', true, pg_temp.id('v2'), pg_temp.id('user'));
SELECT pg_temp.expect('fr publishes v2 while en/nb still serve v1',
    (SELECT array_agg(locale || ':' || (version_id = pg_temp.id('v2')) ORDER BY locale) FROM cv.cv_published)
    = ARRAY['en:false', 'fr:true', 'nb:false']);

-- --- version chain ---------------------------------------------------------------

SELECT pg_temp.expect_error('branch: second successor of v1 (stale editor tab)', '23505', format(
    $q$INSERT INTO cv.cv_versions (id, version_number, previous_version_id, content_hash, created_by)
       VALUES (gen_random_uuid(), 2, %L, sha256('x'), %L)$q$, pg_temp.id('v1'), pg_temp.id('user')));

SELECT pg_temp.expect_error('skipped version number', '23514', format(
    $q$INSERT INTO cv.cv_versions (id, version_number, previous_version_id, content_hash, created_by)
       VALUES (gen_random_uuid(), 4, %L, sha256('x'), %L)$q$, pg_temp.id('v2'), pg_temp.id('user')));

SELECT pg_temp.expect_error('second initial version', '23505', format(
    $q$INSERT INTO cv.cv_versions (id, version_number, previous_version_id, content_hash, created_by)
       VALUES (gen_random_uuid(), 1, NULL, sha256('x'), %L)$q$, pg_temp.id('user')));

SELECT pg_temp.expect_error('no-op save (same content hash as previous)', '23514', format(
    $q$INSERT INTO cv.cv_versions (id, version_number, previous_version_id, content_hash, created_by)
       VALUES (gen_random_uuid(), 3, %L, %L, %L)$q$, pg_temp.id('v2'), pg_temp.h('v2'), pg_temp.id('user')));

SELECT pg_temp.expect_error('version with no nodes', '23514', format(
    $q$INSERT INTO cv.cv_versions (id, version_number, previous_version_id, content_hash, created_by)
       VALUES (gen_random_uuid(), 3, %L, sha256('x'), %L)$q$, pg_temp.id('v2'), pg_temp.id('user')));

-- Restore v1 as v3: copy v1 verbatim. node_ids are stable, so the tree survives.
INSERT INTO cv.cv_versions (id, version_number, previous_version_id, restored_from_version_id,
                            summary, content_hash, created_by)
VALUES (pg_temp.id('v3'), 3, pg_temp.id('v2'), pg_temp.id('v1'),
        'Restored from v1', pg_temp.h('v1'), pg_temp.id('user'));
INSERT INTO cv.cv_nodes (version_id, node_id, parent_node_id, type_code, sort_key, attrs)
SELECT pg_temp.id('v3'), node_id, parent_node_id, type_code, sort_key, attrs
FROM cv.cv_nodes WHERE version_id = pg_temp.id('v1');
INSERT INTO cv.cv_node_contents (version_id, node_id, locale, content, is_omitted, source_hash)
SELECT pg_temp.id('v3'), node_id, locale, content, is_omitted, source_hash
FROM cv.cv_node_contents WHERE version_id = pg_temp.id('v1');
SET CONSTRAINTS ALL IMMEDIATE;
SET CONSTRAINTS ALL DEFERRED;

SELECT pg_temp.expect('restored v3 is identical to v1',
    NOT EXISTS (SELECT 1 FROM cv.cv_version_diff(pg_temp.id('v1'), pg_temp.id('v3'))));
SELECT pg_temp.expect('latest version is v3',
    (SELECT id FROM cv.cv_latest_version) = pg_temp.id('v3'));

-- --- immutability --------------------------------------------------------------------

SELECT pg_temp.expect_error('update a node (even as owner)', '23001',
    $q$UPDATE cv.cv_nodes SET sort_key = 'zz'$q$);
SELECT pg_temp.expect_error('edit text in place', '23001',
    $q$UPDATE cv.cv_node_contents SET content = 'Hacked' WHERE content IS NOT NULL$q$);
SELECT pg_temp.expect_error('delete a version', '23001',
    $q$DELETE FROM cv.cv_versions$q$);
SELECT pg_temp.expect_error('rewrite publication history', '23001',
    $q$DELETE FROM cv.cv_publications$q$);
SELECT pg_temp.expect_error('truncate the audit log', '23001',
    $q$TRUNCATE cv.audit_log$q$);

-- --- unpublish, renders, users -----------------------------------------------------

INSERT INTO cv.cv_publications (locale, locale_publishable, version_id, published_by, note)
VALUES ('nb', true, NULL, pg_temp.id('user'), 'Taking Norwegian offline');
SELECT pg_temp.expect('unpublished nb disappears from cv_published',
    NOT EXISTS (SELECT 1 FROM cv.cv_published WHERE locale = 'nb'));
SELECT pg_temp.expect('publication history is kept',
    (SELECT count(*) FROM cv.cv_publications WHERE locale = 'nb') = 2);

SELECT pg_temp.expect_error('render for the language-neutral locale', '23514', format(
    $q$INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content)
       VALUES (%L, 'zxx', 'pdf', '1.0.0', sha256('pdf'), 'pdf')$q$, pg_temp.id('v1')));

SELECT pg_temp.expect_error('unknown render format', '23514', format(
    $q$INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content)
       VALUES (%L, 'en', 'docx', '1.0.0', sha256('docx'), 'docx')$q$, pg_temp.id('v1')));

SELECT pg_temp.expect_error('render whose hash does not match its content', '23514', format(
    $q$INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content)
       VALUES (%L, 'en', 'pdf', '1.0.0', sha256('other'), 'pdf')$q$, pg_temp.id('v1')));

SELECT pg_temp.expect_error('empty render', '23514', format(
    $q$INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content)
       VALUES (%L, 'en', 'pdf', '1.0.0', sha256(''), '')$q$, pg_temp.id('v1')));

-- --- public files (ADR 0003 §3) ------------------------------------------------------
-- Published so far, each with its files: en v1, fr v2 (current), nb v1 (since
-- unpublished). v3 is a draft. Files that were never published:
INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content)
VALUES (pg_temp.id('v1'), 'fr', 'pdf', 'r/1', sha256('v1 fr pdf'), 'v1 fr pdf'),
       (pg_temp.id('v3'), 'en', 'pdf', 'r/1', sha256('v3 en pdf'), 'v3 en pdf');
-- A re-render with a newer renderer. now() is fixed for the whole transaction, so
-- give it a later created_at by hand, as a later transaction would get.
INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content, created_at)
VALUES (pg_temp.id('v1'), 'en', 'pdf', 'r/2', sha256('v1 en pdf r/2'), 'v1 en pdf r/2', now() + interval '1 second');

SELECT pg_temp.expect('public: latest en serves the newest render of v1, with the name',
    (SELECT version_number = 1 AND content = 'v1 en pdf r/2'::bytea AND name = 'R. Alan Williams'
       FROM cv.cv_public_render('en', 'pdf')));
SELECT pg_temp.expect('public: latest fr serves v2',
    (SELECT content FROM cv.cv_public_render('fr', 'pdf')) = 'v2 fr pdf'::bytea);
SELECT pg_temp.expect('public: a version once published for fr is still served by number',
    (SELECT content FROM cv.cv_public_render('fr', 'pdf', 2)) = 'v2 fr pdf'::bytea);
SELECT pg_temp.expect('public: unpublished nb has no latest file',
    NOT EXISTS (SELECT 1 FROM cv.cv_public_render('nb', 'pdf')));
SELECT pg_temp.expect('public: but its permalink still works',
    (SELECT content FROM cv.cv_public_render('nb', 'pdf', 1)) = 'v1 nb pdf'::bytea);
SELECT pg_temp.expect('public: a draft is never served, even by number',
    NOT EXISTS (SELECT 1 FROM cv.cv_public_render('en', 'pdf', 3)));
SELECT pg_temp.expect('public: fr never published v1, so v1 is not served in fr',
    NOT EXISTS (SELECT 1 FROM cv.cv_public_render('fr', 'pdf', 1)));
SELECT pg_temp.expect('public: the other formats are served too',
    (SELECT content FROM cv.cv_public_render('en', 'md')) = 'v1 en md'::bytea
    AND (SELECT content FROM cv.cv_public_render('en', 'html')) = 'v1 en html'::bytea);

SELECT pg_temp.expect_error('email uniqueness ignores case', '23505',
    $q$INSERT INTO cv.users (id, email, display_name)
       VALUES (gen_random_uuid(), 'OWNER@example.com', 'Impostor')$q$);

SELECT pg_temp.expect_error('second source locale', '23505',
    $q$INSERT INTO cv.locales VALUES ('de', 'Deutsch', true, true)$q$);

-- --- privileges ------------------------------------------------------------------------
-- Temporarily let this session act as cv_app (rolled back at the end).

GRANT cv_app TO CURRENT_USER;
GRANT SELECT ON pg_temp.ids TO cv_app;
SET LOCAL ROLE cv_app;

SELECT pg_temp.expect('cv_app can read what is published',
    (SELECT count(*) FROM cv.cv_published) = 2);
SELECT pg_temp.expect('cv_app can call readiness',
    (SELECT count(*) FROM cv.cv_version_readiness(pg_temp.id('v1'))) = 3);
UPDATE cv.users SET display_name = 'R. Alan Williams' WHERE id = pg_temp.id('user');
SELECT pg_temp.expect('cv_app can rename the user', true);
INSERT INTO cv.audit_log (id, action, details) VALUES (gen_random_uuid(), 'DOWNLOAD', '{"locale":"en"}');
SELECT pg_temp.expect('cv_app can append to the audit log', true);

SELECT pg_temp.expect_error('cv_app cannot delete versions', '42501',
    $q$DELETE FROM cv.cv_versions$q$);
SELECT pg_temp.expect_error('cv_app cannot change reference data', '42501',
    $q$INSERT INTO cv.locales VALUES ('de', 'Deutsch', false, true)$q$);
SELECT pg_temp.expect_error('cv_app cannot change a user id', '42501',
    $q$UPDATE cv.users SET id = gen_random_uuid()$q$);

RESET ROLE;

-- cv_public may call cv_public_render and nothing else.
GRANT cv_public TO CURRENT_USER;
SET LOCAL ROLE cv_public;

SELECT pg_temp.expect('cv_public can fetch a published file',
    (SELECT content FROM cv.cv_public_render('en', 'pdf')) = 'v1 en pdf r/2'::bytea);
SELECT pg_temp.expect_error('cv_public cannot read stored files directly', '42501',
    $q$SELECT 1 FROM cv.cv_renders$q$);
SELECT pg_temp.expect_error('cv_public cannot read versions', '42501',
    $q$SELECT 1 FROM cv.cv_versions$q$);
SELECT pg_temp.expect_error('cv_public cannot read what is published', '42501',
    $q$SELECT 1 FROM cv.cv_published$q$);
SELECT pg_temp.expect_error('cv_public cannot call the other functions', '42501',
    $q$SELECT 1 FROM cv.cv_version_localized(gen_random_uuid())$q$);
SELECT pg_temp.expect_error('cv_public cannot write', '42501',
    $q$INSERT INTO cv.audit_log (id, action) VALUES (gen_random_uuid(), 'DOWNLOAD')$q$);

RESET ROLE;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        PERFORM pg_temp.expect('Supabase anon role has no access to the cv schema',
            NOT has_schema_privilege('anon', 'cv', 'USAGE'));
        PERFORM pg_temp.expect('Supabase authenticated role has no access to the cv schema',
            NOT has_schema_privilege('authenticated', 'cv', 'USAGE'));
    END IF;
END $$;

\o
\echo
\echo 'All schema tests passed. Rolling back.'
ROLLBACK;
