// Spring: a bright sky over a meadow of flowers, blossom petals drifting down, butterflies
// and songbirds, and now and then a passing shower followed by a rainbow. A flowering
// vine grows over the logo box, with a bee buzzing about it that zips off when touched.
// Loaded by themes/themes.js, which also loads spring.css.
import {
    calm, rand, pick, chance, wait, every, tween, says, make, logoBox,
    sky, clouds, ground, weather, fly, costume, tools
} from '../kit.js';

// A straw sun hat with a flower in its band
costume({
    tool: tools.hoe({ handle: '#a0522d' }),
    hat: `<ellipse cx="0" cy="-13" rx="28" ry="5" fill="#e9c46a" transform="rotate(5 0 -13)"/>
        <path d="M-13 -14C-13 -33 13 -33 13 -14Z" fill="#f4d58d"/>
        <path d="M-13 -18Q0 -16 13 -17L13 -13.5Q0 -12.5 -13 -14.5Z" fill="#ff6b9a"/>
        ${[0, 72, 144, 216, 288].map(a => `<ellipse cx="-9" cy="-20.5" rx="2.2" ry="3.2" fill="#ffffff" transform="rotate(${a} -9 -17)"/>`).join('')}
        <circle cx="-9" cy="-17" r="2" fill="#ffd23f"/>`,
    label: 'Stick figure in a straw sun hat working the ground with a hoe'
});

const BUTTERFLY_EVERY = [3000, 7000];         // ms between butterflies
const BIRDS_EVERY = [10000, 20000];           // ms between pairs of songbirds
const SHOWER_EVERY = [30000, 50000];          // ms between showers
const PETAL_AREA = 14000;                     // px² of window per blossom petal
const FLOWERS = 46;
const FLOWER_COLOURS = ['#ff6b9a', '#ffd23f', '#b388ff', '#ff8c42', '#ffffff', '#4cc9f0', '#ff4d6d'];

// Bright sky, a sun with turning rays, white clouds, and a rainbow for after the rain
const spring = sky('spring-sky');
let rays = '';
for (let i = 0; i < 12; i++) rays += `<path d="M50 50L${(50 + 6).toFixed(0)} 4L${(50 - 6).toFixed(0)} 4Z" transform="rotate(${i * 30} 50 50)"/>`;
spring.insertAdjacentHTML('beforeend', `
    <svg class="spring-sun" viewBox="0 0 100 100">
        <g class="rays" fill="#ffe680">${rays}</g>
        <circle cx="50" cy="50" r="24" fill="#ffd23f"/>
    </svg>
    <svg class="rainbow" viewBox="0 0 400 200" preserveAspectRatio="none">
        ${['#ff4d4d', '#ff9f1c', '#ffd23f', '#3bd16f', '#4cc9f0', '#5b5bd6', '#9b5de5'].map((c, i) =>
            `<path d="M${10 + i * 9} 200A${190 - i * 9} ${190 - i * 9} 0 0 1 ${390 - i * 9} 200" fill="none" stroke="${c}" stroke-width="9.5"/>`).join('')}
    </svg>`);
clouds(spring, {
    count: 7, top: [2, 40],
    light: [[255, 255, 255], [248, 251, 255]], dark: [[226, 236, 247], [208, 222, 238]]
});

// The meadow, with flowers growing along its edge and swaying
const flower = (colour, kind) => kind === 'tulip'
    ? `<path d="M10 40V14" stroke="#3f8a34" stroke-width="1.8"/>
       <path d="M10 30C6 26 4 22 5 18C8 21 9 25 10 30Z" fill="#4e9a36"/>
       <path d="M4 8C4 16 16 16 16 8L13 11L10 6L7 11Z" fill="${colour}"/>`
    : `<path d="M10 40V12" stroke="#3f8a34" stroke-width="1.6"/>
       <path d="M10 28C14 24 17 23 19 24C17 27 14 28 10 28Z" fill="#4e9a36"/>
       ${[0, 72, 144, 216, 288].map(a => `<ellipse cx="10" cy="5" rx="3.2" ry="4.6" fill="${colour}" transform="rotate(${a} 10 10)"/>`).join('')}
       <circle cx="10" cy="10" r="2.6" fill="${colour === '#ffd23f' ? '#c0392b' : '#ffd23f'}"/>`;
