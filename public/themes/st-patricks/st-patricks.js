// St. Patrick's Day: rolling green hills under a clear sky, shamrocks tumbling down (with
// the odd golden four-leaf clover), a rainbow ending in a pot of gold that spills coins
// when clicked, and a leprechaun hopping and jigging along the logo box, who throws
// coins of his own when caught.
// Loaded by themes/themes.js, which also loads st-patricks.css.
import {
    calm, rand, pick, chance, wait, napper, says, make, logoBox,
    sky, clouds, ground, weather, percher, costume, tools, GOLD
} from '../kit.js';

// A leprechaun's hat and an orange beard
costume({
    tool: tools.spade({ handle: '#2e8b57', metal: GOLD }),
    hat: `<ellipse cx="1" cy="-14" rx="23" ry="4.5" fill="#1f5f2e" transform="rotate(6 1 -14)"/>
        <path d="M-12 -15L-9 -44H11L14 -13Z" fill="#2e8b57"/>
        <path d="M-11.4 -21L13.4 -19.5L13.8 -14.5L-12 -16Z" fill="#1b1b1b"/>
        <rect x="-1" y="-23" width="8" height="7" fill="none" stroke="#ffd23f" stroke-width="1.8"/>
        <g fill="#3bb143" transform="translate(-6 -32)">
            <circle cx="0" cy="-3" r="2.6"/><circle cx="-3" cy="1" r="2.6"/><circle cx="3" cy="1" r="2.6"/>
        </g>`,
    over: '<path d="M112 58C112 82 138 84 145 66C138 72 122 72 112 58Z" fill="#e2711d"/>',
    label: 'Stick figure in a leprechaun hat and orange beard digging with a golden spade'
});

const LEPRECHAUN_SAYS = ['Top o’ the mornin’!', 'Ye’ll never find me gold!', 'Sláinte!', 'Lucky you!', 'Kiss me, I’m Irish!'];
const CAUGHT_SAYS = ['Oi! Put me down!', 'Here, take a coin and go!', 'Ye caught me!'];
const POT_SAYS = ['Oi! That’s me gold!', 'Hands off!', 'Thief! Thief!'];
const SHAMROCK_AREA = 11000;                  // px² of window per shamrock
const MAX_COINS = 40;                         // on the page at once

// Clear sky with a few white clouds
const lucky = sky('lucky-sky');
clouds(lucky, {
    count: 5, top: [2, 35],
    light: [[255, 255, 255], [246, 250, 255]], dark: [[222, 236, 246], [205, 224, 238]]
});

// The field: a rainbow coming down behind rolling hills
const hills = (y, colour, seed) => {
    let d = `M0 120V${y}`;
    for (let x = 0; x <= 1000; x += 125) d += `Q${x + 62} ${y - 30 - (seed * (x + 7)) % 26} ${x + 125} ${y}`;
    return `<path d="${d}V120Z" fill="${colour}"/>`;
};
const field = ground('clover-field', `
    <svg class="rainbow" viewBox="0 0 400 200" preserveAspectRatio="none">
        ${['#ff4d4d', '#ff9f1c', '#ffd23f', '#3bd16f', '#4cc9f0', '#5b5bd6', '#9b5de5'].map((c, i) =>
            `<path d="M${10 + i * 9} 200A${190 - i * 9} ${190 - i * 9} 0 0 1 ${390 - i * 9} 200" fill="none" stroke="${c}" stroke-width="9.5"/>`).join('')}
    </svg>
    <svg class="hills" viewBox="0 0 1000 120" preserveAspectRatio="none">
        ${hills(70, '#4f9d3a', 7)}${hills(96, '#5fb548', 13)}
    </svg>`);

// The pot of gold at the rainbow's end
let glints = '';
for (let i = 0; i < 4; i++) {
    glints += `<path class="glint" style="animation-delay: ${-rand(0, 2).toFixed(1)}s"
        d="M${rand(16, 44).toFixed(0)} ${rand(4, 14).toFixed(0)}m0 -4l1 3 3 1 -3 1 -1 3 -1 -3 -3 -1 3 -1Z" fill="#ffffff"/>`;
}
const pot = make(`
    <button class="pot" type="button" aria-label="A pot of gold. Help yourself.">
        <svg viewBox="0 0 60 50" aria-hidden="true">
            <ellipse cx="30" cy="14" rx="22" ry="7" fill="#ffd23f"/>
            <circle cx="20" cy="11" r="5" fill="#ffc300" stroke="#d4a017"/>
            <circle cx="31" cy="8" r="5" fill="#ffd23f" stroke="#d4a017"/>
            <circle cx="40" cy="12" r="5" fill="#ffc300" stroke="#d4a017"/>
            <circle cx="27" cy="15" r="5" fill="#ffdd57" stroke="#d4a017"/>
            <path d="M6 16C4 30 10 44 18 46H42C50 44 56 30 54 16C46 22 14 22 6 16Z" fill="#1f1f1f"/>
            <ellipse cx="30" cy="16" rx="25" ry="4.5" fill="none" stroke="#2e2e2e" stroke-width="3"/>
            <path d="M14 46L12 50M46 46L48 50" stroke="#1f1f1f" stroke-width="3"/>
            ${glints}
        </svg>
    </button>`);
