// Tests for choosing the landing page's seasonal theme (public/themes/themes.js).
//   node --test "tests/Site.Js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, existsSync } from 'node:fs';
import { THEMES, ALL_SECONDS, inRange, pickTheme, rotation, forVisit } from '../../public/themes/themes.js';

const themesDir = new URL('../../public/themes/', import.meta.url);

const on = (y, m, d) => new Date(y, m - 1, d, 12);

test('a yearly range includes both end days and repeats every year', () => {
    const range = { from: '03-22', to: '04-25' };

    assert.equal(inRange(range, on(2027, 3, 21)), false);
    assert.equal(inRange(range, on(2027, 3, 22)), true);
    assert.equal(inRange(range, on(2031, 4, 25)), true);
    assert.equal(inRange(range, on(2031, 4, 26)), false);
});

test('a yearly range can run over New Year', () => {
    const range = { from: '12-20', to: '01-06' };

    assert.equal(inRange(range, on(2026, 12, 19)), false);
    assert.equal(inRange(range, on(2026, 12, 31)), true);
    assert.equal(inRange(range, on(2027, 1, 6)), true);
    assert.equal(inRange(range, on(2027, 1, 7)), false);
});

test('a fixed range applies to its year only', () => {
    const range = { from: '2027-03-21', to: '2027-03-29' };

    assert.equal(inRange(range, on(2027, 3, 28)), true);
    assert.equal(inRange(range, on(2028, 3, 28)), false);
});

test('the day is read in local time, not UTC', () => {
    const range = { from: '04-01', to: '04-01' };

    assert.equal(inRange(range, new Date(2027, 3, 1, 0, 5)), true);
    assert.equal(inRange(range, new Date(2027, 3, 1, 23, 55)), true);
});

test('mixed date formats are rejected', () => {
    assert.throws(() => inRange({ from: '03-22', to: '2027-04-25' }, on(2027, 4, 1)), /must both be 'MM-DD' or both 'YYYY-MM-DD'/);
});

test('the first matching theme wins, and outside every range there is none', () => {
    const themes = [{ name: 'a', from: '04-01', to: '04-10' }, { name: 'b', from: '04-05', to: '04-20' }];

    assert.equal(pickTheme(themes, on(2027, 4, 7)).name, 'a');
    assert.equal(pickTheme(themes, on(2027, 4, 15)).name, 'b');
    assert.equal(pickTheme(themes, on(2027, 5, 1)), null);
});

test('?theme= previews a theme on any date, and an unknown name shows none', () => {
    const themes = [{ name: 'a', from: '04-01', to: '04-10' }];

    assert.equal(pickTheme(themes, on(2027, 8, 1), 'a').name, 'a');
    assert.equal(pickTheme(themes, on(2027, 4, 5), 'none'), null);
});

test('every configured theme has valid dates', () => {
    for (const theme of THEMES) assert.doesNotThrow(() => inRange(theme, on(2027, 1, 1)), theme.name);
});

test('every configured theme has its script and styles', () => {
    for (const { name } of THEMES) {
        for (const ext of ['js', 'css']) {
            assert.ok(existsSync(new URL(`${name}/${name}.${ext}`, themesDir)), `${name}/${name}.${ext} is missing`);
        }
    }
});

test('every theme folder is in the list, so none is left unused', () => {
    const folders = readdirSync(themesDir, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name);
    assert.deepEqual(folders.sort(), THEMES.map(t => t.name).sort());
});

test('each holiday in 2027 gets its theme', () => {
    const on2027 = (m, d) => pickTheme(THEMES, on(2027, m, d))?.name ?? null;

    assert.equal(on2027(1, 1), 'new-year');
    assert.equal(on2027(2, 2), 'groundhog');
    assert.equal(on2027(3, 17), 'st-patricks');
    assert.equal(on2027(3, 28), 'easter');          // Easter Sunday 2027
    assert.equal(on2027(5, 10), 'spring');
    assert.equal(on2027(10, 31), 'halloween');
    assert.equal(on2027(11, 25), 'thanksgiving');   // US Thanksgiving 2027
    assert.equal(on2027(12, 25), 'christmas');
    assert.equal(on2027(12, 31), 'new-year');
    assert.equal(on2027(8, 15), null);
});

test('?theme=all shows each theme in turn for ALL_SECONDS, then starts again', () => {
    const span = ALL_SECONDS * 1000, start = span * THEMES.length * 1000;   // the start of a round

    assert.equal(rotation(THEMES, start).index, 0);
    assert.equal(rotation(THEMES, start).left, span);
    assert.equal(rotation(THEMES, start + span - 1).theme, THEMES[0]);
    assert.equal(rotation(THEMES, start + span * 3 + 2500).theme, THEMES[3]);
    assert.equal(rotation(THEMES, start + span * 3 + 2500).left, span - 2500);
    assert.equal(rotation(THEMES, start + span * THEMES.length).index, 0);
});

test('a plain visit gets the theme for its date, and only ?theme=all rotates', () => {
    assert.equal(forVisit('', on(2027, 10, 9)).theme.name, 'halloween');
    assert.equal(forVisit('', on(2027, 8, 15)).theme, null);
    assert.equal(forVisit('?utm_source=x', on(2027, 12, 25)).theme.name, 'christmas');
    assert.equal(forVisit('?theme=easter', on(2027, 10, 9)).theme.name, 'easter');
    assert.equal(forVisit('?theme=none', on(2027, 10, 9)).theme, null);
    assert.deepEqual(forVisit('?theme=all', on(2027, 10, 9)), { rotate: true });
    assert.equal(forVisit('', on(2027, 10, 9)).rotate, undefined);
});