let blooms = '';
for (let i = 0; i < FLOWERS; i++) {
    const height = rand(30, 56);
    blooms += `<svg class="flower" viewBox="0 0 20 40" style="left: ${rand(0, 98).toFixed(1)}%; width: ${(height / 2).toFixed(0)}px; height: ${height.toFixed(0)}px;
        --grow: ${rand(0, 2.5).toFixed(2)}s; --sway: ${rand(2.5, 4.5).toFixed(2)}s">${flower(pick(FLOWER_COLOURS), chance(0.35) ? 'tulip' : 'daisy')}</svg>`;
}
const meadow = ground('meadow', blooms);

// Blossom petals, tumbling down and resting a moment where they land
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
    ground: meadow, area: PETAL_AREA, max: 120,
    spawn: anywhere => ({
        x: rand(-80, innerWidth), y: anywhere ? rand(-innerHeight, innerHeight) : rand(-40, -10),
        size: rand(0.7, 1.4), rot: rand(0, 6.3), spin: rand(-2, 2), vy: rand(25, 50), sway: rand(25, 55),
        phase: rand(0, 6.3), tumble: rand(0, 6.3), colour: pick(['#ffc2d6', '#ffd6e5', '#ffb3cc', '#fff0f5']), t: 0
    }),
    step(p, dt, w) {
        p.t += dt;
        p.y += p.vy * dt;
        p.x += (Math.sin(p.t * 1.2 + p.phase) * p.sway + 12 + w.wind) * dt;
        p.rot += p.spin * dt;
        p.tumble += dt * 2.5;
    },
    draw: (pen, p) => drawPetal(pen, p.x, p.y, p.rot, p.size, Math.cos(p.tumble), p.colour),
    splash: p => ({ life: 5, draw: (pen, k) => drawPetal(pen, p.x, p.land, p.rot, p.size, 0.5, p.colour, k < 0.7 ? 1 : (1 - k) / 0.3) })
});

// Showers: the sky greys over, rain falls for a while, then a rainbow comes out
const rain = weather({
    ground: meadow, area: 6000, density: 0,
    spawn: anywhere => ({
        x: rand(-40, innerWidth), y: anywhere ? rand(-innerHeight, innerHeight) : rand(-80, -10),
        len: rand(10, 18), speed: rand(600, 850)
    }),
    step(d, dt) {
        d.y += d.speed * dt;
        d.x += d.speed * 0.08 * dt;
    },
    draw(pen, d) {
        pen.strokeStyle = 'rgba(90, 120, 170, 0.5)';
        pen.lineWidth = 1;
        pen.beginPath();
        pen.moveTo(d.x, d.y);
        pen.lineTo(d.x - d.len * 0.08, d.y - d.len);
        pen.stroke();
    },
    splash: d => ({
        life: 0.25,
        draw(pen, k) {
            pen.strokeStyle = `rgba(90, 120, 170, ${(0.5 * (1 - k)).toFixed(2)})`;
            pen.beginPath();
            pen.ellipse(d.x, d.land, 2 + k * 6, 0.8 + k * 1.6, 0, Math.PI, 0);
            pen.stroke();
        }
    })
});

async function shower() {
    spring.classList.add('shower');
    await tween(0, 1, 2500, v => { rain.density = v; });
    await wait(rand(6000, 9000));
    await tween(1, 0, 3000, v => { rain.density = v; });
    spring.classList.remove('shower');
    await wait(1500);
    spring.classList.add('rainbow-out');
    await wait(9000);
    spring.classList.remove('rainbow-out');
}

