// Thanksgiving: a sunset over a harvest field, autumn leaves blowing down in gusts, geese
// flying over in a V, and a turkey strutting along the logo box, which panics when clicked.
// Loaded by themes/themes.js, which also loads thanksgiving.css.
import {
    calm, rand, pick, chance, wait, every, tween, napper, says, make, logoBox,
    sky, clouds, ground, weather, fly, percher, costume, tools
} from '../kit.js';

// A pilgrim hat and collar
costume({
    tool: tools.spade(),
    hat: `<ellipse cx="2" cy="-14" rx="24" ry="4.5" fill="#1b1b1b" transform="rotate(6 2 -14)"/>
        <path d="M-11 -15L-8 -42H13L15 -12Z" fill="#262626"/>
        <path d="M-10.5 -21L14.4 -19.5L14.8 -14.5L-11 -16Z" fill="#5a3a1e"/>
        <rect x="-1" y="-23" width="8" height="7" fill="none" stroke="#d4af37" stroke-width="1.8"/>`,
    over: '<path d="M130 72L146 70L152 82L141 78L133 84Z" fill="#f4f1e8" stroke="#c9c4b8" stroke-width="0.6"/>',
    label: 'Stick figure in a pilgrim hat digging with a spade'
});

const TURKEY_SAYS = ['Gobble gobble!', 'I am NOT on the menu.', 'Happy Thanksgiving!', 'Pardon me?', 'Have you tried tofurkey?'];
const GEESE_EVERY = [12000, 24000];           // ms between skeins of geese
const GUST_EVERY = [7000, 15000];             // ms between gusts of wind in the leaves
const LEAF_AREA = 9000;                       // px² of window per leaf
const LEAF_COLOURS = ['#c0392b', '#e67e22', '#f1c40f', '#d35400', '#a04000', '#b7950b', '#922b21'];

// Sunset: a low sun and warm clouds
const dusk = sky('harvest-sky');
dusk.insertAdjacentHTML('beforeend', '<div class="sun"></div>');
clouds(dusk, {
    count: 6, top: [3, 40],
    light: [[255, 196, 150], [240, 160, 130]], dark: [[196, 112, 122], [150, 82, 112]]
});

// The field, with hay, corn shocks, pumpkins and a scarecrow on the skyline
const pumpkin = (x, y, r) => `
    <ellipse cx="${x}" cy="${y}" rx="${r * 1.25}" ry="${r}" fill="#e8761c"/>
    <path d="M${x} ${y - r}C${x - r * 0.5} ${y - r * 0.4} ${x - r * 0.5} ${y + r * 0.4} ${x} ${y + r}M${x} ${y - r}C${x + r * 0.5} ${y - r * 0.4} ${x + r * 0.5} ${y + r * 0.4} ${x} ${y + r}" fill="none" stroke="#b9530f"/>
    <path d="M${x} ${y - r}l1 -${r * 0.5}" stroke="#4f6b2a" stroke-width="2"/>`;
const shock = x => `
    <path d="M${x} 6L${x - 18} 60H${x + 18}Z" fill="#c9a24a"/>
    <path d="M${x} 6L${x - 9} 60M${x} 6L${x} 60M${x} 6L${x + 9} 60M${x} 6L${x - 15} 60M${x} 6L${x + 15} 60" stroke="#9c7a2c" stroke-width="1"/>
    <path d="M${x - 7} 26H${x + 7}" stroke="#6b4423" stroke-width="2.5"/>`;
const field = ground('harvest-field', `
    <svg class="farm" viewBox="0 0 120 60" style="left: 3%">
        <rect x="4" y="22" width="70" height="38" rx="9" fill="#d9b24c"/>
        <path d="M10 30H68M8 38H70M8 46H70M10 54H68" stroke="#b8902e" stroke-width="1.5"/>
        <path d="M12 22V60M66 22V60" stroke="#8a6a1e" stroke-width="2"/>
        ${pumpkin(92, 50, 10)}${pumpkin(108, 54, 6)}
    </svg>
    <svg class="farm" viewBox="0 0 90 60" style="left: 12%">${shock(24)}${shock(64)}</svg>
    <svg class="farm scarecrow" viewBox="0 0 80 100" style="right: 6%">
        <path d="M40 30V100M14 44H66" stroke="#6b4423" stroke-width="3.5"/>
        <path d="M24 40H56L60 72H20Z" fill="#b03a2e"/>
        <path d="M24 48H56M24 56H58M22 64H59M32 40V72M44 40V72" stroke="#7d2a21" stroke-width="1.2"/>
        <path d="M14 44L4 50M14 44L6 40M66 44L76 50M66 44L74 40" stroke="#d9b24c" stroke-width="2"/>
        <path d="M30 72L26 84M36 72L35 86M44 72L45 85M50 72L54 83" stroke="#d9b24c" stroke-width="2"/>
        <circle cx="40" cy="26" r="11" fill="#e9d3a4"/>
        <path d="M35 24L37 26M37 24L35 26M43 24L45 26M45 24L43 26" stroke="#3a2a1a" stroke-width="1.3"/>
        <path d="M34 31C37 34 43 34 46 31" fill="none" stroke="#3a2a1a" stroke-width="1.2"/>
        <path d="M24 17H56L52 13H28Z" fill="#5a3a1e"/>
        <path d="M30 13L33 1H47L50 13Z" fill="#6b4423"/>
        <path d="M31 9H49" stroke="#b03a2e" stroke-width="2"/>
    </svg>
    <svg class="farm" viewBox="0 0 70 40" style="right: 14%">${pumpkin(20, 28, 11)}${pumpkin(44, 30, 9)}${pumpkin(60, 33, 6)}</svg>`);