field.pin(pot, -36);

// Shamrocks, tumbling down; now and then a golden four-leaf clover
function drawClover(pen, x, y, rot, size, squash, colour, leaves, alpha = 1) {
    pen.save();
    pen.globalAlpha = alpha;
    pen.translate(x, y);
    pen.rotate(rot);
    pen.scale(size, size * squash);
    pen.fillStyle = colour;
    for (let i = 0; i < leaves; i++) {
        pen.save();
        pen.rotate(i * Math.PI * 2 / leaves);
        pen.beginPath();
        pen.moveTo(0, 0);
        pen.bezierCurveTo(-6, -4, -6, -10, -2.5, -10);
        pen.bezierCurveTo(-1, -10, 0, -9, 0, -7.5);
        pen.bezierCurveTo(0, -9, 1, -10, 2.5, -10);
        pen.bezierCurveTo(6, -10, 6, -4, 0, 0);
        pen.fill();
        pen.restore();
    }
    pen.strokeStyle = colour;
    pen.lineWidth = 1.4;
    pen.beginPath();
    pen.moveTo(0, 0);
    pen.quadraticCurveTo(2, 6, 1, 11);
    pen.stroke();
    pen.restore();
}

weather({
    ground: field, area: SHAMROCK_AREA, max: 150,
    spawn: anywhere => {
        const golden = chance(0.03);
        return {
            x: rand(-80, innerWidth), y: anywhere ? rand(-innerHeight, innerHeight) : rand(-40, -10),
            size: rand(0.6, 1.2), rot: rand(0, 6.3), spin: rand(-2, 2), vy: rand(35, 65), sway: rand(15, 45),
            phase: rand(0, 6.3), tumble: rand(0, 6.3), t: 0, leaves: golden ? 4 : 3,
            colour: golden ? '#ffc300' : pick(['#2e8b57', '#3bb143', '#228b22', '#4caf50'])
        };
    },
    step(s, dt, w) {
        s.t += dt;
        s.y += s.vy * dt;
        s.x += (Math.sin(s.t * 1.3 + s.phase) * s.sway + w.wind) * dt;
        s.rot += s.spin * dt;
        s.tumble += dt * 2.2;
    },
    draw: (pen, s) => drawClover(pen, s.x, s.y, s.rot, s.size, Math.cos(s.tumble), s.colour, s.leaves),
    splash: s => ({
        life: 5, draw: (pen, k) => drawClover(pen, s.x, s.land, s.rot, s.size, 0.5, s.colour, s.leaves, k < 0.7 ? 1 : (1 - k) / 0.3)
    })
});

// Gold coins: thrown from (x, y) in the page, arcing out and bouncing where they land
function throwCoins(x, y, count, floor) {
    for (let i = 0; i < count; i++) {
        if (document.querySelectorAll('.coin').length >= MAX_COINS) return;
        const coin = make(`
            <svg class="coin" viewBox="0 0 16 16" aria-hidden="true">
                <circle cx="8" cy="8" r="7" fill="#ffc300" stroke="#d4a017" stroke-width="1.5"/>
                <path d="M8 4.5V11.5M6 6.5H10" stroke="#d4a017" stroke-width="1.2"/>
            </svg>`);
        document.body.appendChild(coin);
        const x1 = x + rand(-160, 160), y1 = floor() + rand(4, 50), peak = Math.min(y, y1) - rand(60, 160);
        coin.style.transform = `translate(${x1}px, ${y1}px)`;
        coin.animate([
            { transform: `translate(${x}px, ${y}px) rotateY(0)`, easing: 'ease-out' },
            { transform: `translate(${(x + x1) / 2}px, ${peak}px) rotateY(540deg)`, easing: 'ease-in', offset: 0.45 },
            { transform: `translate(${x1}px, ${y1}px) rotateY(1080deg)`, easing: 'ease-out', offset: 0.85 },
            { transform: `translate(${x1}px, ${y1 - 10}px) rotateY(1200deg)`, easing: 'ease-in', offset: 0.93 },
            { transform: `translate(${x1}px, ${y1}px) rotateY(1260deg)` }
        ], { duration: rand(1100, 1600), delay: i * 40, fill: 'backwards' })
            .finished.then(() => wait(4000))
            .then(() => coin.animate([{ opacity: 1 }, { opacity: 0 }], 800).finished)
            .then(() => coin.remove());
    }
}

