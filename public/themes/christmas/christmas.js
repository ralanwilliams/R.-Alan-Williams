// Christmas: a starry night with snow falling on a snowy yard, Santa's sleigh flying over
// and dropping presents to open, and the logo box snowed on and strung with lights that
// change pattern when clicked.
// Loaded by themes/themes.js, which also loads christmas.css.
import {
    calm, rand, pick, chance, wait, every, tween, says, make, logoBox,
    sky, stars, clouds, ground, snowfall, snowCap, fly, costume, tools
} from '../kit.js';

// His Santa hat, and a scarf
costume({
    tool: tools.snowShovel(),
    over: '<path d="M131 72Q140 77 149 71L150 76Q140 82 131 77Z" fill="#d62828"/><path d="M146 74L163 85L159 90L143 78Z" fill="#d62828"/><path d="M151 77.5L154 80M156 81L159 83.5" stroke="#ffffff" stroke-width="2.2"/>',
    label: 'Stick figure in a Santa hat and scarf shovelling snow'
});

const SANTA_SAYS = ['Ho ho ho!', 'Merry Christmas!', 'On, Dasher! On, Dancer!', 'Have you been good?'];
const PRESENTS_HOLD = ['A new project!', 'Socks. Again.', 'A lump of coal?!', 'Merry Christmas!', 'Just what I wanted!', 'More RAM!'];
const SANTA_EVERY = [14000, 26000];           // ms between flights
const GUST_EVERY = [8000, 16000];             // ms between gusts of wind in the snow
const MAX_PRESENTS = 6;                       // on the ground at once
const PATTERNS = ['twinkle', 'chase', 'alternate', 'steady'];
const BULB_COLOURS = ['#ff3b3b', '#ffd23f', '#3bd16f', '#3b9dff', '#ff7ad9', '#ff9f1c'];

// Night sky with a full moon and a few dark clouds
const night = sky('night-sky');
stars(night, { count: 170 });
night.insertAdjacentHTML('beforeend', `
    <svg class="moon" viewBox="0 0 80 80">
        <circle cx="40" cy="40" r="30" fill="#f6f1d3"/>
        <circle cx="30" cy="32" r="6" fill="#e6dfb8"/>
        <circle cx="50" cy="47" r="8" fill="#e6dfb8"/>
        <circle cx="47" cy="25" r="3.5" fill="#e6dfb8"/>
    </svg>`);
clouds(night, {
    count: 4, top: [4, 35],
    light: [[70, 86, 120], [52, 66, 98]], dark: [[44, 56, 86], [34, 44, 70]]
});

// The yard: snowy ground with a snowman, a lit tree, pines and a cabin on the skyline
const pine = (x, h) => `
    <path d="M${x} ${44 - h}L${x - h * 0.35} 44H${x + h * 0.35}Z" fill="#1d4d34"/>
    <path d="M${x} ${44 - h}L${x - h * 0.16} ${44 - h * 0.6}H${x + h * 0.16}Z" fill="#f4f8ff"/>`;
