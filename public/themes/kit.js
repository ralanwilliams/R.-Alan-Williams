// Shared pieces for the seasonal themes: random timing, a sky and ground behind the page,
// weather falling on a canvas, things that fly across the window, characters that walk
// along the top of the logo box, and speech bubbles. Styles are in kit.css, which
// themes/themes.js loads before the theme's own.
//
// A theme script imports what it needs:
//   import { rand, sky, ground, weather } from '../kit.js';

export const calm = matchMedia('(prefers-reduced-motion: reduce)').matches;
export const rand = (min, max) => min + Math.random() * (max - min);
export const pick = list => list[Math.floor(Math.random() * list.length)];
export const chance = p => Math.random() < p;
export const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
export const logoBox = document.querySelector('.logo-box');

// Runs fn at random intervals of [min, max] ms, skipping turns while the tab is hidden
export async function every([min, max], fn, firstAfter = rand(min, max)) {
    await wait(firstAfter);
    for (;;) {
        if (!document.hidden) await fn();
        await wait(rand(min, max));
    }
}

// A pause that can be cut short, e.g. when a visitor startles something
export function napper() {
    let wake = () => {};
    return {
        nap: ms => new Promise(resolve => {
            const t = setTimeout(resolve, ms);
            wake = () => { clearTimeout(t); wake = () => {}; resolve(); };
        }),
        wake: () => wake()
    };
}

// Eases a value from one number to another over ms, calling set with each step
export function tween(from, to, ms, set) {
    return new Promise(resolve => {
        const start = performance.now();
        requestAnimationFrame(function tick(now) {
            const k = Math.min(1, (now - start) / ms);
            set(from + (to - from) * (k < 0.5 ? 2 * k * k : 1 - (2 - 2 * k) ** 2 / 2));
            if (k < 1) requestAnimationFrame(tick);
            else resolve();
        });
    });
}

// One element from markup
export function make(markup) {
    const template = document.createElement('template');
    template.innerHTML = markup.trim();
    return template.content.firstElementChild;
}

// A speech bubble over parent that fades in and out. Inside something flipped to face
// left, pass flipped so the text still reads.
export function says(parent, text, { delay = 0, duration = 2600, flipped = false } = {}) {
    const bubble = document.createElement('span');
    bubble.className = 'kit-says';
    bubble.textContent = text;
    if (flipped) bubble.style.transform = 'scaleX(-1)';
    parent.appendChild(bubble);
    bubble.animate([{ opacity: 0 }, { opacity: 1, offset: 0.1 }, { opacity: 1, offset: 0.85 }, { opacity: 0 }],
        { duration, delay, fill: 'both' }).finished.then(() => bubble.remove());
    return bubble;
}

// The digger, the stick figure at the dig site (in index.html), drawn in a 250 x 237
// box. His head is a circle at HEAD; his neck is at (142, 72) and his hips at (156, 128).
export const HEAD = { x: 129, y: 58, r: 16.5 };

// Dresses the digger for the theme. Each part is SVG markup:
//   hat    replaces his Santa hat, drawn about the centre of his head, so (0, -HEAD.r) is
//          the top of it. '' leaves him bare-headed; leave it out to keep the Santa hat.
//   under  goes behind him (a cape), over in front of him (a scarf, a beard), both in
//          the drawing's own coordinates
//   tool   replaces his jackhammer: one of the tools below
//   label  describes him to screen readers
export function costume({ hat, under = '', over = '', tool, label } = {}) {
    const digger = document.querySelector('.digger');
    if (!digger) return;
    if (tool !== undefined) {
        const holder = digger.querySelector('.tool');
        holder.innerHTML = tool;
        holder.classList.toggle('dig', !holder.querySelector('.chisel'));   // hand tools dig; a jackhammer hammers
    }
    if (hat !== undefined) digger.querySelector('.hat').innerHTML = `<g transform="translate(${HEAD.x} ${HEAD.y})">${hat}</g>`;
    digger.querySelector('.under').innerHTML = under;
    digger.querySelector('.over').innerHTML = over;
    if (label) digger.setAttribute('aria-label', label);
}

