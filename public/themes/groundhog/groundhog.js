// Groundhog Day, seen from the crowd at Gobbler's Knob. The logo box is the sign over a
// wooden stage with snowy pines behind it, the Inner Circle stand along the stage in top
// hats, and the backs of the crowd's heads fill the bottom of the window, phones up and
// cameras flashing. Snow falls in the dark before dawn, and the sun breaks through low on
// the horizon now and then. Every so often (or when his stump is clicked) Phil pops up out
// of his tree stump and his handler holds him up for his forecast: if the sun is out he
// sees his shadow and it's six more weeks of winter, to boos and heavier snow; if not,
// it's an early spring, to cheers. A tally under the sign keeps score.
// Loaded by themes/themes.js, which also loads groundhog.css.
import {
    calm, rand, pick, chance, wait, every, tween, says, make, logoBox,
    sky, clouds, ground, snowfall, backdrop, costume, tools
} from '../kit.js';

const FORECAST_EVERY = [12000, 20000];        // ms between forecasts
const SUN_EVERY = [6000, 14000];              // ms between the sun breaking through or going in
const FLASH_EVERY = [500, 2500];              // ms between camera flashes in the crowd
const BOOS = ['Booo!', 'Noooo!', 'Not again, Phil!'];
const CHEERS = ['Hooray!', 'Yay, Phil!', 'Spring!'];

// The digger joins the Inner Circle: a top hat and a bow tie
costume({
    tool: tools.iceChopper(),
    hat: `<ellipse cx="2" cy="-14" rx="23" ry="4" fill="#111" transform="rotate(6 2 -14)"/>
        <path d="M-11 -15L-10 -46H13L14 -13Z" fill="#161616"/>
        <path d="M-10.6 -21L13.6 -19.6L13.8 -15L-10.8 -16.4Z" fill="#7a0d0d"/>`,
    over: '<path d="M136 73L130 69V77ZM140 73L146 69V77Z" fill="#7a0d0d"/><circle cx="138" cy="73" r="2" fill="#7a0d0d"/>',
    label: 'Stick figure in a top hat and bow tie breaking the ice with an ice chopper'
});

// Dark before dawn; the sun breaking through warms the horizon
const dawn = sky('dawn-sky');
clouds(dawn, {
    count: 5, top: [0, 30],
    light: [[96, 110, 140], [80, 92, 120]], dark: [[64, 74, 100], [52, 60, 84]]
});
let sunny = false;
function setSun(on) {
    sunny = on;
    dawn.classList.toggle('sunny', on);
}

// Snow on the knob, with the stage drawn on its skyline
const knob = ground('knob-snow', '<svg class="stage" aria-hidden="true"></svg>');
const stage = knob.el.querySelector('.stage');
const snow = snowfall(knob, { area: 5000 });

// The crowd across the bottom of the window, in front of the snow but behind the page
const crowd = document.createElement('div');
crowd.className = 'crowd';
crowd.innerHTML = '<svg></svg>';
backdrop(crowd, 'weather');
let phones = [];

// The sign: a red header over the logo box, and the score underneath
logoBox.insertAdjacentHTML('afterbegin', '<div class="knob-sign" aria-hidden="true">Gobbler’s Knob</div>');
logoBox.insertAdjacentHTML('beforeend', '<span class="tally" role="status"></span>');
const tally = logoBox.querySelector('.tally');
const score = { winter: 0, spring: 0 };
const showScore = () => { tally.textContent = `Six more weeks: ${score.winter} · Early spring: ${score.spring}`; };
showScore();