let treeLights = '';
for (let i = 0; i < 16; i++) {
    const y = rand(14, 60), half = (y - 6) * 0.42;
    treeLights += `<circle class="tree-light" cx="${(30 + rand(-half, half)).toFixed(1)}" cy="${y.toFixed(1)}" r="1.6"
        fill="${pick(BULB_COLOURS)}" style="animation-delay: ${-rand(0, 2).toFixed(2)}s"/>`;
}
const yard = ground('snowfield', `
    <svg class="yard snowman" viewBox="0 0 50 70" style="left: 6%">
        <path d="M14 34L2 26M36 34L48 24" stroke="#5a3a1e" stroke-width="1.6"/>
        <circle cx="25" cy="54" r="15" fill="#ffffff" stroke="#c9d6e8"/>
        <circle cx="25" cy="31" r="11" fill="#ffffff" stroke="#c9d6e8"/>
        <circle cx="25" cy="14" r="8" fill="#ffffff" stroke="#c9d6e8"/>
        <path d="M17 21C22 24 28 24 33 21L34 24C28 27 22 27 16 24Z" fill="#d62828"/>
        <path d="M30 23L33 32L29 31Z" fill="#d62828"/>
        <circle cx="22" cy="12" r="1.2" fill="#222"/><circle cx="28" cy="12" r="1.2" fill="#222"/>
        <path d="M25 14L32 16L25 16.5Z" fill="#f08a1c"/>
        <circle cx="25" cy="30" r="1.3" fill="#222"/><circle cx="25" cy="36" r="1.3" fill="#222"/>
        <rect x="17" y="5" width="16" height="2" fill="#222"/><rect x="19.5" y="-5" width="11" height="11" fill="#222"/>
        <rect x="19.5" y="2" width="11" height="2" fill="#d62828"/>
    </svg>
    <svg class="yard" viewBox="0 0 70 44" style="left: 12%">${pine(18, 30)}${pine(44, 40)}</svg>
    <svg class="yard tree" viewBox="0 0 60 80" style="right: 6%">
        <rect x="26" y="66" width="8" height="12" fill="#5a3a1e"/>
        <path d="M30 6L6 70H54Z" fill="#1f6b3a"/>
        <path d="M30 6L14 40H46Z" fill="#26804a"/>
        <path d="M12 52C24 58 38 50 50 56M16 36C26 41 36 34 45 39M21 22C27 25 33 21 39 24" fill="none" stroke="#d4af37" stroke-width="1.2"/>
        ${treeLights}
        <path class="tree-star" d="M30 0L32.4 5.5L38 6L33.6 9.6L35 15L30 12L25 15L26.4 9.6L22 6L27.6 5.5Z" fill="#ffd23f"/>
    </svg>
    <svg class="yard cabin" viewBox="0 0 80 60" style="right: 14%">
        <g class="smoke" fill="#cfd8e6">
            <circle cx="60" cy="8" r="4"/><circle cx="64" cy="2" r="5"/><circle cx="58" cy="-6" r="6"/>
        </g>
        <rect x="56" y="12" width="7" height="16" fill="#6b3f2a"/>
        <rect x="10" y="30" width="60" height="30" fill="#7a4a2c"/>
        <path d="M10 36H70M10 42H70M10 48H70M10 54H70" stroke="#5e3820" stroke-width="1"/>
        <path d="M4 32L40 10L76 32Z" fill="#f4f8ff" stroke="#c9d6e8"/>
        <rect class="window" x="20" y="38" width="12" height="11" fill="#ffd36b"/>
        <rect class="window" x="48" y="38" width="12" height="11" fill="#ffd36b"/>
        <rect x="35" y="44" width="10" height="16" fill="#4a2c1a"/>
    </svg>`);

// Snow, with gusts of wind now and then
const snow = snowfall(yard, { area: 5000 });

// Snow on the logo box, and a string of lights draped along its top edge
snowCap();
logoBox.insertAdjacentHTML('beforeend', `
    <svg class="xmas-lights pattern-twinkle" aria-hidden="true"></svg>
    <button class="lights-switch" type="button" aria-label="Christmas lights. Change the pattern."></button>`);
const lights = logoBox.querySelector('.xmas-lights');
let pattern = 0;

function stringLights() {
    const width = logoBox.clientWidth + 8, hooks = Math.max(3, Math.round(width / 70));
    const span = width / hooks, sag = 10;
    lights.setAttribute('viewBox', `0 0 ${width} 30`);
    let wire = '', bulbs = '', n = 0;
    for (let h = 0; h < hooks; h++) {
        const x0 = h * span, x1 = x0 + span, cx = x0 + span / 2;
        wire += `M${x0.toFixed(1)} 3Q${cx.toFixed(1)} ${3 + sag * 2} ${x1.toFixed(1)} 3`;
        for (const t of [0.2, 0.5, 0.8]) {
            const x = (1 - t) ** 2 * x0 + 2 * (1 - t) * t * cx + t * t * x1;
            const y = (1 - t) ** 2 * 3 + 2 * (1 - t) * t * (3 + sag * 2) + t * t * 3;
            const colour = BULB_COLOURS[n % BULB_COLOURS.length];
            bulbs += `
                <g class="bulb" style="--i: ${n}; --d: ${rand(0.8, 2.2).toFixed(2)}s; --o: ${-rand(0, 2).toFixed(2)}s">
                    <circle cx="${x.toFixed(1)}" cy="${(y + 7).toFixed(1)}" r="7" fill="${colour}" opacity="0.35" filter="url(#bulb-glow)"/>
                    <rect x="${(x - 1.6).toFixed(1)}" y="${(y - 0.5).toFixed(1)}" width="3.2" height="3" fill="#1d3b22"/>
                    <ellipse cx="${x.toFixed(1)}" cy="${(y + 6).toFixed(1)}" rx="2.6" ry="4" fill="${colour}"/>
                </g>`;
            n++;
        }
    }
    lights.innerHTML = `
        <defs><filter id="bulb-glow" x="-1" y="-1" width="3" height="3"><feGaussianBlur stdDeviation="2.5"/></filter></defs>
        <path d="${wire}" fill="none" stroke="#1d3b22" stroke-width="1.2"/>${bulbs}`;
}
stringLights();
new ResizeObserver(stringLights).observe(logoBox);

