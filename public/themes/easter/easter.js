// Easter: an egg in a nest on the logo box and the mama hen who looks after it, over a
// pastel morning with an egg hunt in the meadow. The Easter Bunny hops through hiding
// eggs, chicks potter about, egg balloons float up to be popped, blossom petals drift
// down, and a garland of eggs hangs under the sign.
// Loaded by themes/themes.js, which also loads easter.css.
import {
    calm, rand, pick, chance, wait, every, says, make,
    sky, clouds, ground, weather, costume, tools
} from '../kit.js';

(() => {
    const logoBox = document.querySelector('.logo-box');
    if (!logoBox) return;

    // Bunny ears for the digger
    costume({
        tool: tools.fork({ ribbon: '#ff9ec4' }),
        hat: `<path d="M-15 -8Q0 -24 15 -8" stroke="#ff9ec4" stroke-width="3" fill="none"/>
            <g transform="rotate(-16 -6 -16)">
                <ellipse cx="-6" cy="-33" rx="5.5" ry="17" fill="#ffffff" stroke="#dddddd"/>
                <ellipse cx="-6" cy="-32" rx="2.6" ry="12" fill="#ffb3c8"/>
            </g>
            <g transform="rotate(22 7 -16)">
                <ellipse cx="7" cy="-33" rx="5.5" ry="17" fill="#ffffff" stroke="#dddddd"/>
                <ellipse cx="7" cy="-32" rx="2.6" ry="12" fill="#ffb3c8"/>
            </g>`,
        label: 'Stick figure in bunny ears digging with a garden fork'
    });

    logoBox.insertAdjacentHTML('afterbegin', `
        <svg class="nest back" viewBox="0 0 64 28" aria-hidden="true">
            <ellipse cx="32" cy="11" rx="27" ry="7" fill="#4a2f17"/>
            <path d="M8 9C18 4 46 4 56 9" fill="none" stroke="#7a4e26" stroke-width="1.5"/>
        </svg>
        <svg class="nest front" viewBox="0 0 64 28" aria-hidden="true">
            <path d="M2 10C4 24 18 28 32 28C46 28 60 24 62 10C54 16 44 18 32 18C20 18 10 16 2 10Z" fill="#8b5a2b"/>
            <g fill="none" stroke-linecap="round">
                <path d="M3 12C14 20 46 22 61 12" stroke="#6b4423" stroke-width="1.6"/>
                <path d="M5 17C18 25 44 26 59 17" stroke="#a8743f" stroke-width="1.4"/>
                <path d="M10 22C22 27 42 27 54 22" stroke="#6b4423" stroke-width="1.3"/>
                <path d="M6 14C20 18 30 24 48 23" stroke="#b9854d" stroke-width="1"/>
                <path d="M58 14C46 19 34 23 16 23" stroke="#5a3a1c" stroke-width="1"/>
                <path d="M1 9L7 13M63 9L56 14M-2 14L5 15M66 13L59 17M20 26L14 30M44 26L51 29" stroke="#7a4e26" stroke-width="1.3"/>
            </g>
        </svg>
        <div class="mama" aria-hidden="true">
            <div class="face">
                <svg viewBox="0 0 44 40">
                    <g class="legs" fill="none" stroke="#e0a030" stroke-width="1.6" stroke-linecap="round">
                        <path d="M17 31L16 39M13 39L19 39M24 31L25 39M22 39L28 39"/>
                    </g>
                    <path d="M9 18C3 10 4 2 10 5C9 1 15 1 15 6C17 3 21 6 17 13Z" fill="#8a4b17"/>
                    <ellipse cx="20" cy="22" rx="14" ry="11" fill="#c0702a"/>
                    <!-- Feathers fluffed out to cover the egg -->
                    <ellipse class="fluffed" cx="20" cy="22" rx="17" ry="13" fill="#c0702a"/>
                    <ellipse cx="29" cy="17" rx="6" ry="8" fill="#c0702a"/>
                    <path d="M10 20C15 15 26 17 28 24C22 29 13 27 10 20Z" fill="#9a5520"/>
                    <path d="M14 21C18 20 22 21 25 24" fill="none" stroke="#7d4418" stroke-width="0.8"/>
                    <circle cx="32" cy="11" r="7" fill="#c0702a"/>
                    <path d="M28 6C27 2 30 1 31 4C32 0 35 1 34 4C36 2 38 5 35 7Z" fill="#d62828"/>
                    <ellipse cx="37.5" cy="16.5" rx="1.6" ry="2.6" fill="#d62828"/>
                    <path d="M38 9.5L43 12L38 14Z" fill="#f2b134"/>
                    <circle cx="34.5" cy="9.5" r="1.3" fill="#222"/>
                </svg>
            </div>
        </div>
        <div class="egg">
            <!-- Shared shell gradient; kept out of the two views, which each get hidden -->
            <svg width="0" height="0" aria-hidden="true">
                <radialGradient id="egg-shell" cx="0.38" cy="0.35" r="0.75">
                    <stop offset="0" stop-color="#fbf1e2"/>
                    <stop offset="0.6" stop-color="#ead2b0"/>
                    <stop offset="1" stop-color="#c9a47a"/>
                </radialGradient>
            </svg>
            <svg class="whole" viewBox="0 0 60 76" aria-hidden="true">
                <path d="M30 2C14 2 4 30 4 48C4 64 16 74 30 74C44 74 56 64 56 48C56 30 46 2 30 2Z" fill="url(#egg-shell)" stroke="#b48d62" stroke-width="1"/>
                <ellipse cx="20" cy="26" rx="5" ry="9" fill="#ffffff" opacity="0.55" transform="rotate(20 20 26)"/>
            </svg>
            <svg class="broken" viewBox="-70 -44 140 68" aria-hidden="true">
                <clipPath id="egg-rim">
                    <rect x="-70" y="-44" width="140" height="48"/>
                </clipPath>
                <!-- Crumbs of shell scattered about -->
                <g fill="#ead2b0" stroke="#b48d62" stroke-width="0.8">
                    <path d="M-14 22L-9 17L-5 21L-8 23Z"/>
                    <path d="M4 23L7 19L12 22Z"/>
                    <path d="M56 22L60 18L64 21L61 23Z"/>
                </g>
                <!-- Chick, hidden below the shell's rim until it pops up -->
                <g clip-path="url(#egg-rim)">
                    <g class="chick">
                        <ellipse cx="34" cy="-2" rx="12" ry="12" fill="#ffd93b" stroke="#d9a400" stroke-width="1"/>
                        <path d="M24-4C20 0 22 6 27 5Z" fill="#f2c200"/>
                        <circle cx="34" cy="-20" r="9" fill="#ffd93b" stroke="#d9a400" stroke-width="1"/>
                        <path d="M33-30C33-34 36-35 37-32C38-35 41-33 39-29" fill="none" stroke="#d9a400" stroke-width="1.2"/>
                        <circle cx="31" cy="-22" r="1.6" fill="#222"/>
                        <circle cx="38" cy="-22" r="1.6" fill="#222"/>
                        <path d="M32.5-18L35.5-18L34-15Z" fill="#f08a1c"/>
                    </g>
                </g>
                <!-- Bottom half, sitting upright in the mess -->
                <path d="M18 2L23-4L28 3L33-5L38 2L43-3L48 2L50 1C50 14 44 22 35 22C25 22 18 14 18 2Z" fill="url(#egg-shell)" stroke="#b48d62" stroke-width="1"/>
                <!-- Top half, knocked over onto its side -->
                <path d="M-30 20C-48 20-60 14-60 6C-60-2-50-6-36-6L-32-1L-38 3L-31 7L-37 11L-30 15L-35 19Z" fill="url(#egg-shell)" stroke="#b48d62" stroke-width="1"/>
            </svg>
            <div class="egg-bubble" aria-live="polite"></div>
        </div>
    `);

    // The egg: hold the left button to pick it up, let go to drop it to the bottom
    // of the screen. A fall of more than half the screen height breaks it.
    const EGG_URL = 'https://www.google.com/';
    const EGG_SAYS = 'Opening Google';
    const EGG_WAIT = 5000;                    // ms the chick talks before the page opens
    const GRAVITY = 4000;                     // px/s²
    const BOUNCE = 0.35;                      // fraction of speed kept on each bounce
    const egg = document.querySelector('.egg');
    const eggHome = egg.parentElement;
    const bubble = egg.querySelector('.egg-bubble');
    let grab = null, falling = false, cracks = 0;

    function placeEgg(x, y) {
        egg.style.left = x + 'px';
        egg.style.top = y + 'px';
    }

    function squashEgg() {
        egg.classList.remove('squash');
        void egg.offsetWidth;                 // restart the animation
        egg.classList.add('squash');
    }

    egg.addEventListener('pointerdown', e => {
        if (e.button !== 0 || falling || egg.classList.contains('cracked')) return;
        e.preventDefault();
        wakeMama();
        const r = egg.getBoundingClientRect();
        grab = { dx: e.clientX - r.left, dy: e.clientY - r.top };
        document.body.appendChild(egg);
        egg.classList.add('loose', 'held');
        egg.style.position = 'fixed';
        placeEgg(r.left, r.top);
        egg.setPointerCapture(e.pointerId);
    });

    egg.addEventListener('pointermove', e => {
        if (!grab) return;
        const maxX = innerWidth - egg.offsetWidth, maxY = innerHeight - egg.offsetHeight;
        placeEgg(Math.min(maxX, Math.max(0, e.clientX - grab.dx)),
            Math.min(maxY, Math.max(0, e.clientY - grab.dy)));
    });

    function releaseEgg() {
        if (!grab) return;
        grab = null;
        egg.classList.remove('held');
        dropEgg();
    }
    egg.addEventListener('pointerup', releaseEgg);
    egg.addEventListener('pointercancel', releaseEgg);

    function dropEgg() {
        falling = true;
        const floor = innerHeight - egg.offsetHeight;
        let y = parseFloat(egg.style.top), v = 0, last = performance.now();
        const breaks = floor - y > innerHeight * 0.5;
        requestAnimationFrame(function step(now) {
            const dt = Math.min(0.032, (now - last) / 1000);
            last = now;
            v += GRAVITY * dt;
            y += v * dt;
            if (y >= floor) {
                y = floor;
                if (breaks) {
                    placeEgg(parseFloat(egg.style.left), y);
                    crackEgg();
                    return;
                }
                v = -v * BOUNCE;
                if (Math.abs(v) < 80) {
                    placeEgg(parseFloat(egg.style.left), y);
                    settleEgg();
                    return;
                }
                squashEgg();
            }
            placeEgg(parseFloat(egg.style.left), y);
            requestAnimationFrame(step);
        });
    }

    // Pin a landed egg to the page so it scrolls with everything else
    function settleEgg() {
        squashEgg();
        const r = egg.getBoundingClientRect();
        egg.style.position = 'absolute';
        placeEgg(r.left + scrollX, r.top + scrollY);
        falling = false;
    }

    function crackEgg() {
        egg.classList.add('cracked');
        const crack = ++cracks;
        bubble.textContent = EGG_SAYS;
        // Keep the bubble on screen when the egg lands near either edge
        bubble.style.setProperty('--nudge', '0px');
        const r = bubble.getBoundingClientRect();
        const nudge = Math.min(0, innerWidth - 8 - r.right) || Math.max(0, 8 - r.left);
        bubble.style.setProperty('--nudge', nudge + 'px');

        setTimeout(() => {
            // The browser may block this: the click that dropped the egg is a few seconds old.
            // If so, the chick offers a link instead.
            const page = window.open(EGG_URL, '_blank');
            if (page) {
                page.opener = null;
                resetEgg(crack);
                return;
            }
            const link = document.createElement('a');
            link.href = EGG_URL;
            link.target = '_blank';
            link.rel = 'noopener';
            link.textContent = EGG_SAYS.replace(/^Opening/, 'Open') + ' →';
            link.addEventListener('click', () => setTimeout(resetEgg, 300, crack));
            bubble.replaceChildren(link);
            setTimeout(resetEgg, 8000, crack);
        }, EGG_WAIT + 700);
    }

    // Fade out the broken shell and put a fresh egg back in the nest
    function resetEgg(crack) {
        if (crack !== cracks || !egg.classList.contains('cracked') || egg.classList.contains('gone')) return;
        egg.classList.add('gone');
        setTimeout(() => {
            egg.classList.remove('loose', 'cracked', 'gone', 'squash');
            egg.removeAttribute('style');
            bubble.replaceChildren();
            eggHome.prepend(egg);
            falling = false;
        }, 500);
    }

    // Mama hen hops back and forth along the top of the logo box at random, and
    // every so often goes to sit on the egg (only while it is in the nest).
    const mama = document.querySelector('.mama');
    const mamaSvg = mama.querySelector('svg');
    const HOP_MS = 260, HOP_UP = 14, MAX_HOP = 40;
    const SIT_DROP = -4;                      // px she rises when perched on the egg
    let mamaX = 0, mamaY = 0, wakeMama = () => {};

    const rand = (min, max) => min + Math.random() * (max - min);
    const nestX = () => eggHome.clientWidth - 42 - mama.offsetWidth * 0.45;

    // Waits ms, or less if she is woken (the egg being grabbed)
    function nap(ms) {
        return new Promise(resolve => {
            const t = setTimeout(resolve, ms);
            wakeMama = () => { clearTimeout(t); wakeMama = () => {}; resolve(); };
        });
    }

    function placeMama() {
        mama.style.transform = `translate(${mamaX}px, ${mamaY}px)`;
    }

    function hop(x, y = 0) {
        const from = `translate(${mamaX}px, ${mamaY}px)`;
        const peak = `translate(${(mamaX + x) / 2}px, ${Math.min(mamaY, y) - HOP_UP}px)`;
        mamaX = x;
        mamaY = y;
        placeMama();
        return mama.animate([
            { transform: from, easing: 'ease-out' },
            { transform: peak, easing: 'ease-in' },
            { transform: mama.style.transform }
        ], HOP_MS).finished;
    }

    async function hopTo(x, y = 0) {
        mama.classList.toggle('left', x < mamaX);
        while (Math.abs(x - mamaX) > 1) {
            const step = Math.sign(x - mamaX) * Math.min(Math.abs(x - mamaX), rand(MAX_HOP * 0.6, MAX_HOP));
            const last = Math.abs(x - mamaX - step) <= 1;
            await hop(mamaX + step, last ? y : 0);
            await nap(rand(40, 160));
        }
    }

    function peck() {
        return mamaSvg.animate([
            { transform: 'rotate(0)' }, { transform: 'rotate(22deg)' }, { transform: 'rotate(0)' }
        ], { duration: 280, iterations: 1 + Math.floor(Math.random() * 3) }).finished;
    }

    async function mamaLife() {
        const room = () => Math.max(0, eggHome.clientWidth - mama.offsetWidth);
        mamaX = rand(0, room() * 0.6);
        placeMama();
        for (;;) {
            if (egg.parentElement === eggHome && Math.random() < 0.35) {
                await hopTo(nestX(), SIT_DROP);
                mama.classList.remove('left');
                if (egg.parentElement === eggHome) {
                    mama.classList.add('sitting');
                    await nap(rand(4000, 12000));
                    mama.classList.remove('sitting');
                }
                await hopTo(mamaX - rand(30, 60));
            } else {
                await hopTo(rand(0, room()));
                if (Math.random() < 0.5) await peck();
                await nap(rand(400, 3000));
            }
        }
    }

    if (matchMedia('(prefers-reduced-motion: reduce)').matches) {
        // No hopping: she just sits on the egg
        mamaX = nestX();
        mamaY = SIT_DROP;
        mama.classList.add('sitting');
        placeMama();
    } else {
        mamaLife();
    }
})();