// A member of the Inner Circle, drawn in a 60 x 120 box with his feet at the bottom
const COATS = ['#16161b', '#1d1c24', '#121218', '#24202a'];
function member({ coat = pick(COATS), beard = chance(0.4), scarf = chance(0.25), skin = pick(['#f1c6a5', '#e0ac8a', '#c68a65']) } = {}) {
    return `
        <path d="M23 100L22 118H28L29 100M31 100L32 118H38L37 100" fill="#0d0d0d"/>
        <path d="M14 44C11 70 10 96 12 104H48C50 96 49 70 46 44C40 40 20 40 14 44Z" fill="${coat}"/>
        <path d="M25 43L35 43L30 60Z" fill="#f4f1ea"/>
        <path d="M28.5 46L31.5 46L30.8 56L29.2 56Z" fill="#8b1a1a"/>
        ${scarf ? '<path d="M20 42Q30 50 40 42L41 47Q30 55 19 47Z" fill="#8b1a1a"/>' : ''}
        <path d="M15 48L10 84M45 48L50 84" stroke="${coat}" stroke-width="7" stroke-linecap="round"/>
        <circle cx="30" cy="32" r="9" fill="${skin}"/>
        ${beard ? '<path d="M21 32C21 46 39 46 39 32C35 37 25 37 21 32Z" fill="#d9d4cc"/>' : ''}
        <g class="hat">
            <rect x="13" y="19" width="34" height="3.5" rx="1.5" fill="#0d0d0d"/>
            <rect x="18" y="1" width="24" height="20" fill="#121212"/>
            <rect x="18" y="16" width="24" height="3" fill="#2b2b2b"/>
        </g>`;
}

// Phil, standing in the mouth of his stump: centred at x 30, like a member in his box
const PHIL = `
    <ellipse cx="30" cy="66" rx="10" ry="13" fill="#8a6240"/>
    <ellipse cx="30" cy="69" rx="6" ry="8.5" fill="#c49a6c"/>
    <circle cx="24" cy="44" r="2.6" fill="#7a5434"/><circle cx="36" cy="44" r="2.6" fill="#7a5434"/>
    <circle cx="30" cy="51" r="8" fill="#8a6240"/>
    <ellipse cx="30" cy="54" rx="4.4" ry="3.2" fill="#c49a6c"/>
    <circle cx="27" cy="49.5" r="1.2" fill="#111"/><circle cx="33" cy="49.5" r="1.2" fill="#111"/>
    <ellipse cx="30" cy="52.6" rx="1.4" ry="1" fill="#2a1a10"/>
    <rect x="29" y="55" width="2" height="2.4" fill="#ffffff"/>`;

// Phil's tree stump and his handler, at the front of the stage beside the digger. Phil
// hides inside the stump: only what rises above its rim shows. Click for a forecast.
const handler = make(`
    <button class="handler" type="button" aria-label="Phil's tree stump. Knock for his forecast.">
        <svg viewBox="0 0 124 120" aria-hidden="true">
            <defs><clipPath id="stump-mouth"><rect x="-40" y="-200" width="220" height="280"/></clipPath></defs>
            <g class="phil-shadow" transform="translate(114 -62) scale(1.9)" fill="rgba(8, 10, 20, 0.5)">
                <ellipse cx="0" cy="22" rx="10" ry="13"/><circle cx="0" cy="7" r="8"/>
                <circle cx="-6" cy="0" r="2.6"/><circle cx="6" cy="0" r="2.6"/>
            </g>
            <g transform="translate(62 0)">
                ${member({ beard: false, scarf: true, coat: '#16161b', skin: '#f1c6a5' })}
                <g class="arms-down" stroke="#16161b" stroke-width="7" stroke-linecap="round" fill="none">
                    <path d="M15 50L12 82M45 50L48 82"/>
                </g>
                <g class="arms-up" stroke="#16161b" stroke-width="7" stroke-linecap="round" fill="none">
                    <path d="M15 48L20 22M45 48L40 22"/>
                </g>
            </g>
            <ellipse cx="30" cy="80" rx="22" ry="6" fill="#2a1a10"/>
            <path d="M8 80A22 6 0 0 1 52 80" fill="none" stroke="#c9a46c" stroke-width="3"/>
            <path d="M10 77Q18 70 27 73Q38 69 50 77Q40 74 30 75Q20 74 10 77Z" fill="#f4f8ff"/>
            <g clip-path="url(#stump-mouth)"><g class="phil">${PHIL}</g></g>
            <path d="M8 80A22 6 0 0 0 52 80V111Q53 116 61 119H-1Q7 116 8 111Z" fill="#6b4a2e"/>
            <path d="M14 88V112M22 90V115M33 90V116M42 88V113" stroke="#4e3420" stroke-width="1.5"/>
            <path d="M8 80A22 6 0 0 0 52 80" fill="none" stroke="#c9a46c" stroke-width="3"/>
            <path d="M-4 120Q8 112 18 117Q30 111 42 117Q54 112 64 120Z" fill="#f4f8ff"/>
        </svg>
    </button>`);