const lightSwitch = logoBox.querySelector('.lights-switch');
lightSwitch.addEventListener('click', () => {
    lights.classList.remove('pattern-' + PATTERNS[pattern]);
    pattern = (pattern + 1) % PATTERNS.length;
    lights.classList.add('pattern-' + PATTERNS[pattern]);
    lightSwitch.querySelectorAll('.kit-says').forEach(b => b.remove());
    says(lightSwitch, PATTERNS[pattern][0].toUpperCase() + PATTERNS[pattern].slice(1), { duration: 1400 });
});

// Presents fall from the sleigh onto the yard. Click one to open it.
function dropPresent(from) {
    if (document.querySelectorAll('.present').length >= MAX_PRESENTS) return;
    const [box, ribbon] = pick([['#d62828', '#ffd23f'], ['#2a9d8f', '#ffffff'], ['#3b5bdb', '#ff7ad9'], ['#ffd23f', '#d62828'], ['#7b2cbf', '#3bd16f']]);
    const present = make(`
        <button class="present" type="button" aria-label="A present. Open it.">
            <svg viewBox="0 0 34 34" aria-hidden="true">
                <rect x="4" y="14" width="26" height="19" fill="${box}"/>
                <rect x="15" y="14" width="4" height="19" fill="${ribbon}"/>
                <g class="lid">
                    <rect x="2" y="9" width="30" height="6" fill="${box}" stroke="rgba(0,0,0,0.2)"/>
                    <rect x="15" y="9" width="4" height="6" fill="${ribbon}"/>
                    <path d="M17 9C11 2 7 6 12 9M17 9C23 2 27 6 22 9" fill="none" stroke="${ribbon}" stroke-width="2.2"/>
                </g>
            </svg>
        </button>`);
    document.body.appendChild(present);
    const x0 = from.x - 17, y0 = from.y + scrollY - 17;
    const x1 = Math.max(10, Math.min(innerWidth - 44, x0 + rand(-60, 60)));
    const y1 = Math.min(yard.top + rand(14, 70), document.documentElement.scrollHeight - 40);
    present.style.transform = `translate(${x1}px, ${y1}px)`;
    present.animate([
        { transform: `translate(${x0}px, ${y0}px) rotate(${rand(-40, 40)}deg)`, easing: 'cubic-bezier(0.5, 0, 1, 1)' },
        { transform: `translate(${x1}px, ${y1}px)`, offset: 0.85, easing: 'ease-out' },
        { transform: `translate(${x1}px, ${y1 - 8}px)`, offset: 0.93, easing: 'ease-in' },
        { transform: `translate(${x1}px, ${y1}px)` }
    ], { duration: Math.max(900, (y1 - y0) * 2.2) });

    const leave = () => present.animate([{ opacity: 1 }, { opacity: 0 }], 600).finished.then(() => present.remove());
    const unopened = setTimeout(leave, 20000);
    present.addEventListener('click', () => {
        if (present.classList.contains('open')) return;
        clearTimeout(unopened);
        present.classList.add('open');
        says(present, pick(PRESENTS_HOLD), { duration: 2800 });
        setTimeout(leave, 3000);
    });
}