// The rest of Easter morning: a pastel sky over a meadow with an egg hunt in it, the
// Easter Bunny hopping through hiding more eggs, baby chicks, egg balloons floating up,
// blossom petals, and a garland of eggs under the sign.
const EGGS_HIDDEN = 8;                        // eggs in the meadow at a time
const BUNNY_EVERY = [12000, 22000];           // ms between the bunny's rounds
const BALLOON_EVERY = [3500, 8000];           // ms between balloons
const PETAL_AREA = 16000;                     // px² of window per petal
const PASTELS = ['#ffb3c8', '#b5e3ff', '#c9b6ff', '#fff1a8', '#b8f2c9', '#ffd1a8'];
const DEEPER = { '#ffb3c8': '#e8789c', '#b5e3ff': '#5fb3e6', '#c9b6ff': '#9a7ff0', '#fff1a8': '#e6c740', '#b8f2c9': '#5fcf86', '#ffd1a8': '#f09a54' };
const BUNNY_SAYS = ['Happy Easter!', 'Hop hop!', 'Find my eggs!', 'Have a carrot?'];

// An egg, decorated: a pastel shell with stripes, zigzags or dots in a deeper shade.
// Drawn in a 30 x 38 box, standing on its broad end.
let eggIds = 0;
function decoratedEgg(colour = pick(PASTELS), pattern = pick(['stripes', 'zigzag', 'dots', 'band'])) {
    const id = 'easter-shell-' + eggIds++, ink = DEEPER[colour];
    const marks = {
        stripes: `<path d="M3 15H27M2 24H28" stroke="${ink}" stroke-width="3"/>`,
        zigzag: `<path d="M2 19L7 15L12 19L17 15L22 19L27 15" fill="none" stroke="${ink}" stroke-width="2.5"/>
                 <path d="M3 27H27" stroke="#ffffff" stroke-width="2"/>`,
        dots: `<circle cx="9" cy="14" r="2.4" fill="${ink}"/><circle cx="20" cy="12" r="2" fill="${ink}"/>
               <circle cx="15" cy="21" r="2.6" fill="${ink}"/><circle cx="8" cy="27" r="2" fill="${ink}"/><circle cx="22" cy="27" r="2.4" fill="${ink}"/>`,
        band: `<path d="M2 18H28V23H2Z" fill="${ink}"/><path d="M5 20.5H25" stroke="#ffffff" stroke-width="1.2" stroke-dasharray="2 2"/>`
    };
    return `<defs><clipPath id="${id}"><path d="M15 1C7 1 1 14 1 24C1 32 7 37 15 37C23 37 29 32 29 24C29 14 23 1 15 1Z"/></clipPath></defs>
        <path d="M15 1C7 1 1 14 1 24C1 32 7 37 15 37C23 37 29 32 29 24C29 14 23 1 15 1Z" fill="${colour}" stroke="${ink}" stroke-width="0.8"/>
        <g clip-path="url(#${id})">${marks[pattern]}</g>
        <ellipse cx="10" cy="11" rx="2.5" ry="4.5" fill="#ffffff" opacity="0.6" transform="rotate(20 10 11)"/>`;
}