// Autumn leaves: maple leaves tumbling and swaying down, resting a while where they land
const LEAF = new Path2D('M0 -11L2.2 -5.5L6.5 -7.5L5.2 -2.2L10.5 -2.4L7.2 1.6L9.4 5.2L3.2 4.2L1 9.6L0 6L-1 9.6L-3.2 4.2L-9.4 5.2L-7.2 1.6L-10.5 -2.4L-5.2 -2.2L-6.5 -7.5L-2.2 -5.5Z');
function drawLeaf(pen, x, y, rot, size, squash, colour, alpha = 1) {
    pen.save();
    pen.globalAlpha = alpha;
    pen.translate(x, y);
    pen.rotate(rot);
    pen.scale(size, size * squash);
    pen.fillStyle = colour;
    pen.fill(LEAF);
    pen.strokeStyle = 'rgba(60, 25, 10, 0.5)';
    pen.lineWidth = 0.8;
    pen.beginPath();
    pen.moveTo(0, 6);
    pen.lineTo(0, 14);
    pen.stroke();
    pen.restore();
}

const leaves = weather({
    ground: field, area: LEAF_AREA, max: 160,
    spawn: anywhere => ({
        x: rand(-120, innerWidth), y: anywhere ? rand(-innerHeight, innerHeight) : rand(-60, -20),
        size: rand(0.6, 1.3), rot: rand(0, 6.3), spin: rand(-2.5, 2.5), vy: rand(35, 70),
        sway: rand(20, 60), phase: rand(0, 6.3), tumble: rand(0, 6.3), colour: pick(LEAF_COLOURS), t: 0
    }),
    step(f, dt, w) {
        f.t += dt;
        f.y += f.vy * dt;
        f.x += (Math.sin(f.t * 1.4 + f.phase) * f.sway + w.wind) * dt;
        f.rot += f.spin * dt * (1 + Math.abs(w.wind) / 80);
        f.tumble += dt * 3;
    },
    draw: (pen, f) => drawLeaf(pen, f.x, f.y, f.rot, f.size, Math.cos(f.tumble), f.colour),
    splash: f => ({
        life: 7,
        draw: (pen, k) => drawLeaf(pen, f.x, f.land, f.rot, f.size, 0.45, f.colour, k < 0.8 ? 1 : (1 - k) / 0.2)
    })
});

// Gusts: the wind picks up, more leaves blow down, then it dies away
async function gust() {
    const wind = rand(90, 200) * (chance(0.5) ? -1 : 1);
    await tween(0, 1, 1500, k => { leaves.wind = wind * k; leaves.density = 1 + k * 0.8; });
    await wait(rand(1500, 3500));
    await tween(1, 0, 3000, k => { leaves.wind = wind * k; leaves.density = 1 + k * 0.8; });
}

// Geese in a V: the leader in front, the rest trailing behind on either side
const GOOSE = `
    <svg viewBox="0 0 60 30">
        <path d="M14 15L2 12L6 16L2 20Z" fill="#3a2f26"/>
        <ellipse cx="26" cy="16" rx="13" ry="5" fill="#6b5a48"/>
        <path d="M18 18C22 21 32 21 36 18" fill="#d8cfc0"/>
        <path d="M36 14L50 11" stroke="#1d1d1d" stroke-width="3" stroke-linecap="round"/>
        <ellipse cx="52" cy="10.5" rx="3.6" ry="2.6" fill="#1d1d1d"/>
        <path d="M50 12L53 13" stroke="#f4f4f4" stroke-width="1.6"/>
        <path d="M55 10L59.5 11L55 12Z" fill="#2a2a2a"/>
        <path class="wing" d="M20 14L8 0L32 13Z" fill="#574838"/>
    </svg>`;

function skein() {
    const count = pick([5, 7, 9, 11]), leftward = chance(0.5);
    const y = rand(0.06, 0.32) * innerHeight, size = rand(0.6, 0.9), duration = rand(12000, 17000);
    for (let i = 0; i < count; i++) {
        const rank = Math.ceil(i / 2), side = i % 2 ? -1 : 1;
        const goose = make(`<div class="goose">${GOOSE}</div>`);
        goose.querySelector('.wing').style.animationDelay = -rand(0, 0.5) + 's';
        fly(goose, { leftward, y: y + rank * 15 * side * size, size, duration, waves: 1, amp: 6, delay: rank * 300 });
        if (i === 0 && chance(0.6)) says(goose, 'Honk!', { delay: rand(2000, 5000), duration: 1500, flipped: leftward });
    }
}