// Tools for the digger to hold. Each is SVG in his drawing, gripped where his hands are
// (about y 104 to 117) and reaching down to the ground at the bit, (92, 214), where the
// holes are centred. Colours are optional; metal is a list of three stops, dark to light
// to dark, for a rounded shine.
let toolIds = 0;
const STEEL = ['#7d7d7d', '#ececec', '#8a8a8a'];
export const GOLD = ['#8a6a12', '#fff3c0', '#9a7414'];
const WOOD = '#8b5a2b';

function shine(metal) {
    const id = 'tool-metal-' + toolIds++;
    return {
        id,
        defs: `<defs><linearGradient id="${id}" x1="0" x2="1">
            <stop offset="0" stop-color="${metal[0]}"/><stop offset="0.45" stop-color="${metal[1]}"/><stop offset="1" stop-color="${metal[2]}"/>
        </linearGradient></defs>`
    };
}

// A T-shaped grip across the top of the shaft, where his hands go
const grip = colour => `<rect x="90.5" y="106" width="5" height="10" fill="${colour}"/>
    <rect x="77" y="103" width="32" height="7" rx="3.5" fill="${colour}"/>`;

export const tools = {
    jackhammer({ metal = STEEL } = {}) {
        const { id, defs } = shine(metal);
        return `${defs}
            <g class="chisel">
                <rect x="90" y="194" width="4.5" height="18" fill="${metal[0]}"/>
                <ellipse cx="92.2" cy="213" rx="4.2" ry="1.9" fill="${metal[2]}"/>
            </g>
            <g stroke="${metal[2]}" stroke-width="0.6">
                <rect x="84" y="184" width="16.5" height="13" rx="3" fill="url(#${id})"/>
                <rect x="81" y="127" width="22.5" height="60" rx="7" fill="url(#${id})"/>
                <rect x="89.5" y="111" width="5.5" height="17" fill="url(#${id})"/>
                <rect x="77" y="104" width="34" height="7" rx="3.5" fill="url(#${id})"/>
            </g>`;
    },

    spade({ handle = WOOD, metal = STEEL } = {}) {
        const { id, defs } = shine(metal);
        return `${defs}${grip(handle)}
            <rect x="90.5" y="112" width="5" height="68" fill="${handle}"/>
            <path d="M88 176H98L99 188H87Z" fill="url(#${id})"/>
            <path d="M80 186H106L105 204Q93 221 81 204Z" fill="url(#${id})" stroke="${metal[2]}" stroke-width="0.6"/>
            <path d="M85 190V203M101 190V203" stroke="#ffffff" stroke-opacity="0.35"/>`;
    },

    snowShovel({ scoop = '#c1121f', snow = true } = {}) {
        return `${grip('#2b2b2b')}
            <rect x="90.5" y="112" width="5" height="76" fill="#9aa3ad"/>
            <path d="M84 186H102L104 194H82Z" fill="${scoop}"/>
            <path d="M66 193H120L122 214H64Z" fill="${scoop}" stroke="rgba(0, 0, 0, 0.25)" stroke-width="0.6"/>
            <path d="M75 197V211M84 197V212M93 197V212M102 197V212M111 197V211" stroke="rgba(0, 0, 0, 0.18)" stroke-width="1.5"/>
            <path d="M64 214H122" stroke="#d9dde3" stroke-width="2.5"/>
            ${snow ? '<path d="M68 194C71 186 81 186 85 190C91 182 104 184 108 190C112 186 118 188 120 194Z" fill="#ffffff"/>' : ''}`;
    },

    hoe({ handle = WOOD, metal = STEEL } = {}) {
        const { id, defs } = shine(metal);
        return `${defs}${grip(handle)}
            <rect x="90.5" y="112" width="5" height="90" fill="${handle}"/>
            <path d="M93 199Q93 209 85 208" stroke="${metal[0]}" stroke-width="3" fill="none"/>
            <path d="M70 205H88V214H70Z" fill="url(#${id})" stroke="${metal[2]}" stroke-width="0.6"/>`;
    },

    fork({ handle = WOOD, ribbon = null } = {}) {
        return `${grip(handle)}
            <rect x="90.5" y="112" width="5" height="66" fill="${handle}"/>
            <path d="M83 176H103V184H83Z" fill="#9a9a9a"/>
            <path d="M84.5 184V211M90 184V213M96 184V213M101.5 184V211" stroke="#9a9a9a" stroke-width="2.6" stroke-linecap="round"/>
            ${ribbon ? `<path d="M93 150L85 145V155ZM93 150L101 145V155Z" fill="${ribbon}"/><circle cx="93" cy="150" r="2.2" fill="${ribbon}"/>` : ''}`;
    },

    iceChopper({ metal = STEEL } = {}) {
        const { id, defs } = shine(metal);
        return `${defs}${grip('#2b2b2b')}
            <rect x="91" y="112" width="4" height="92" fill="url(#${id})"/>
            <path d="M89 201H97L100 214H86Z" fill="url(#${id})" stroke="${metal[2]}" stroke-width="0.6"/>`;
    }
};