// Pastel morning sky, a soft sun and white clouds
const morning = sky('easter-sky');
morning.insertAdjacentHTML('beforeend', '<div class="easter-sun"></div>');
clouds(morning, {
    count: 6, top: [2, 40],
    light: [[255, 255, 255], [252, 248, 255]], dark: [[234, 228, 246], [222, 214, 240]]
});

// The meadow, with an egg-hunt sign and an overflowing basket on the skyline
let basketEggs = '';
[[22, 18], [36, 14], [50, 17], [29, 8], [43, 7]].forEach(([x, y], i) => {
    basketEggs += `<g transform="translate(${x - 9} ${y - 12}) scale(0.6)">${decoratedEgg(PASTELS[i % PASTELS.length])}</g>`;
});
const meadow = ground('easter-meadow', `
    <svg class="skyline-prop" viewBox="0 0 90 80" style="left: 4%">
        <rect x="40" y="20" width="7" height="60" fill="#8b5a2b"/>
        <path d="M6 14H70L82 26L70 38H6Z" fill="#c9965a" stroke="#8b5a2b" stroke-width="2"/>
        <text x="40" y="31" text-anchor="middle" font-family="Georgia, serif" font-weight="bold" font-size="12" fill="#6b3f1c"
              textLength="56" lengthAdjust="spacingAndGlyphs">EGG HUNT</text>
        <path d="M30 80C34 70 40 70 44 76C48 68 56 70 58 80Z" fill="#6cb84a"/>
    </svg>
    <svg class="skyline-prop" viewBox="0 0 72 60" style="right: 5%">
        <path d="M12 20C12 -4 60 -4 60 20" fill="none" stroke="#a0702f" stroke-width="4"/>
        ${basketEggs}
        <path d="M6 24H66L60 56H12Z" fill="#c9965a"/>
        <path d="M8 32H64M9 40H63M10 48H62M20 24V56M32 24V56M44 24V56M56 24V56" stroke="#a0702f" stroke-width="1.6"/>
        <path d="M6 24H66" stroke="#8b5a2b" stroke-width="4"/>
        <path d="M60 22C70 16 72 26 64 28C72 30 68 40 60 32" fill="#ff9ec4"/>
    </svg>`);