// Butterflies flutter by on loopy paths, each in its own colours
function butterfly() {
    const hue = rand(0, 360);
    const flutterer = make(`
        <div class="butterfly" style="--wing: hsl(${hue.toFixed(0)} 85% 62%); --spot: hsl(${((hue + 160) % 360).toFixed(0)} 80% 45%)">
            <svg viewBox="0 0 40 30">
                <g class="wing-l">
                    <path d="M20 15C12 2 2 2 4 12C5 16 12 17 20 15Z" fill="var(--wing)"/>
                    <path d="M20 16C12 18 6 26 11 27C15 28 18 22 20 16Z" fill="var(--wing)"/>
                    <circle cx="10" cy="10" r="2.2" fill="var(--spot)"/>
                </g>
                <g class="wing-r">
                    <path d="M20 15C28 2 38 2 36 12C35 16 28 17 20 15Z" fill="var(--wing)"/>
                    <path d="M20 16C28 18 34 26 29 27C25 28 22 22 20 16Z" fill="var(--wing)"/>
                    <circle cx="30" cy="10" r="2.2" fill="var(--spot)"/>
                </g>
                <ellipse cx="20" cy="16" rx="1.4" ry="7" fill="#2b2b2b"/>
                <path d="M20 9L17 3M20 9L23 3" stroke="#2b2b2b" stroke-width="0.8" fill="none"/>
            </svg>
        </div>`);
    fly(flutterer, {
        y: rand(0.15, 0.7) * innerHeight, size: rand(0.5, 0.95), duration: rand(9000, 15000),
        waves: rand(3, 6), amp: rand(25, 60), climb: rand(-120, 60), flip: false
    });
}

// Songbirds, in pairs, with a tweet
const SONGBIRD = colour => `
    <svg viewBox="0 0 44 26">
        <path d="M8 14L0 10L2 16Z" fill="${colour}"/>
        <ellipse cx="18" cy="15" rx="11" ry="6" fill="${colour}"/>
        <ellipse cx="20" cy="18" rx="7" ry="3.5" fill="#f4a261"/>
        <circle cx="30" cy="11" r="5" fill="${colour}"/>
        <circle cx="31.5" cy="10" r="0.9" fill="#111"/>
        <path d="M34.5 10.5L39 11.5L34.5 12.5Z" fill="#e9c46a"/>
        <path class="wing" d="M14 13L22 1L24 13Z" fill="${colour}"/>
    </svg>`;
function songbirds() {
    const leftward = chance(0.5), y = rand(0.08, 0.4) * innerHeight, colour = pick(['#3a86ff', '#2a9d8f', '#577590']);
    for (let i = 0; i < 2; i++) {
        const bird = make(`<div class="songbird">${SONGBIRD(colour)}</div>`);
        fly(bird, { leftward, y: y + i * 26, size: rand(0.75, 0.95), duration: rand(7000, 10000), waves: 3, amp: 14, delay: i * 450 });
        if (i === 0 && chance(0.7)) says(bird, pick(['Tweet!', 'Tweet tweet!', 'Chirp!']), { delay: rand(1500, 3500), duration: 1600, flipped: leftward });
    }
}

// A vine grows up the left side of the logo box and along its top, leafing and blooming
logoBox.insertAdjacentHTML('beforeend', '<svg class="vine" aria-hidden="true"></svg>');
const vine = logoBox.querySelector('.vine');

function growVine() {
    const w = logoBox.clientWidth, h = logoBox.clientHeight, reach = w * 0.62;
    vine.setAttribute('viewBox', `-20 -20 ${w + 40} ${h + 40}`);
    let d = `M2 ${h}`, side = 1;
    for (let y = h; y > 10; y -= 26) d += `Q${(side = -side) * 11} ${y - 13} 2 ${Math.max(2, y - 26)}`;
    for (let x = 2; x < reach; x += 30) d += `Q${x + 15} ${(side = -side) * 9} ${x + 30} 2`;
    vine.innerHTML = `<path class="stem" d="${d}" fill="none" stroke="#3f8a34" stroke-width="2.2" stroke-linecap="round"/>`;
    const stem = vine.querySelector('.stem'), length = stem.getTotalLength();
    stem.style.setProperty('--length', length);
    let extras = '';
    for (let at = 18, i = 0; at < length; at += rand(16, 26), i++) {
        const p = stem.getPointAtLength(at), delay = (at / length * 3).toFixed(2);
        if (i % 3 === 2) {
            const colour = pick(['#ff6b9a', '#ffffff', '#ffd23f', '#b388ff']);
            extras += `<g class="blossom" style="--at: ${delay}s; transform-origin: ${p.x.toFixed(1)}px ${p.y.toFixed(1)}px">
                ${[0, 72, 144, 216, 288].map(a => `<ellipse cx="${p.x.toFixed(1)}" cy="${(p.y - 3.5).toFixed(1)}" rx="2.4" ry="3.4" fill="${colour}" transform="rotate(${a} ${p.x.toFixed(1)} ${p.y.toFixed(1)})"/>`).join('')}
                <circle cx="${p.x.toFixed(1)}" cy="${p.y.toFixed(1)}" r="1.8" fill="#ffb703"/></g>`;
        } else {
            const angle = rand(-60, 60) + (i % 2 ? 180 : 0);
            extras += `<path class="leaf" style="--at: ${delay}s; transform-origin: ${p.x.toFixed(1)}px ${p.y.toFixed(1)}px"
                d="M${p.x.toFixed(1)} ${p.y.toFixed(1)}c4 -6 12 -6 14 0c-4 4 -10 4 -14 0Z" fill="#4e9a36"
                transform="rotate(${angle.toFixed(0)})"/>`;   // turns about transform-origin, the point on the stem
        }
    }
    vine.insertAdjacentHTML('beforeend', extras);
}
growVine();
let regrow;
new ResizeObserver(() => { clearTimeout(regrow); regrow = setTimeout(growVine, 200); }).observe(logoBox);