// Backdrop layers sit behind the page (z-index -1), painted in this order
const LAYERS = { sky: 0, skyWeather: 1, ground: 2, weather: 3 };

// Puts el behind the page in one of LAYERS, for a theme drawing its own backdrop
export function backdrop(el, layer) {
    el.dataset.kitLayer = LAYERS[layer];
    el.setAttribute('aria-hidden', 'true');
    const layered = [...document.body.querySelectorAll(':scope > [data-kit-layer]')];
    const next = layered.find(other => Number(other.dataset.kitLayer) > LAYERS[layer]);
    if (next) next.before(el);
    else if (layered.length) layered[layered.length - 1].after(el);
    else document.body.prepend(el);
}

// The sky: fixed behind everything. The theme's CSS paints it through className.
export function sky(className) {
    const el = document.createElement('div');
    el.className = 'kit-sky ' + className;
    backdrop(el, 'sky');
    return el;
}

let clipIds = 0;

function puffs(count, x0, x1, base, size) {
    let out = '';
    for (let i = 0; i < count; i++) {
        const t = count > 1 ? i / (count - 1) : 0.5;
        const r = size(t) * rand(0.85, 1.15);
        out += `<circle cx="${(x0 + (x1 - x0) * t + rand(-8, 8)).toFixed(0)}" cy="${(base - r * 0.7).toFixed(0)}" r="${r.toFixed(0)}"/>`;
    }
    return out;
}

// A puffy cloud: overlapping circles on a flat base, drawn light, then again dark and a
// little lower and to the right, which leaves a lit rim along the top
export function cloudSvg(w, h, light, dark) {
    const base = h * 0.9, id = 'kitcloud' + clipIds++;
    const shape = puffs(5 + Math.floor(rand(0, 4)), w * 0.14, w * 0.86, base,
        t => h * (0.2 + 0.3 * Math.sin(Math.PI * t)));
    return `<svg viewBox="0 0 ${w} ${h}" width="${w}" height="${h}">
        <clipPath id="${id}"><rect y="${-h}" width="${w}" height="${base + h}"/></clipPath>
        <g clip-path="url(#${id})">
            <g fill="${light}">${shape}</g>
            <g fill="${dark}" transform="translate(4 7)">${shape}</g>
        </g></svg>`;
}

const mix = (a, b, k) => `rgb(${a.map((v, i) => Math.round(v + (b[i] - v) * k)).join(',')})`;

// Clouds drifting across the sky. light and dark are each a pair of [r, g, b] colours;
// every cloud takes a shade somewhere between the pair.
export function clouds(skyEl, {
    count = 8, top = [0, 45], width = [240, 520], speed = [70, 150],
    light = [[128, 133, 141], [96, 100, 108]], dark = [[92, 96, 104], [62, 66, 73]]
} = {}) {
    for (let i = 0; i < count; i++) {
        const cloud = document.createElement('div');
        cloud.className = 'kit-cloud';
        const w = Math.round(rand(...width)), duration = rand(...speed), shade = rand(0, 1);
        cloud.innerHTML = cloudSvg(w, Math.round(w * 0.4), mix(...light, shade), mix(...dark, shade));
        cloud.style.top = rand(...top) + '%';
        cloud.style.animationDuration = duration + 's';
        cloud.style.animationDelay = -rand(0, duration) + 's';   // start part-way across
        if (calm) cloud.style.transform = `translateX(${rand(-200, innerWidth - 200)}px)`;   // spread out, still
        skyEl.appendChild(cloud);
    }
}

