// Seasonal themes for the landing page. Each theme lives in themes/<name>/ as <name>.css
// and <name>.js, and is shown automatically between its dates.
//
// Dates are inclusive, in the visitor's local time, written either way:
//   'MM-DD'       every year, e.g. { from: '03-22', to: '04-25' }
//   'YYYY-MM-DD'  that year only, e.g. { from: '2027-03-21', to: '2027-03-29' }
// A yearly range may run over New Year: { from: '12-20', to: '01-06' }.
// Where ranges overlap, the first theme listed wins.
//
// To preview a theme on any day, add ?theme=<name> to the URL; ?theme=none shows no theme.
// ?theme=all is for testing: it shows every theme in turn, ALL_SECONDS each, reloading the
// page between them.
export const THEMES = [
    { name: 'new-year', from: '12-31', to: '01-02' },
    { name: 'groundhog', from: '01-31', to: '02-03' },
    { name: 'st-patricks', from: '03-10', to: '03-18' },
    { name: 'easter', from: '03-22', to: '04-25' },     // every date Easter Sunday can fall on
    { name: 'spring', from: '03-19', to: '06-20' },     // listed after Easter, which wins where they overlap
    { name: 'halloween', from: '10-01', to: '10-31' },
    { name: 'thanksgiving', from: '11-01', to: '11-28' },   // US Thanksgiving is the 22nd to the 28th
    { name: 'christmas', from: '11-29', to: '12-30' },
];

// Testing only: a theme name (or 'all') shown on every visit, whatever the date. A ?theme=
// in the URL still beats it. Set back to null before release.
export const FORCE_THEME = 'all';
export const ALL_SECONDS = 10;

const YEARLY = /^\d{2}-\d{2}$/, FIXED = /^\d{4}-\d{2}-\d{2}$/;

// 'YYYY-MM-DD' for a date in local time, which compares correctly as a string
function isoDay(date) {
    const pad = n => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function inRange({ from, to }, date) {
    const day = isoDay(date);
    if (FIXED.test(from) && FIXED.test(to)) return from <= day && day <= to;
    if (YEARLY.test(from) && YEARLY.test(to)) {
        const md = day.slice(5);
        return from <= to ? from <= md && md <= to : md >= from || md <= to;
    }
    throw new Error(`Theme dates must both be 'MM-DD' or both 'YYYY-MM-DD': ${from} to ${to}`);
}

// The theme to show on a date, or null. An override name (from ?theme=) beats the dates.
export function pickTheme(themes, date, override = null) {
    if (override) return themes.find(t => t.name === override) ?? null;
    return themes.find(t => inRange(t, date)) ?? null;
}

// For ?theme=all: which theme is up at time ms, its place in the list, and the ms until
// the next. Each slot of ALL_SECONDS on the clock belongs to one theme, so a reload
// carries on where the last page left off without remembering anything.
export function rotation(themes, ms) {
    const span = ALL_SECONDS * 1000, slot = Math.floor(ms / span), index = slot % themes.length;
    return { theme: themes[index], index, left: (slot + 1) * span - ms };
}

function stylesheet(href) {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    document.head.appendChild(link);
    return new Promise(resolve => {
        link.addEventListener('load', resolve, { once: true });
        link.addEventListener('error', resolve, { once: true });
    });
}

// The shared kit's styles, then the theme's, then its script as a module (so it can
// import ../kit.js). The script runs once the styles are in, so nothing appears unstyled.
function load(theme) {
    const base = new URL(`${theme.name}/${theme.name}`, import.meta.url);
    Promise.all([stylesheet(new URL('kit.css', import.meta.url)), stylesheet(base + '.css')]).then(() => {
        const js = document.createElement('script');
        js.type = 'module';
        js.src = base + '.js';
        document.body.appendChild(js);
    });
}

// A tag in the corner saying which theme is up and how long it has left
function rotationTag(theme, index, left) {
    const tag = document.createElement('div');
    tag.setAttribute('role', 'status');
    tag.style.cssText = 'position: fixed; left: 10px; bottom: 10px; z-index: 100; padding: 4px 12px; border-radius: 12px;' +
        'background: rgba(20, 20, 30, 0.8); color: #fff; font: 0.85rem Oswald, "Arial Narrow", sans-serif; pointer-events: none;';
    const end = Date.now() + left;
    const show = () => {
        tag.textContent = `Theme test: ${theme.name} (${index + 1} of ${THEMES.length}), next in ${Math.max(0, Math.ceil((end - Date.now()) / 1000))}s`;
    };
    show();
    setInterval(show, 250);
    document.body.appendChild(tag);
}

if (typeof document !== 'undefined') {
    const override = new URLSearchParams(location.search).get('theme') ?? FORCE_THEME;
    if (override === 'all') {
        const { theme, index, left } = rotation(THEMES, Date.now());
        load(theme);
        rotationTag(theme, index, left);
        setTimeout(() => location.reload(), left + 50);
    } else {
        const theme = pickTheme(THEMES, new Date(), override);
        if (theme) load(theme);
    }
}