// Blossom petals in pastels, drifting down and resting a moment where they land
function drawPetal(pen, x, y, rot, size, squash, colour, alpha = 1) {
    pen.save();
    pen.globalAlpha = alpha;
    pen.translate(x, y);
    pen.rotate(rot);
    pen.scale(size, size * squash);
    pen.fillStyle = colour;
    pen.beginPath();
    pen.moveTo(0, -5);
    pen.bezierCurveTo(4, -4, 4, 3, 0, 5);
    pen.bezierCurveTo(-4, 3, -4, -4, 0, -5);
    pen.fill();
    pen.restore();
}
weather({
    ground: meadow, area: PETAL_AREA, max: 110,
    spawn: anywhere => ({
        x: rand(-80, innerWidth), y: anywhere ? rand(-innerHeight, innerHeight) : rand(-40, -10),
        size: rand(0.7, 1.4), rot: rand(0, 6.3), spin: rand(-2, 2), vy: rand(22, 45), sway: rand(25, 55),
        phase: rand(0, 6.3), tumble: rand(0, 6.3), colour: pick(PASTELS), t: 0
    }),
    step(p, dt, w) {
        p.t += dt;
        p.y += p.vy * dt;
        p.x += (Math.sin(p.t * 1.2 + p.phase) * p.sway + 10 + w.wind) * dt;
        p.rot += p.spin * dt;
        p.tumble += dt * 2.5;
    },
    draw: (pen, p) => drawPetal(pen, p.x, p.y, p.rot, p.size, Math.cos(p.tumble), p.colour),
    splash: p => ({ life: 4, draw: (pen, k) => drawPetal(pen, p.x, p.land, p.rot, p.size, 0.5, p.colour, k < 0.7 ? 1 : (1 - k) / 0.3) })
});