// Stars across the top of a night sky, some of them twinkling
export function stars(skyEl, { count = 140, depth = 70 } = {}) {
    let dots = '';
    for (let i = 0; i < count; i++) {
        const twinkle = chance(0.35)
            ? ` class="twinkle" style="animation-duration: ${rand(1.5, 4).toFixed(1)}s; animation-delay: ${-rand(0, 4).toFixed(1)}s"` : '';
        dots += `<circle cx="${rand(0, 100).toFixed(1)}%" cy="${(rand(0, 1) ** 1.6 * depth).toFixed(1)}%" r="${rand(0.4, 1.6).toFixed(1)}"${twinkle}/>`;
    }
    skyEl.insertAdjacentHTML('beforeend', `<svg class="kit-stars">${dots}</svg>`);
}

// Overcast along the top of the sky: a solid band with a scalloped underside
export function cloudBank(skyEl, light, dark) {
    const bank = n => Array.from({ length: n }, (_, i) =>
        `<circle cx="${(i * 2400 / (n - 1) + rand(-20, 20)).toFixed(0)}" cy="${rand(40, 70).toFixed(0)}" r="${rand(35, 75).toFixed(0)}"/>`).join('');
    skyEl.insertAdjacentHTML('beforeend', `
        <svg class="kit-cloud-bank" viewBox="0 0 2400 150" preserveAspectRatio="xMidYMin slice">
            <g fill="${light}"><rect width="2400" height="45"/>${bank(34)}</g>
            <g fill="${dark}"><rect width="2400" height="30"/>${bank(30)}</g>
        </svg>`);
}

// The ground: from just above the top of the hole cracks to the bottom of the page.
// Holes are resized as the window changes, so it is measured again after them.
// Returns { el, top } with top in page coordinates.
export function ground(className, markup = '') {
    const el = document.createElement('div');
    el.className = 'kit-ground ' + className;
    el.innerHTML = markup;
    backdrop(el, 'ground');
    const state = { el, top: Infinity, pins: [] };

    // Keeps el (absolutely placed in the page) at dy px below the top of the ground.
    // Things people can click go here rather than inside the ground, which sits
    // behind the page.
    state.pin = (el, dy = 0) => {
        state.pins.push([el, dy]);
        document.body.appendChild(el);
        el.style.top = state.top + dy + 'px';
    };

    function place() {
        const tops = [...document.querySelectorAll('.hole:not([hidden]) svg.cracks > g')]
            .map(g => g.getBoundingClientRect()).filter(r => r.height).map(r => r.top);
        if (!tops.length) return;
        state.top = Math.round(Math.min(...tops) + scrollY - 6);
        el.style.top = state.top + 'px';
        el.style.height = '0';
        el.style.height = Math.max(document.documentElement.scrollHeight, innerHeight) - state.top + 'px';
        for (const [pinned, dy] of state.pins) pinned.style.top = state.top + dy + 'px';
    }
    place();
    document.fonts.ready.then(place);
    addEventListener('resize', () => requestAnimationFrame(place));
    const site = document.querySelector('.dig-site');
    if (site) new ResizeObserver(() => requestAnimationFrame(place)).observe(site);
    return state;
}

