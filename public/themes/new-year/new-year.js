// New Year: fireworks bursting over a city skyline at night, confetti falling, and a
// "Happy New Year" bunting strung over the logo box between two poles, one wearing a
// party hat that throws confetti when clicked. On New Year's Eve a countdown runs to
// midnight, which sets off a finale. Click the sky to launch a firework.
// Loaded by themes/themes.js, which also loads new-year.css.
import {
    calm, rand, pick, chance, wait, every, says, make, logoBox,
    sky, stars, ground, weather, backdrop, costume, tools, GOLD
} from '../kit.js';

// A party hat, and a party blower
costume({
    tool: tools.jackhammer({ metal: GOLD }),
    hat: `<g transform="rotate(-14)">
            <path d="M-11 -13L11 -13L0 -49Z" fill="#7b2cbf"/>
            <path d="M-7.5 -24H7.5M-4 -36H4" stroke="#ffd23f" stroke-width="3"/>
            <circle cx="0" cy="-50" r="4" fill="#ff4d6d"/>
        </g>`,
    over: '<path d="M114 64L96 70" stroke="#ff9f1c" stroke-width="4" stroke-linecap="round"/><path d="M97 70L87 75L89 64Z" fill="#ffd23f"/>',
    label: 'Stick figure in a party hat, with a party blower, operating a gold jackhammer'
});

const HAT_SAYS = ['Happy New Year!', 'Toot toot!', 'Cheers!', 'New year, new projects!', 'Auld lang syne!'];
const LAUNCH_EVERY = [700, 2000];             // ms between launches of one to three rockets
const FINALE_EVERY = [28000, 42000];          // ms between finales
const CONFETTI_AREA = 9000;                   // px² of window per piece of confetti
const COLOURS = ['#ff4d6d', '#ffd23f', '#3bd16f', '#4cc9f0', '#b388ff', '#ff9f1c', '#ffffff'];

const now = new Date();
const year = now.getMonth() === 11 ? now.getFullYear() + 1 : now.getFullYear();

// Night sky and a city skyline with lit windows along the horizon
const night = sky('new-year-sky');
stars(night, { count: 120, depth: 55 });
const plaza = ground('plaza', '<svg class="skyline" aria-hidden="true"></svg>');
const skyline = plaza.el.querySelector('.skyline');

function buildSkyline() {
    const width = innerWidth, height = 170;
    skyline.setAttribute('viewBox', `0 0 ${width} ${height}`);
    let out = '';
    for (let x = -10; x < width; ) {
        const w = rand(34, 92), h = rand(40, height - 15), top = height - h;
        const shade = pick(['#151530', '#1b1b3a', '#202046', '#17172e']);
        out += `<rect x="${x.toFixed(0)}" y="${top.toFixed(0)}" width="${w.toFixed(0)}" height="${h.toFixed(0)}" fill="${shade}"/>`;
        if (h > 110 && chance(0.5)) {
            out += `<path d="M${(x + w / 2).toFixed(0)} ${top.toFixed(0)}V${(top - 18).toFixed(0)}" stroke="${shade}" stroke-width="2"/>
                <circle class="beacon" cx="${(x + w / 2).toFixed(0)}" cy="${(top - 19).toFixed(0)}" r="2" fill="#ff4d4d"/>`;
        }
        for (let wy = top + 8; wy < height - 8; wy += 11) {
            for (let wx = x + 6; wx < x + w - 8; wx += 9) {
                if (chance(0.32)) {
                    const flicker = chance(0.08) ? ` class="flicker" style="animation-delay: ${-rand(0, 6).toFixed(1)}s"` : '';
                    out += `<rect x="${wx.toFixed(0)}" y="${wy.toFixed(0)}" width="4" height="5" fill="#ffd86b"${flicker}/>`;
                }
            }
        }
        x += w + rand(-6, 4);
    }
    skyline.innerHTML = out;
}
buildSkyline();
let resizing;
addEventListener('resize', () => { clearTimeout(resizing); resizing = setTimeout(buildSkyline, 200); });