// Confetti over everything, for bursts: popped balloons and finding every egg
const confetti = weather({
    front: true, density: 0,
    spawn: () => ({ x: -500, y: -500, vx: 0, vy: 0, t: 0 }),
    step(c, dt) {
        c.t += dt;
        c.vy = Math.min(c.vy + 340 * dt, 110);
        c.vx *= 1 - 1.8 * dt;
        c.x += (c.vx + Math.sin(c.t * 3 + c.phase) * 20) * dt;
        c.y += c.vy * dt;
        c.rot += c.spin * dt;
        c.tumble += dt * 7;
    },
    draw(pen, c) {
        pen.save();
        pen.translate(c.x, c.y);
        pen.rotate(c.rot);
        pen.scale(1, Math.cos(c.tumble));
        pen.fillStyle = c.colour;
        pen.fillRect(-c.w / 2, -c.h / 2, c.w, c.h);
        pen.restore();
    }
});
function burst(x, y, count = 50) {
    for (let i = 0; i < count; i++) {
        confetti.add({
            x, y, vx: rand(-240, 240), vy: -rand(150, 420), t: 0, phase: rand(0, 6.3),
            rot: rand(0, 6.3), spin: rand(-5, 5), tumble: rand(0, 6.3),
            w: rand(4, 8), h: rand(2.5, 4), colour: pick([...PASTELS, ...Object.values(DEEPER)])
        });
    }
}

