// Tests for the landing page's accessibility settings (public/a11y.js): where each
// setting comes from, saving a choice, and what changing one does. The script runs in a
// small stand-in for the browser; the dialog itself is checked in a real browser.
//   node --test "tests/Site.Js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../../public/a11y.js', import.meta.url), 'utf8');

// Runs a11y.js as a page at url would, with the OS preferences that match in os, what
// the browser has saved, and storage that can be blocked
function page({ url = 'https://example.com/', os = [], saved = null, blocked = false } = {}) {
    const store = new Map(saved ? [['site-settings', JSON.stringify(saved)]] : []);
    const events = [];
    const env = {
        location: { href: url, search: new URL(url).search, hash: '', reloads: 0, reload() { this.reloads++; } },
        localStorage: {
            getItem: key => { if (blocked) throw new Error('blocked'); return store.get(key) ?? null; },
            setItem: (key, value) => { if (blocked) throw new Error('blocked'); store.set(key, value); },
            removeItem: key => { if (blocked) throw new Error('blocked'); store.delete(key); },
        },
        matchMedia: query => ({ matches: os.includes(query), addEventListener() {} }),
        document: {
            readyState: 'loading',                // so the dialog isn't built
            documentElement: { dataset: {} },
            addEventListener() {},
            dispatchEvent: e => events.push({ ...e.detail }),   // copied out of the script's realm
        },
        URL, URLSearchParams, JSON,
        CustomEvent: class { constructor(type, { detail }) { this.type = type; this.detail = detail; } },
    };
    env.history = { replaceState: (_, __, to) => { env.location.href = String(to); env.location.search = new URL(to).search; } };
    env.window = env;
    runInNewContext(source, env);
    return {
        settings: { ...env.document.documentElement.dataset },
        api: env.siteSettings,
        html: env.document.documentElement.dataset,
        saved: () => JSON.parse(store.get('site-settings') ?? 'null'),
        url: () => env.location.href,
        hash: () => env.location.hash,
        reloads: () => env.location.reloads,
        events,
    };
}

const DEFAULTS = { contrast: 'normal', motion: 'full', decor: 'on', list: 'off', newtab: 'on' };

test('with nothing chosen, the defaults apply', () => {
    assert.deepEqual(page().settings, DEFAULTS);
});

test('contrast and motion follow the OS until the visitor chooses', () => {
    const os = ['(prefers-contrast: more)', '(prefers-reduced-motion: reduce)'];

    assert.deepEqual(page({ os }).settings, { ...DEFAULTS, contrast: 'high', motion: 'reduce' });
});

test('a saved choice beats the OS, and the URL beats both', () => {
    const os = ['(prefers-reduced-motion: reduce)'];

    assert.equal(page({ os, saved: { motion: 'full' } }).settings.motion, 'full');
    assert.equal(page({ os, saved: { motion: 'full' }, url: 'https://example.com/?motion=reduce' }).settings.motion, 'reduce');
});

test('values that aren\'t allowed are ignored', () => {
    const p = page({ url: 'https://example.com/?contrast=max&decor=1', saved: { list: 'yes', newtab: 'off' } });

    assert.deepEqual(p.settings, { ...DEFAULTS, newtab: 'off' });
});

test('a choice applies at once, is saved, and is announced to the page', () => {
    const p = page();

    assert.equal(p.api.set('contrast', 'high'), false);
    assert.equal(p.html.contrast, 'high');
    assert.deepEqual(p.saved(), { contrast: 'high' });
    assert.deepEqual(p.events, [{ key: 'contrast', value: 'high' }]);
    assert.equal(p.reloads(), 0);
});

test('motion and decorations reload the page, coming back to the same switch', () => {
    const p = page();

    assert.equal(p.api.set('decor', 'off'), true);
    assert.equal(p.reloads(), 1);
    assert.equal(p.hash(), 'settings-decor');
    assert.deepEqual(p.saved(), { decor: 'off' });
});

test('choosing the value already in effect does nothing', () => {
    const p = page();

    p.api.set('motion', 'full');
    assert.equal(p.reloads(), 0);
    assert.deepEqual(p.events, []);
});

test('a choice updates a setting in the URL, so the old value can\'t override it', () => {
    const p = page({ url: 'https://example.com/?theme=none&contrast=high' });

    p.api.set('contrast', 'normal');
    assert.equal(new URL(p.url()).searchParams.get('contrast'), 'normal');
    assert.equal(new URL(p.url()).searchParams.get('theme'), 'none');
    assert.equal(new URL(p.url()).searchParams.has('list'), false);
});

test('where storage is blocked, settings travel in the URL', () => {
    const p = page({ blocked: true });

    p.api.set('list', 'on');
    const params = new URL(p.url()).searchParams;
    assert.equal(params.get('list'), 'on');
    assert.equal(params.get('contrast'), 'normal');
    assert.equal(page({ blocked: true, url: p.url() }).settings.list, 'on');
});

test('the kit and every theme take reduced motion from the setting, not the media query alone', () => {
    const themes = new URL('../../public/themes/', import.meta.url);
    const files = ['kit.js', 'kit.css', ...readdirSync(themes, { withFileTypes: true })
        .filter(d => d.isDirectory())
        .flatMap(d => [`${d.name}/${d.name}.js`, `${d.name}/${d.name}.css`])];

    for (const file of files) {
        assert.doesNotMatch(readFileSync(new URL(file, themes), 'utf8'), /prefers-reduced-motion/, file);
    }
    assert.match(readFileSync(new URL('kit.js', themes), 'utf8'), /calm = document\.documentElement\.dataset\.motion === 'reduce'/);
});