// Santa's sleigh: across the sky, often dropping a present on the way
function santa() {
    const sleigh = make(`
        <div class="santa">
            <svg viewBox="0 0 220 70">
                <path d="M78 38L196 34" stroke="#d4af37" stroke-width="1"/>
                ${[115, 140, 165, 190].map((dx, i) => `
                    <g class="reindeer" transform="translate(${dx} 0)">
                        <path class="legs" d="M-7 44L-12 54M-4 45L-6 55M5 45L9 54M8 44L14 52" stroke="#6b4423" stroke-width="2" stroke-linecap="round"/>
                        <ellipse cx="0" cy="40" rx="11" ry="6" fill="#8b5a2b"/>
                        <path d="M-10 37L-14 34" stroke="#f4e6d0" stroke-width="2.5" stroke-linecap="round"/>
                        <path d="M8 38L13 30" stroke="#8b5a2b" stroke-width="4.5" stroke-linecap="round"/>
                        <ellipse cx="15.5" cy="28" rx="4.8" ry="3.2" fill="#8b5a2b"/>
                        <path d="M13 25L11 17M11 21L8 18M15.5 25L18 17M17 21L20.5 19" stroke="#c9a26b" stroke-width="1.3" stroke-linecap="round"/>
                        <circle cx="16" cy="26.8" r="0.8" fill="#111"/>
                        ${i === 3 ? '<circle class="rudolph" cx="20.3" cy="28.6" r="2.4" fill="#ff2a2a"/>'
                                  : '<circle cx="20" cy="28.6" r="1.4" fill="#3a2a1a"/>'}
                    </g>`).join('')}
                <ellipse cx="22" cy="22" rx="15" ry="12" fill="#8b5a2b"/>
                <rect x="14" y="8" width="8" height="8" fill="#3b9dff"/><rect x="22" y="6" width="7" height="10" fill="#3bd16f"/>
                <rect x="17" y="10" width="2" height="6" fill="#ffd23f"/>
                <circle cx="48" cy="24" r="10" fill="#d62828"/>
                <rect x="38" y="25" width="20" height="3" fill="#222"/>
                <path d="M57 22L66 30" stroke="#d62828" stroke-width="4" stroke-linecap="round"/>
                <circle cx="51" cy="10" r="6" fill="#f1c6a5"/>
                <path d="M45 11C45 20 57 20 57 11C54 14 48 14 45 11Z" fill="#ffffff"/>
                <path d="M45 8C45 0 53 -2 58 4L54 6Z" fill="#d62828"/>
                <rect x="44" y="6" width="13" height="3" rx="1.5" fill="#ffffff"/>
                <circle cx="59" cy="4" r="2.2" fill="#ffffff"/>
                <circle cx="53" cy="9.5" r="0.9" fill="#222"/>
                <path d="M3 30H70C79 30 81 40 75 48H20C9 48 3 40 3 30Z" fill="#c1121f"/>
                <path d="M10 36H68" stroke="#d4af37" stroke-width="1.5"/>
                <path d="M14 48V56M64 48V56M6 56H72C80 56 82 50 78 45" fill="none" stroke="#d4af37" stroke-width="2.5" stroke-linecap="round"/>
            </svg>
        </div>`);
    const duration = rand(9000, 13000);
    const flight = fly(sleigh, {
        y: rand(0.06, 0.32) * innerHeight, size: rand(0.8, 1.1), duration,
        waves: 1.5, amp: rand(10, 20), arc: rand(20, 50), tilt: -3
    });
    says(sleigh, pick(SANTA_SAYS), { delay: rand(1500, 4000), flipped: flight.leftward });
    if (chance(0.75)) {
        const t = rand(0.25, 0.75);
        wait(duration * t).then(() => dropPresent(flight.at(t)));
    }
}

if (!calm) {
    every(SANTA_EVERY, santa, rand(3000, 6000));
    every(GUST_EVERY, async () => {
        const gust = rand(25, 70) * (chance(0.5) ? -1 : 1);
        await tween(0, gust, 1500, v => { snow.wind = v; });
        await wait(rand(1500, 3500));
        await tween(gust, 0, 2500, v => { snow.wind = v; });
    });
}