// The egg hunt. Eggs are hidden in tufts of grass about the meadow, away from the dig
// site; click one to find it. Find them all and a fresh batch is hidden.
const box = document.querySelector('.logo-box');
box.insertAdjacentHTML('beforeend', `
    <svg class="egg-garland" aria-hidden="true"></svg>
    <span class="hunt-score" role="status"></span>`);
const huntScore = box.querySelector('.hunt-score');
let found = 0, round = 1;
const showHunt = () => {
    const hidden = document.querySelectorAll('.hidden-egg:not(.found)').length;
    huntScore.textContent = `Eggs found: ${found}` + (hidden ? ` · ${hidden} still hidden` : '');
};

function hideEgg(xPercent = null) {
    const site = document.querySelector('.dig-site')?.getBoundingClientRect();
    let x = xPercent, depth = rand(22, 120);
    for (let tries = 0; x === null && tries < 20; tries++) {
        const guess = rand(3, 95), px = guess / 100 * innerWidth;
        if (!site || depth > 110 || px < site.left - 30 || px > site.right + 30) x = guess;
        else depth = rand(22, 120);
    }
    if (x === null) x = rand(3, 95);
    const egg = make(`
        <button class="hidden-egg" type="button" aria-label="A hidden Easter egg. Find it." style="left: ${x.toFixed(1)}%">
            <svg viewBox="-6 -2 42 44" aria-hidden="true">
                <g class="shell" transform="rotate(${rand(-25, 25).toFixed(0)} 15 30)">${decoratedEgg()}</g>
                <path class="tuft" d="M-4 42C-2 32 0 28 2 24L5 36L8 20L12 37L15 26L18 38L22 22L25 36L28 27L31 38L34 24L36 42Z" fill="#5fae3f"/>
            </svg>
        </button>`);
    meadow.pin(egg, depth);
    egg.addEventListener('click', () => {
        if (egg.classList.contains('found')) return;
        egg.classList.add('found');
        found++;
        showHunt();
        const r = egg.getBoundingClientRect();
        if (!calm) burst(r.left + r.width / 2, r.top + 8, 18);
        says(egg, pick(['Found one!', 'Egg-cellent!', 'Nice!', 'Another one!']), { duration: 1400 });
        wait(1500).then(() => egg.remove()).then(() => {
            showHunt();
            if (!document.querySelector('.hidden-egg:not(.found)')) allFound();
        });
    });
    showHunt();
    return egg;
}

function allFound() {
    round++;
    huntScore.textContent = `You found them all! Round ${round} coming up…`;
    if (!calm) for (let i = 0; i < 5; i++) wait(i * 180).then(() => burst(rand(0.15, 0.85) * innerWidth, rand(0.1, 0.4) * innerHeight, 60));
    wait(3000).then(() => {
        if (calm) for (let i = 0; i < EGGS_HIDDEN; i++) hideEgg();
        else bunnyRound(EGGS_HIDDEN);
    });
}