const onGround = () => field.top;

pot.addEventListener('click', () => {
    const r = pot.getBoundingClientRect();
    throwCoins(r.left + r.width / 2 - 8, r.top + scrollY, calm ? 0 : 12, onGround);
    pot.animate([{ transform: 'rotate(0)' }, { transform: 'rotate(-8deg)' }, { transform: 'rotate(6deg)' }, { transform: 'rotate(0)' }], 400);
    lepSays(pick(POT_SAYS));
    stomp = true;
    wake();
});

// The leprechaun: hops about the top of the box and breaks into a jig now and then.
// Catch him (click) and he leaps, throwing a handful of coins.
logoBox.insertAdjacentHTML('beforeend', `
    <button class="leprechaun" type="button" aria-label="A leprechaun. Catch him.">
        <svg viewBox="0 0 44 62" aria-hidden="true">
            <path d="M15 52L13 60H20L20 52M25 52L25 60H33L30 52" fill="#1f5f2e"/>
            <path d="M11 60H21V62H9C9 61 10 60 11 60ZM24 60H34C35 60 36 61 36 62H24Z" fill="#1b1b1b"/>
            <path d="M12 30C10 40 11 48 14 53H31C34 48 35 40 33 30C28 27 17 27 12 30Z" fill="#2e8b57"/>
            <rect x="12" y="43" width="21" height="3.5" fill="#1b1b1b"/>
            <rect x="20" y="42.5" width="5" height="4.5" fill="none" stroke="#ffd23f" stroke-width="1.2"/>
            <path d="M14 33L6 40M31 33L38 27" stroke="#2e8b57" stroke-width="4.5" stroke-linecap="round"/>
            <circle cx="5.5" cy="40.5" r="2.4" fill="#f1c6a5"/><circle cx="38.5" cy="26.5" r="2.4" fill="#f1c6a5"/>
            <circle cx="22.5" cy="20" r="8" fill="#f1c6a5"/>
            <path d="M14 20C14 33 31 33 31 20C28 25 17 25 14 20Z" fill="#e2711d"/>
            <circle cx="20" cy="18" r="1" fill="#111"/><circle cx="26" cy="18" r="1" fill="#111"/>
            <ellipse cx="23" cy="21" rx="1.6" ry="1.2" fill="#e8a080"/>
            <rect x="11" y="10" width="23" height="2.8" rx="1" fill="#1f5f2e"/>
            <path d="M14 11L16 -2H29L31 11Z" fill="#2e8b57"/>
            <rect x="15" y="7" width="15" height="3" fill="#1b1b1b"/>
            <rect x="20" y="6.5" width="5" height="4" fill="none" stroke="#ffd23f" stroke-width="1.2"/>
        </svg>
    </button>`);
const leprechaun = logoBox.querySelector('.leprechaun');
const lep = percher(leprechaun, { lift: 18, stepMs: 280, stride: [20, 40] });
const { nap, wake } = napper();
let caught = false, stomp = false;
lep.x = rand(0, lep.room());
lep.place();

function lepSays(text) {
    leprechaun.querySelectorAll('.kit-says').forEach(b => b.remove());
    says(leprechaun, text, { duration: 2400 });
}

async function jig(beats = 6) {
    for (let i = 0; i < beats; i++) {
        lep.face(i % 2 === 1);
        await lep.step(lep.x, 0, 200);
    }
}

leprechaun.addEventListener('click', () => {
    if (caught) return;
    caught = true;
    wake();
});

async function leprechaunLife() {
    for (;;) {
        if (caught) {
            lepSays(pick(CAUGHT_SAYS));
            await lep.step(lep.x, -50, 500);
            const r = leprechaun.getBoundingClientRect();
            throwCoins(r.left + r.width / 2 - 8, r.top + scrollY, 8, onGround);
            await lep.step(lep.x, 0, 400);
            await lep.goTo(lep.x < lep.room() / 2 ? lep.room() : 0, 0, { ms: 180, pause: [0, 30] });
            caught = false;
            await nap(rand(1500, 3000));
            continue;
        }
        if (stomp) {
            stomp = false;
            await jig(8);
            continue;
        }
        const r = Math.random();
        if (r < 0.5) await lep.goTo(rand(0, lep.room()));
        else if (r < 0.75) await jig(Math.round(rand(4, 9)));
        else lepSays(pick(LEPRECHAUN_SAYS));
        await nap(rand(700, 2600));
    }
}

if (!calm) leprechaunLife();