// Confetti: tumbling scraps of colour, which also bursts out of the party hat
const confetti = weather({
    ground: plaza, area: CONFETTI_AREA, max: 220,
    spawn: anywhere => piece(rand(-40, innerWidth + 40), anywhere ? rand(-innerHeight, innerHeight) : rand(-40, -10), 0, rand(40, 90)),
    step(c, dt, w) {
        c.t += dt;
        c.vy = Math.min(c.vy + 320 * dt, c.fall);
        c.vx *= 1 - 1.6 * dt;
        c.x += (c.vx + Math.sin(c.t * 2 + c.phase) * c.sway + w.wind) * dt;
        c.y += c.vy * dt;
        c.rot += c.spin * dt;
        c.tumble += dt * 6;
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

function piece(x, y, vx, vy) {
    return {
        x, y, vx, vy, fall: rand(50, 100), t: 0, phase: rand(0, 6.3), sway: rand(10, 30),
        rot: rand(0, 6.3), spin: rand(-4, 4), tumble: rand(0, 6.3),
        w: rand(4, 8), h: rand(2.5, 4), colour: pick(COLOURS)
    };
}

function burst(x, y, count = 60) {
    for (let i = 0; i < count; i++) confetti.add(piece(x, y, rand(-260, 260), -rand(180, 460)));
}

// Fireworks, on their own canvas behind the skyline. Each frame fades what is already
// drawn a little, which leaves trails behind the sparks.
const sparkCanvas = document.createElement('canvas');
sparkCanvas.className = 'kit-weather';
backdrop(sparkCanvas, 'skyWeather');
const pen = sparkCanvas.getContext('2d');
const GRAVITY = 220;
let rockets = [], sparks = [];

function sizeSparks() {
    const dpr = devicePixelRatio || 1;
    sparkCanvas.width = innerWidth * dpr;
    sparkCanvas.height = innerHeight * dpr;
    pen.setTransform(dpr, 0, 0, dpr, 0, 0);
}
sizeSparks();
addEventListener('resize', sizeSparks);

const horizon = () => Math.min(innerHeight, plaza.top - scrollY);

// A rocket from the skyline that bursts at (tx, ty)
function launch(tx = rand(0.1, 0.9) * innerWidth, ty = rand(0.08, 0.45) * innerHeight) {
    const y0 = horizon();
    if (ty > y0 - 60) return;
    const x0 = tx + rand(-80, 80), vy = -Math.sqrt(2 * GRAVITY * (y0 - ty));
    rockets.push({ x: x0, y: y0, vx: (tx - x0) / (-vy / GRAVITY), vy });
}

function explode(x, y) {
    const kind = pick(['peony', 'peony', 'ring', 'willow', 'crackle', 'double']);
    const colour = pick(COLOURS), second = pick(COLOURS);
    const count = kind === 'ring' ? 48 : Math.round(rand(70, 120));
    for (let i = 0; i < count; i++) {
        const angle = kind === 'ring' ? i / count * Math.PI * 2 : rand(0, Math.PI * 2);
        const speed = kind === 'ring' ? 150 : kind === 'willow' ? rand(40, 120) : rand(50, 190);
        sparks.push({
            x, y, vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed, age: 0,
            life: kind === 'willow' ? rand(2.2, 3) : rand(1, 1.8),
            colour: kind === 'willow' ? '#ffcf6b' : kind === 'double' && i % 2 ? second : kind === 'crackle' ? '#ffffff' : colour,
            size: kind === 'willow' ? 1.4 : rand(1.4, 2.4), drag: kind === 'willow' ? 1.6 : 1.1, crackle: kind === 'crackle'
        });
    }
}

let last = 0;
function frame(time) {
    const dt = Math.min(0.05, (time - last) / 1000 || 0);
    last = time;
    pen.globalCompositeOperation = 'destination-out';
    pen.fillStyle = 'rgba(0, 0, 0, 0.2)';
    pen.fillRect(0, 0, innerWidth, innerHeight);
    pen.globalCompositeOperation = 'lighter';

    rockets = rockets.filter(r => {
        r.vy += GRAVITY * dt;
        r.x += r.vx * dt;
        r.y += r.vy * dt;
        sparks.push({ x: r.x, y: r.y, vx: rand(-15, 15), vy: rand(10, 40), age: 0, life: 0.35, colour: '#ffcf8a', size: 1.2, drag: 2 });
        if (r.vy < 0) return true;
        explode(r.x, r.y);
        return false;
    });
    sparks = sparks.filter(s => (s.age += dt) < s.life);
    for (const s of sparks) {
        s.vx *= 1 - s.drag * dt;
        s.vy = s.vy * (1 - s.drag * dt) + GRAVITY * 0.35 * dt;
        s.x += s.vx * dt;
        s.y += s.vy * dt;
        const fade = 1 - s.age / s.life;
        if (s.crackle && chance(0.3)) continue;   // flickering on and off
        pen.globalAlpha = fade;
        pen.fillStyle = s.colour;
        pen.beginPath();
        pen.arc(s.x, s.y, s.size * (0.6 + fade * 0.4), 0, Math.PI * 2);
        pen.fill();
    }
    pen.globalAlpha = 1;
    requestAnimationFrame(frame);
}

async function finale() {
    for (let i = 0; i < 16; i++) {
        launch();
        await wait(rand(80, 260));
    }
}

// Click the sky (anywhere that isn't a link, button or the logo) to launch a rocket there
document.addEventListener('pointerdown', e => {
    if (calm || e.button !== 0 || e.target.closest('a, button, .logo-box, .egg')) return;
    launch(e.clientX, e.clientY);
});

// Bunting strung between two poles on the logo box, with a party hat on one
const letters = `HAPPY NEW YEAR ${year}`;
logoBox.insertAdjacentHTML('beforeend', `
    <span class="pole left" aria-hidden="true"></span>
    <span class="pole right" aria-hidden="true"></span>
    <svg class="bunting" aria-hidden="true"></svg>
    <button class="party-hat" type="button" aria-label="Party hat. Throw confetti.">
        <svg viewBox="0 0 30 40" aria-hidden="true">
            <path d="M15 2L3 36H27Z" fill="#7b2cbf"/>
            <path d="M12 10L18 10M9 19L21 19M6 28L24 28" stroke="#ffd23f" stroke-width="3"/>
            <ellipse cx="15" cy="36" rx="13" ry="3" fill="#ffd23f"/>
            <circle class="pompom" cx="15" cy="3" r="4" fill="#ff4d6d"/>
        </svg>
    </button>`);
const bunting = logoBox.querySelector('.bunting');

function hangBunting() {
    const width = logoBox.clientWidth, sag = 9, flag = Math.min(26, (width - 20) / letters.length);
    bunting.setAttribute('viewBox', `0 0 ${width} 60`);
    const at = t => ({ x: t * width, y: 4 + 4 * sag * t * (1 - t) });
    let flags = '';
    [...letters].forEach((ch, i) => {
        const t = (i + 0.5) / letters.length;
        if (ch === ' ') return;
        const { x, y } = at(t);
        const colour = ['#ffd23f', '#c0c7d6', '#7b2cbf', '#4cc9f0'][i % 4];
        const ink = colour === '#7b2cbf' ? '#ffffff' : '#1b1b3a';
        flags += `
            <g class="flag" style="animation-delay: ${-rand(0, 3).toFixed(1)}s; transform-origin: ${x.toFixed(1)}px ${y.toFixed(1)}px">
                <path d="M${(x - flag / 2).toFixed(1)} ${y.toFixed(1)}H${(x + flag / 2).toFixed(1)}L${x.toFixed(1)} ${(y + flag * 1.25).toFixed(1)}Z" fill="${colour}"/>
                <text x="${x.toFixed(1)}" y="${(y + flag * 0.55).toFixed(1)}" text-anchor="middle" font-family="Anton, Impact, sans-serif"
                      font-size="${(flag * 0.55).toFixed(1)}" fill="${ink}">${ch}</text>
            </g>`;
    });
    let string = 'M0 4';
    for (let i = 1; i <= 20; i++) { const { x, y } = at(i / 20); string += `L${x.toFixed(1)} ${y.toFixed(1)}`; }
    bunting.innerHTML = `<path d="${string}" fill="none" stroke="#e8e8f0" stroke-width="1"/>${flags}`;
}
hangBunting();
new ResizeObserver(hangBunting).observe(logoBox);

const hat = logoBox.querySelector('.party-hat');
hat.addEventListener('click', () => {
    const r = hat.getBoundingClientRect();
    burst(r.left + r.width / 2, r.top + 4, 70);
    hat.querySelectorAll('.kit-says').forEach(b => b.remove());
    says(hat, pick(HAT_SAYS), { duration: 2200 });
    hat.animate([{ transform: 'rotate(18deg)' }, { transform: 'rotate(-8deg) translateY(-8px)' }, { transform: 'rotate(18deg)' }],
        { duration: 450, easing: 'ease-out' });
});

// New Year's Eve: count down to midnight on a tag under the box, then a finale
const tag = make('<span class="countdown" role="timer" aria-live="off"></span>');
logoBox.appendChild(tag);
const midnight = new Date(year, 0, 1);
let celebrated = false;

function tick() {
    const left = midnight - new Date();
    if (left > 0 && left < 24 * 3600 * 1000) {
        const s = Math.floor(left / 1000);
        const pad = n => String(n).padStart(2, '0');
        tag.textContent = `${Math.floor(s / 3600)}h ${pad(Math.floor(s / 60) % 60)}m ${pad(s % 60)}s to ${year}`;
    } else {
        tag.textContent = `Happy ${year}!`;
        if (left <= 0 && left > -5000 && !celebrated && !calm) {
            celebrated = true;
            finale().then(finale);
            burst(innerWidth / 2, innerHeight * 0.2, 150);
        }
    }
}
tick();
setInterval(tick, 1000);

if (!calm) {
    requestAnimationFrame(frame);
    wait(600).then(() => { launch(); launch(); });
    every(LAUNCH_EVERY, () => { for (let i = Math.floor(rand(1, 4)); i > 0; i--) launch(); });
    every(FINALE_EVERY, finale);
}