knob.pin(handler, -122);

// The stage, drawn to fit around wherever the logo box and the ground are
function buildStage() {
    const width = document.documentElement.clientWidth, box = logoBox.getBoundingClientRect();
    const boxTop = box.top + scrollY;
    if (!Number.isFinite(knob.top)) {                          // the ground isn't measured yet
        rebuild();
        return;
    }
    const height = Math.max(140, Math.round(knob.top - boxTop + 60));
    const top = height - (knob.top - boxTop);                 // the box's top, in the stage
    const left = box.left - 14, right = box.right + 14, cx = (box.left + box.right) / 2;
    const wing = Math.min(240, Math.max(60, box.left - 10)), deck = height - 10;
    stage.style.height = height + 'px';
    stage.setAttribute('viewBox', `0 0 ${width} ${height}`);

    let forest = '';
    for (let x = -20; x < width + 20; x += rand(22, 40)) {
        const h = rand(70, 180), w = h * 0.36;
        forest += `<path d="M${x.toFixed(0)} ${(deck - h).toFixed(0)}L${(x - w).toFixed(0)} ${deck}H${(x + w).toFixed(0)}Z" fill="${pick(['#1b2740', '#202e4a', '#17223a'])}"/>
            <path d="M${x.toFixed(0)} ${(deck - h).toFixed(0)}L${(x - w * 0.35).toFixed(0)} ${(deck - h * 0.62).toFixed(0)}M${x.toFixed(0)} ${(deck - h * 0.55).toFixed(0)}L${(x + w * 0.5).toFixed(0)} ${(deck - h * 0.2).toFixed(0)}"
                stroke="#e8eef7" stroke-width="2" stroke-opacity="0.7"/>`;
    }

    // A plain backdrop of upright planks behind the sign, flat along the top
    const wallLeft = left - wing, wallRight = right + wing, wallTop = top + 40;

    let members = '';
    const keepClear = [cx - 120, cx + 190];                    // the digger, and the stump and handler beside him
    for (let x = left - wing + 6; x < right + wing - 40; x += rand(30, 44)) {
        if (x + 40 > keepClear[0] && x < keepClear[1]) continue;
        const s = rand(0.66, 0.8);
        members += `<g class="member" style="--d: ${-rand(0, 0.4).toFixed(2)}s" transform="translate(${x.toFixed(0)} ${(deck - 120 * s).toFixed(0)}) scale(${s.toFixed(2)})">${member()}</g>`;
    }

    stage.innerHTML = `
        <defs>
            <pattern id="planks" width="44" height="160" patternUnits="userSpaceOnUse">
                <rect width="22" height="160" fill="#8d603c"/>
                <rect x="22" width="22" height="160" fill="#835735"/>
                <path d="M0 0V160M22 0V160" stroke="#3a2616" stroke-width="1.5"/>
                <path d="M8 10V70M15 40V120M30 0V50M36 70V150" stroke="#6f4a2c" stroke-width="0.8" stroke-opacity="0.6"/>
                <ellipse cx="12" cy="96" rx="2" ry="3" fill="#5e3d22"/>
            </pattern>
        </defs>
        ${forest}
        <rect x="${wallLeft}" y="${wallTop}" width="${wallRight - wallLeft}" height="${deck - wallTop}" fill="url(#planks)"/>
        <rect x="${wallLeft - 4}" y="${wallTop - 6}" width="${wallRight - wallLeft + 8}" height="8" fill="#5e3d22"/>
        <path d="M${wallLeft - 4} ${wallTop - 6}H${wallRight + 4}" stroke="#f4f8ff" stroke-width="3"/>
        <rect x="${wallLeft - 4}" y="${wallTop}" width="8" height="${deck - wallTop}" fill="#5e3d22"/>
        <rect x="${wallRight - 4}" y="${wallTop}" width="8" height="${deck - wallTop}" fill="#5e3d22"/>
        ${members}
        <rect x="${wallLeft - 20}" y="${deck}" width="${wallRight - wallLeft + 40}" height="10" fill="#4a3424"/>
        <path d="M${wallLeft - 20} ${deck}H${wallRight + 20}" stroke="#f4f8ff" stroke-width="2"/>`;
    handler.style.left = cx + 60 + 'px';
}