// A bee buzzing from spot to spot around the vine. Touch it and it zips away for a while.
logoBox.insertAdjacentHTML('beforeend', `
    <div class="bee" aria-hidden="true">
        <svg viewBox="0 0 26 20">
            <ellipse class="bee-wing" cx="11" cy="6" rx="5" ry="3.5" fill="rgba(220, 240, 255, 0.85)"/>
            <ellipse class="bee-wing" cx="16" cy="6" rx="5" ry="3.5" fill="rgba(220, 240, 255, 0.85)"/>
            <ellipse cx="13" cy="12" rx="8" ry="5.5" fill="#ffd23f"/>
            <path d="M10 7.2V16.8M14 6.6V17.4" stroke="#222" stroke-width="2.2"/>
            <circle cx="21" cy="11" r="3.2" fill="#222"/>
            <path d="M5 12L1 12" stroke="#222" stroke-width="1.4"/>
        </svg>
    </div>`);
const bee = logoBox.querySelector('.bee');
let beeAt = { x: 20, y: -40 }, beeAway = false;

function buzzTo(x, y, ms) {
    const from = beeAt;
    bee.classList.toggle('left', x < from.x);
    beeAt = { x, y };
    bee.style.transform = `translate(${x}px, ${y}px)`;
    return bee.animate([
        { transform: `translate(${from.x}px, ${from.y}px)` },
        { transform: `translate(${(x + from.x) / 2 + rand(-20, 20)}px, ${(y + from.y) / 2 + rand(-25, 25)}px)` },
        { transform: bee.style.transform }
    ], { duration: ms, easing: 'ease-in-out' }).finished;
}

function zipAway() {
    if (beeAway) return;
    beeAway = true;
    bee.getAnimations().forEach(a => a.cancel());
    buzzTo(beeAt.x + (chance(0.5) ? -1 : 1) * innerWidth, -innerHeight * 0.5, 700)
        .then(() => wait(rand(5000, 9000)))
        .then(() => { beeAway = false; });
}
bee.addEventListener('pointerenter', zipAway);
bee.addEventListener('pointerdown', zipAway);

async function beeLife() {
    for (;;) {
        if (beeAway) {
            await wait(500);
            continue;
        }
        const w = logoBox.clientWidth, h = logoBox.clientHeight;
        const spot = chance(0.5)
            ? { x: rand(-24, w * 0.62), y: rand(-34, -10) }          // along the top
            : { x: rand(-30, -8), y: rand(0, h - 10) };              // down the left side
        await buzzTo(spot.x, spot.y, rand(700, 1500)).catch(() => {});
        if (!beeAway) await wait(rand(200, 1400));
    }
}

if (!calm) {
    every(BUTTERFLY_EVERY, () => { butterfly(); if (chance(0.3)) butterfly(); }, 1500);
    every(BIRDS_EVERY, songbirds, rand(4000, 8000));
    every(SHOWER_EVERY, shower, rand(15000, 25000));
    beeLife();
}