// A garland of little eggs strung under the sign
function stringGarland() {
    const garland = box.querySelector('.egg-garland');
    const width = box.clientWidth, count = Math.max(6, Math.round(width / 34));
    garland.setAttribute('viewBox', `0 0 ${width} 34`);
    let eggs = '';
    for (let i = 0; i < count; i++) {
        const t = (i + 0.5) / count, x = t * width, y = 2 + 4 * 9 * t * (1 - t);
        eggs += `<g class="garland-egg" style="transform-origin: ${x.toFixed(1)}px ${y.toFixed(1)}px; animation-delay: ${-rand(0, 3).toFixed(1)}s">
            <path d="M${x.toFixed(1)} ${y.toFixed(1)}V${(y + 3).toFixed(1)}" stroke="#8b5a2b"/>
            <g transform="translate(${(x - 6).toFixed(1)} ${(y + 3).toFixed(1)}) scale(0.4)">${decoratedEgg(PASTELS[i % PASTELS.length])}</g>
        </g>`;
    }
    let string = 'M0 2';
    for (let i = 1; i <= 20; i++) {
        const t = i / 20;
        string += `L${(t * width).toFixed(1)} ${(2 + 36 * t * (1 - t)).toFixed(1)}`;
    }
    garland.innerHTML = `<path d="${string}" fill="none" stroke="#8b5a2b" stroke-width="1"/>${eggs}`;
}
stringGarland();
new ResizeObserver(stringGarland).observe(box);

// The Easter Bunny hops across the meadow with his basket, hiding eggs as he goes.
// Click him for a word.
const BUNNY = `
    <svg viewBox="0 0 70 64" aria-hidden="true">
        <circle cx="12" cy="40" r="6" fill="#ffffff"/>
        <ellipse cx="28" cy="44" rx="17" ry="13" fill="#d9cfc4"/>
        <ellipse cx="31" cy="48" rx="10" ry="8" fill="#f4efe8"/>
        <ellipse cx="20" cy="56" rx="9" ry="4" fill="#d9cfc4"/>
        <g class="ears">
            <ellipse cx="44" cy="12" rx="4.5" ry="13" fill="#d9cfc4" transform="rotate(-12 44 22)"/>
            <ellipse cx="44" cy="12" rx="2.2" ry="9.5" fill="#ffb3c8" transform="rotate(-12 44 22)"/>
            <ellipse cx="51" cy="13" rx="4.5" ry="13" fill="#d9cfc4" transform="rotate(14 51 23)"/>
            <ellipse cx="51" cy="13" rx="2.2" ry="9.5" fill="#ffb3c8" transform="rotate(14 51 23)"/>
        </g>
        <circle cx="48" cy="30" r="10" fill="#d9cfc4"/>
        <circle cx="52" cy="28" r="1.6" fill="#222"/>
        <ellipse cx="57.5" cy="32" rx="1.8" ry="1.3" fill="#ff8fb1"/>
        <path d="M54 34L62 33M54 35L62 37" stroke="#9a8f86" stroke-width="0.6"/>
        <path d="M34 54C36 62 46 62 48 54" fill="none" stroke="#a0702f" stroke-width="2"/>
        <path d="M32 50H52L49 62H35Z" fill="#c9965a"/>
        <path d="M33 54H51M34 58H50" stroke="#a0702f" stroke-width="1"/>
        <circle cx="37" cy="49" r="3" fill="#ffb3c8"/><circle cx="43" cy="48" r="3" fill="#b5e3ff"/><circle cx="48" cy="49" r="3" fill="#fff1a8"/>
    </svg>`;
const bunny = make(`<button class="bunny" type="button" aria-label="The Easter Bunny. Say hello.">${BUNNY}</button>`);
meadow.pin(bunny, -58);
let bunnyBusy = false;

async function bunnyRound(eggsToHide = Math.max(0, EGGS_HIDDEN - document.querySelectorAll('.hidden-egg:not(.found)').length)) {
    if (bunnyBusy) return;
    bunnyBusy = true;
    const leftward = chance(0.5), start = leftward ? innerWidth + 20 : -90, end = leftward ? -90 : innerWidth + 20;
    const duration = Math.abs(end - start) * 9, hops = Math.round(Math.abs(end - start) / 70);
    bunny.classList.toggle('left', leftward);
    bunny.classList.add('hopping');
    const frames = [];
    for (let i = 0; i <= hops * 4; i++) {
        const t = i / (hops * 4), x = start + (end - start) * t;
        const y = -Math.abs(Math.sin(t * hops * Math.PI)) * 26;
        frames.push({ transform: `translate(${x.toFixed(0)}px, ${y.toFixed(1)}px)` });
    }
    const run = bunny.animate(frames, { duration, easing: 'linear' });
    // Hide eggs at points along the way
    for (let i = 0; i < eggsToHide; i++) {
        const t = (i + 0.5 + rand(-0.3, 0.3)) / eggsToHide;
        wait(duration * t).then(() => {
            const x = (start + (end - start) * t + 35) / innerWidth * 100;
            if (x > 2 && x < 96) hideEgg(x);
        });
    }
    await run.finished;
    bunny.classList.remove('hopping');
    bunny.style.transform = `translate(${end}px, 0)`;
    bunnyBusy = false;
}