// The backs of the crowd: two rows of heads and shoulders in hats, hoods and beanies,
// some holding phones up
const COATS_CROWD = ['#1c1f2b', '#2a2233', '#22303a', '#3a2a24', '#1b2a22', '#2e2e3a'];
const BEANIES = ['#b03a2e', '#2a6f97', '#e9c46a', '#6a4c93', '#2a9d8f', '#d62828', '#f4a261'];
function fan(x, base, s) {
    const coat = pick(COATS_CROWD), hair = pick(['#2a1d14', '#3b2a1a', '#1a1a1a', '#5a4632', '#a07a4a']);
    const head = base - 38 * s, r = 11 * s;
    const wear = pick(['beanie', 'beanie', 'beanie', 'hood', 'top', 'groundhog', 'cap', 'none']);
    let hat = '';
    if (wear === 'beanie') {
        const c = pick(BEANIES);
        hat = `<path d="M${x - r * 1.05} ${head - r * 0.1}C${x - r} ${head - r * 1.6} ${x + r} ${head - r * 1.6} ${x + r * 1.05} ${head - r * 0.1}Z" fill="${c}"/>
            <circle cx="${x}" cy="${head - r * 1.25}" r="${r * 0.38}" fill="#f4f1ea"/>`;
    } else if (wear === 'hood') {
        hat = `<path d="M${x - r * 1.35} ${head + r}C${x - r * 1.5} ${head - r * 1.6} ${x + r * 1.5} ${head - r * 1.6} ${x + r * 1.35} ${head + r}Z" fill="${coat}"/>`;
    } else if (wear === 'top') {
        hat = `<rect x="${x - r * 1.4}" y="${head - r * 0.7}" width="${r * 2.8}" height="${r * 0.3}" fill="#0d0d0d"/>
            <rect x="${x - r * 0.9}" y="${head - r * 2.2}" width="${r * 1.8}" height="${r * 1.6}" fill="#121212"/>`;
    } else if (wear === 'groundhog') {
        hat = `<path d="M${x - r * 1.1} ${head}C${x - r} ${head - r * 1.7} ${x + r} ${head - r * 1.7} ${x + r * 1.1} ${head}Z" fill="#8a6240"/>
            <circle cx="${x - r * 0.7}" cy="${head - r * 1.1}" r="${r * 0.3}" fill="#7a5434"/><circle cx="${x + r * 0.7}" cy="${head - r * 1.1}" r="${r * 0.3}" fill="#7a5434"/>`;
    } else if (wear === 'cap') {
        hat = `<path d="M${x - r * 1.02} ${head - r * 0.15}C${x - r} ${head - r * 1.4} ${x + r} ${head - r * 1.4} ${x + r * 1.02} ${head - r * 0.15}Z" fill="${pick(BEANIES)}"/>`;
    }
    let phone = '';
    if (chance(0.18)) {
        const px = x + 14 * s, py = base - 86 * s;
        phone = `<path d="M${x + 12 * s} ${base - 26 * s}L${px} ${py + 12 * s}" stroke="${coat}" stroke-width="${6 * s}" stroke-linecap="round"/>
            <rect x="${px - 5 * s}" y="${py}" width="${10 * s}" height="${16 * s}" rx="${1.5 * s}" fill="#0d0d0d"/>
            <rect x="${px - 4 * s}" y="${py + 1.5 * s}" width="${8 * s}" height="${12 * s}" fill="#bcd8ff"/>`;
        phones.push({ x: px, y: py + 8 * s });
    }
    return `<g class="fan" style="--d: ${-rand(0, 0.35).toFixed(2)}s">
        <path d="M${x - 24 * s} ${base}C${x - 24 * s} ${base - 32 * s} ${x + 24 * s} ${base - 32 * s} ${x + 24 * s} ${base}Z" fill="${coat}"/>
        ${phone}
        <circle cx="${x}" cy="${head}" r="${r}" fill="${hair}"/>
        ${hat}
    </g>`;
}