// The turkey struts along the top of the box, pecks, and fans its tail to gobble.
// Click it and it panics: feathers fly and it runs off to the far end.
let fan = '';
const TAIL = ['#7a3e12', '#a0522d', '#c46a28', '#8b4513', '#d9822b'];
for (let i = 0; i < 9; i++) {
    fan += `<g transform="rotate(${(-165 + i * 18.75).toFixed(1)} 22 36)">
        <ellipse cx="38" cy="36" rx="16" ry="5.5" fill="${TAIL[i % TAIL.length]}"/>
        <ellipse cx="50" cy="36" rx="4" ry="4.6" fill="#f4e3c1"/>
        <ellipse cx="51" cy="36" rx="2" ry="2.6" fill="#2b1a0e"/>
    </g>`;
}
logoBox.insertAdjacentHTML('beforeend', `
    <button class="turkey" type="button" aria-label="A turkey. Startle it.">
        <svg viewBox="-14 -2 80 64" aria-hidden="true">
            <g class="tail">${fan}</g>
            <path d="M27 48L25 58M33 48L35 58M21 58H29M31 58H39" stroke="#e0a030" stroke-width="2" stroke-linecap="round"/>
            <ellipse cx="30" cy="38" rx="15" ry="12" fill="#5c3a1e"/>
            <path d="M20 34C26 28 38 30 40 38C34 44 24 44 20 34Z" fill="#4a2e17"/>
            <path d="M24 36C28 34 33 35 36 38M24 39C28 37 33 38 36 41" fill="none" stroke="#6d4528"/>
            <path d="M40 34C44 28 44 22 46 16" stroke="#b8c6d6" stroke-width="5" stroke-linecap="round" fill="none"/>
            <circle cx="47" cy="14" r="5" fill="#b8c6d6"/>
            <ellipse cx="46.5" cy="21" rx="2.2" ry="4" fill="#d62828"/>
            <path d="M51.5 12.5L56 14.5L51.5 16Z" fill="#e8a33b"/>
            <path d="M51 11C54 13 55 18 53 22" stroke="#d62828" stroke-width="2" fill="none" stroke-linecap="round"/>
            <circle cx="48.5" cy="12.5" r="1.1" fill="#111"/>
        </svg>
    </button>`);
const turkey = logoBox.querySelector('.turkey');
const turkeySvg = turkey.querySelector('svg');
const strut = percher(turkey, { lift: 4, stepMs: 300, stride: [10, 18] });
const { nap, wake } = napper();
let panic = false;
strut.x = rand(0, strut.room());
strut.place();

function feathers() {
    for (let i = 0; i < 4; i++) {
        const feather = make(`
            <svg class="feather" viewBox="0 0 10 24" aria-hidden="true">
                <path d="M5 0C9 6 9 16 5 24C1 16 1 6 5 0Z" fill="${pick(TAIL)}"/>
                <path d="M5 2V24" stroke="#f4e3c1" stroke-width="0.8"/>
            </svg>`);
        logoBox.appendChild(feather);
        const x = strut.x + 30, dx = rand(-70, 70);
        feather.animate([
            { transform: `translate(${x}px, -30px) rotate(0)`, opacity: 1 },
            { transform: `translate(${x + dx * 0.5}px, -60px) rotate(${rand(-90, 90)}deg)`, opacity: 1, offset: 0.25 },
            { transform: `translate(${x + dx}px, ${rand(10, 60)}px) rotate(${rand(-200, 200)}deg)`, opacity: 0 }
        ], { duration: rand(1400, 2200), easing: 'ease-out' }).finished.then(() => feather.remove());
    }
}

function gobble(text = pick(TURKEY_SAYS)) {
    turkey.classList.add('fanned');
    turkey.querySelectorAll('.kit-says').forEach(b => b.remove());
    says(turkey, text, { duration: 2200 });
    return wait(2200).then(() => turkey.classList.remove('fanned'));
}

turkey.addEventListener('click', () => {
    gobble();
    if (!calm) feathers();
    panic = true;
    wake();
});

async function turkeyLife() {
    for (;;) {
        if (panic) {
            panic = false;
            await strut.goTo(strut.x < strut.room() / 2 ? strut.room() : 0, 0, { ms: 150, pause: [0, 20] });
            await nap(rand(1500, 3000));
            continue;
        }
        const r = Math.random();
        if (r < 0.55) await strut.goTo(rand(0, strut.room()));
        else if (r < 0.8) {
            await turkeySvg.animate([{ transform: 'rotate(0)' }, { transform: 'rotate(18deg)' }, { transform: 'rotate(0)' }],
                { duration: 300, iterations: 1 + Math.floor(rand(0, 3)) }).finished;
        } else {
            await gobble(chance(0.6) ? 'Gobble gobble!' : pick(TURKEY_SAYS));
        }
        await nap(rand(600, 2500));
    }
}

if (!calm) {
    turkeyLife();
    every(GEESE_EVERY, skein, rand(2000, 5000));
    every(GUST_EVERY, gust);
}