bunny.addEventListener('click', () => {
    bunny.querySelectorAll('.kit-says').forEach(b => b.remove());
    says(bunny, pick(BUNNY_SAYS), { duration: 2000 });
});

// Baby chicks pottering about the meadow, with a peep now and then
for (let i = 0; i < 3; i++) {
    const chick = make(`
        <div class="meadow-chick" aria-hidden="true" style="left: ${rand(4, 22).toFixed(1)}%">
            <svg viewBox="0 0 26 24">
                <ellipse cx="12" cy="15" rx="9" ry="8" fill="#ffd93b"/>
                <circle cx="17" cy="8" r="5.5" fill="#ffd93b"/>
                <circle cx="18.5" cy="7" r="1" fill="#222"/>
                <path d="M22 8L26 9L22 10.5Z" fill="#f08a1c"/>
                <path d="M7 14C10 11 14 13 15 16C12 18 9 17 7 14Z" fill="#f2c200"/>
                <path d="M10 22V24M14 22V24" stroke="#f08a1c" stroke-width="1.2"/>
            </svg>
        </div>`);
    meadow.pin(chick, rand(8, 40) - 24);
    if (calm) continue;
    let at = 0;
    every([1500, 4500], async () => {
        const to = Math.max(-60, Math.min(160, at + rand(-60, 60)));
        chick.classList.toggle('left', to < at);
        const frames = [];
        for (let k = 0; k <= 8; k++) {
            const t = k / 8;
            frames.push({ transform: `translate(${(at + (to - at) * t).toFixed(1)}px, ${(-Math.abs(Math.sin(t * 4 * Math.PI)) * 4).toFixed(1)}px)` });
        }
        await chick.animate(frames, { duration: Math.abs(to - at) * 18 + 200, fill: 'forwards' }).finished;
        at = to;
        if (chance(0.3)) says(chick, 'Peep!', { duration: 1200 });
    }, rand(500, 2500));
}

// Egg-shaped balloons floating up the window. Click one to pop it.
function balloon() {
    const colour = pick(PASTELS);
    const el = make(`
        <button class="egg-balloon" type="button" aria-label="A balloon. Pop it." style="left: ${rand(3, 92).toFixed(1)}%">
            <svg viewBox="0 0 40 110" aria-hidden="true">
                <path d="M20 50C24 62 14 72 20 84C26 96 16 104 20 110" fill="none" stroke="#9a8f86" stroke-width="1"/>
                <path d="M17 49L20 45L23 49Z" fill="${DEEPER[colour]}"/>
                <g transform="translate(5 0) scale(1, 1.25)">${decoratedEgg(colour)}</g>
            </svg>
        </button>`);
    document.body.appendChild(el);
    const sway = rand(20, 50), rise = innerHeight + 160, duration = rand(9000, 14000);
    const frames = [];
    for (let k = 0; k <= 12; k++) {
        const t = k / 12;
        frames.push({ transform: `translate(${(Math.sin(t * Math.PI * 3) * sway).toFixed(1)}px, ${(-t * rise).toFixed(0)}px) rotate(${(Math.sin(t * Math.PI * 3 + 1) * 6).toFixed(1)}deg)` });
    }
    const flight = el.animate(frames, { duration, easing: 'linear' });
    flight.finished.then(() => el.remove(), () => {});
    el.addEventListener('click', () => {
        const r = el.getBoundingClientRect();
        flight.cancel();
        el.remove();
        burst(r.left + r.width / 2, r.top + 30, 45);
    });
}

for (let i = 0; i < EGGS_HIDDEN - 3; i++) hideEgg();          // some are hidden already
if (calm) {
    for (let i = 0; i < 3; i++) hideEgg();
    bunny.style.transform = `translate(${Math.round(innerWidth * 0.82)}px, 0)`;
} else {
    wait(1500).then(() => bunnyRound(3));
    every(BUNNY_EVERY, () => bunnyRound());
    every(BALLOON_EVERY, balloon, 1200);
}