function buildCrowd() {
    const width = innerWidth, height = 190;
    phones = [];
    let back = '', front = '';
    for (let x = rand(-10, 10); x < width + 40; x += rand(40, 52)) back += fan(x, height - 18, rand(1.25, 1.45));
    for (let x = rand(5, 30); x < width + 60; x += rand(60, 76)) front += fan(x, height + 30, rand(1.8, 2.1));
    const svg = crowd.querySelector('svg');
    svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
    svg.innerHTML = back + front;
}

// Redraws the stage and crowd at most every 100ms, after the ground has measured itself again
let queued = false;
function rebuild() {
    if (queued) return;
    queued = true;
    setTimeout(() => requestAnimationFrame(() => {
        queued = false;
        buildCrowd();
        buildStage();
    }), 100);
}
buildCrowd();
buildStage();
document.fonts.ready.then(rebuild);
addEventListener('resize', rebuild);
const site = document.querySelector('.dig-site');
if (site) new ResizeObserver(rebuild).observe(site);

// A camera flash from a phone in the crowd
function flash() {
    if (calm || !phones.length) return;
    const { x, y } = pick(phones);
    const burst = make(`<span class="flash" style="left: ${x.toFixed(0)}px; top: ${y.toFixed(0)}px"></span>`);
    crowd.appendChild(burst);
    burst.animate([{ opacity: 0, scale: 0.3 }, { opacity: 1, scale: 1, offset: 0.2 }, { opacity: 0, scale: 1.3 }], 320)
        .finished.then(() => burst.remove());
}

// Somebody in the crowd shouts
function shout(text) {
    const voice = make(`<span class="voice" style="left: ${rand(10, 90).toFixed(0)}%"></span>`);
    crowd.appendChild(voice);
    says(voice, text, { duration: 2200 });
    wait(2300).then(() => voice.remove());
}

// The forecast: Phil comes up out of his stump, is held up, and either sees his shadow
// or doesn't
let busy = false;
async function forecast() {
    if (busy) return;
    busy = true;
    handler.classList.add('out');
    for (let i = 0; i < 4; i++) wait(rand(0, 1000)).then(flash);
    await wait(1200);
    handler.classList.add('raise');
    for (let i = 0; i < 6; i++) wait(rand(0, 1200)).then(flash);
    await wait(1300);
    if (sunny) {
        handler.classList.add('shadow');
        says(handler, 'Phil sees his shadow! Six more weeks of winter!', { duration: 3800 });
        crowd.classList.add('boo');
        shout(pick(BOOS));
        score.winter++;
        tween(1, 2.8, 1500, v => { snow.density = v; }).then(() => wait(6000)).then(() => tween(2.8, 1, 3000, v => { snow.density = v; }));
    } else {
        says(handler, 'No shadow! An early spring!', { duration: 3800 });
        crowd.classList.add('cheer');
        stage.classList.add('cheer');
        shout(pick(CHEERS));
        for (let i = 0; i < 10; i++) wait(rand(0, 2500)).then(flash);
        score.spring++;
        tween(1, 0.3, 1500, v => { snow.density = v; }).then(() => wait(6000)).then(() => tween(0.3, 1, 3000, v => { snow.density = v; }));
    }
    showScore();
    await wait(3800);
    handler.classList.remove('raise', 'shadow');
    crowd.classList.remove('boo', 'cheer');
    stage.classList.remove('cheer');
    await wait(900);
    handler.classList.remove('out');                          // back down into the stump
    await wait(700);
    busy = false;
}
handler.addEventListener('click', forecast);

if (!calm) {
    every(SUN_EVERY, () => setSun(!sunny), rand(2000, 5000));
    every(FORECAST_EVERY, forecast, rand(3000, 5000));
    every(FLASH_EVERY, flash);
}