// Particles on a full-window canvas: rain, snow, leaves, petals, confetti.
//   spawn(anywhere, w)  a new particle with at least x and y; anywhere is true while
//                       filling the window at the start, otherwise it should start above it
//   step(p, dt, w)      moves it on by dt seconds
//   draw(pen, p, w)     draws it on the 2D context
//   splash(p, w)        optional: an effect where it lands, { life, draw(pen, k) } with
//                       k going 0 to 1 over life seconds
// With a ground, each particle lands at a random depth on it. area is px² of window per
// particle, up to max. The returned controller w carries wind and density (a multiplier
// on the count, starting at density and changeable while it runs) for the callbacks to use.
// front puts the canvas over the page instead of behind it.
export function weather({ spawn, step, draw, splash = null, ground: land = null, area = 7000, max = 400, density = 1, layer = 'weather', front = false }) {
    const canvas = document.createElement('canvas');
    canvas.className = 'kit-weather';
    if (front) {
        canvas.classList.add('front');
        canvas.setAttribute('aria-hidden', 'true');
        document.body.appendChild(canvas);
    } else {
        backdrop(canvas, layer);
    }
    const pen = canvas.getContext('2d');
    const w = { wind: 0, density, particles: [], effects: [], canvas, pen, add: () => {} };
    if (calm) return w;

    function landing() {
        if (!land) return innerHeight + 60;
        const top = land.top - scrollY;
        return top < innerHeight ? rand(Math.max(0, top), innerHeight) : innerHeight + 60;
    }
    const fresh = anywhere => Object.assign(spawn(anywhere, w), { land: landing() });
    const wanted = () => Math.round(Math.min(max, innerWidth * innerHeight / area) * w.density);

    function size() {
        const dpr = devicePixelRatio || 1;
        canvas.width = innerWidth * dpr;
        canvas.height = innerHeight * dpr;
        pen.setTransform(dpr, 0, 0, dpr, 0, 0);
    }
    size();
    addEventListener('resize', size);
    w.particles = Array.from({ length: wanted() }, () => fresh(true));
    // Adds a particle made elsewhere, e.g. a burst of confetti; it goes once it lands
    w.add = p => w.particles.push(Object.assign(p, { land: landing() }));

    let last = 0;
    function frame(now) {
        const dt = Math.min(0.05, (now - last) / 1000 || 0);
        last = now;
        pen.clearRect(0, 0, innerWidth, innerHeight);
        const want = wanted(), ps = w.particles;
        if (ps.length < want) ps.push(fresh(false));
        for (let i = ps.length - 1; i >= 0; i--) {
            const p = ps[i];
            step(p, dt, w);
            const lost = p.y > innerHeight + 80 || p.x < -150 || p.x > innerWidth + 150;
            if (lost || p.y >= p.land) {
                if (!lost && splash && p.land < innerHeight) {
                    const effect = splash(p, w);
                    if (effect) w.effects.push(Object.assign(effect, { age: 0 }));
                }
                if (ps.length > want) ps.splice(i, 1);
                else ps[i] = fresh(false);
            }
        }
        for (const p of ps) draw(pen, p, w);
        w.effects = w.effects.filter(e => (e.age += dt) < e.life);
        for (const e of w.effects) e.draw(pen, e.age / e.life);
        requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
    return w;
}

// Snow drifting down and swaying, settling on the ground
export function snowfall(land, { area = 6000, max = 350 } = {}) {
    return weather({
        ground: land, area, max,
        spawn: anywhere => ({
            x: rand(-40, innerWidth + 40), y: anywhere ? rand(-innerHeight, innerHeight) : rand(-30, -5),
            r: rand(1, 3.4), vy: rand(25, 60), phase: rand(0, 6.3), sway: rand(10, 35), t: 0
        }),
        step(f, dt, w) {
            f.t += dt;
            f.y += f.vy * (0.6 + f.r / 4) * dt;
            f.x += (Math.sin(f.t * 1.3 + f.phase) * f.sway + w.wind) * dt;
        },
        draw(pen, f) {
            pen.fillStyle = `rgba(255, 255, 255, ${(0.5 + f.r / 7).toFixed(2)})`;
            pen.beginPath();
            pen.arc(f.x, f.y, f.r, 0, Math.PI * 2);
            pen.fill();
        }
    });
}

// Snow piled along the top of the logo box, with icicles hanging off the bottom
export function snowCap() {
    let lumps = 'M0 20V9';
    for (let x = 0; x < 400; x += rand(18, 34)) lumps += `Q${(x + 12).toFixed(0)} ${rand(-2, 5).toFixed(0)} ${(x + 26).toFixed(0)} ${rand(6, 10).toFixed(0)}`;
    logoBox.insertAdjacentHTML('beforeend', `
        <svg class="kit-snowcap" viewBox="0 0 400 20" preserveAspectRatio="none" aria-hidden="true">
            <path d="${lumps}L400 9V20Z" fill="#ffffff"/>
            <path d="M0 18H400" stroke="#dbe6f3" stroke-width="3"/>
        </svg>
        <span class="kit-icicles" aria-hidden="true"></span>`);
}

// Sends el across the window on a wavy path, then removes it. Artwork faces right and
// is mirrored to fly left unless flip is false. y is the height it starts at; arc lifts
// the middle of the flight and climb moves the end up (negative) or down.
export function fly(el, {
    leftward = chance(0.5), y = rand(0.05, 0.5) * innerHeight, duration = rand(6000, 10000),
    size = 1, waves = 2, amp = 20, arc = 0, climb = 0, tilt = 0, delay = 0, flip = true
} = {}) {
    el.classList.add('kit-flyer');
    el.setAttribute('aria-hidden', 'true');
    document.body.appendChild(el);
    const span = el.offsetWidth * size;
    const start = -span - 20, end = innerWidth + 20, turn = leftward && flip ? -1 : 1;
    const path = t => ({
        x: leftward ? end - (end - start) * t : start + (end - start) * t,
        y: y + Math.sin(t * waves * Math.PI * 2) * amp - Math.sin(t * Math.PI) * arc + t * climb
    });
    const frames = [];
    for (let i = 0; i <= 30; i++) {
        const { x, y: yy } = path(i / 30);
        frames.push({ transform: `translate(${x}px, ${yy}px) scale(${size * turn}, ${size}) rotate(${tilt}deg)` });
    }
    const flight = el.animate(frames, { duration, delay, fill: 'backwards' });
    flight.finished.then(() => el.remove());
    flight.leftward = leftward;
    // Where its centre is in the window at t (0 to 1) of the way across
    flight.at = t => {
        const { x, y: yy } = path(t);
        return { x: x + el.offsetWidth / 2, y: yy + el.offsetHeight / 2 };
    };
    return flight;
}

// A character that walks or hops along the top of the logo box. el is absolutely
// placed with left: 0 and bottom: 100% inside the box; its artwork faces right, and it
// gets the class "left" while facing left.
export function percher(el, { lift = 14, stepMs = 260, stride = [24, 40] } = {}) {
    const me = { x: 0, y: 0 };
    me.room = () => Math.max(0, logoBox.clientWidth - el.offsetWidth);
    me.place = () => { el.style.transform = `translate(${me.x}px, ${me.y}px)`; };

    // One step or hop, landing at (x, y)
    me.step = (x, y = 0, ms = stepMs) => {
        const from = `translate(${me.x}px, ${me.y}px)`;
        const peak = `translate(${(me.x + x) / 2}px, ${Math.min(me.y, y) - lift}px)`;
        me.x = x;
        me.y = y;
        me.place();
        return el.animate([
            { transform: from, easing: 'ease-out' },
            { transform: peak, easing: 'ease-in' },
            { transform: el.style.transform }
        ], ms).finished;
    };

    me.face = leftward => el.classList.toggle('left', leftward);

    // Steps to x, turning to face the way it goes; ms and pause set the pace
    me.goTo = async (x, y = 0, { ms = stepMs, pause = [40, 160] } = {}) => {
        x = Math.max(0, Math.min(me.room(), x));
        me.face(x < me.x);
        while (Math.abs(x - me.x) > 1) {
            const step = Math.sign(x - me.x) * Math.min(Math.abs(x - me.x), rand(...stride));
            const last = Math.abs(x - me.x - step) <= 1;
            await me.step(me.x + step, last ? y : 0, ms);
            await wait(rand(...pause));
        }
    };
    return me;
}
