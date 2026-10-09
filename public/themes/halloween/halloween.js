// Halloween: a stormy sky with rain and lightning over muddy ground, swarms of bats and
// witches on broomsticks, a jack-o'-lantern on the logo box with a ghost inside, and a
// cobweb in its corner with a spider letting itself down from it.
// Loaded by themes/themes.js, which also loads halloween.css.
import {
    calm, rand, pick, chance, wait, every, napper, says, logoBox,
    sky, clouds, cloudBank, ground, weather, fly, make, costume, tools
} from '../kit.js';

// A jack-o'-lantern for a head, and a cape
costume({
    tool: tools.spade({ handle: '#4a3324', metal: ['#4f463e', '#a39a90', '#3f3730'] }),
    hat: '',
    under: '<path d="M142 72C150 82 168 102 178 150L170 146L163 152L156 132C153 110 148 90 142 72Z" fill="#3c1361"/>',
    over: `<g transform="translate(129 58)">
        <path d="M1 -17C0 -22 3 -25 6 -25L5 -21C4 -21 3 -19 4 -16Z" fill="#4f6b2a"/>
        <ellipse cx="-8" cy="1" rx="12" ry="16" fill="#d9661a"/>
        <ellipse cx="8" cy="1" rx="12" ry="16" fill="#d9661a"/>
        <ellipse cx="0" cy="1" rx="11" ry="17.5" fill="#e8761c"/>
        <path d="M-5 -14C-9 -6 -9 8 -5 16M6 -14C10 -6 10 8 6 16" stroke="#b9530f" fill="none"/>
        <g class="glow" fill="#ffd23f">
            <path d="M-15 -4L-9 -8L-8 -1Z"/><path d="M1 -4L-5 -8L-6 -1Z"/>
            <path d="M-7 2L-5 6L-9 6Z"/>
            <path d="M-17 8C-13 14 1 14 5 8L2 9L0 11L-3 9L-6 11L-9 9L-12 11L-15 9Z"/>
        </g>
    </g>`,
    label: "Stick figure with a jack-o'-lantern for a head, in a cape, digging with an old spade"
});

// What the ghost says when it gets out of the pumpkin, picked at random
const GHOST_SAYS = ['Boo!', 'Trick or treat?', 'Happy Halloween!', 'Shipping soon... mwahaha', 'Did you check the basement?'];
const WITCH_SAYS = ['Hehehe!', 'Cackle cackle!', 'Out of my way!', 'Nice site, dearie!'];
const BAT_EVERY = [1500, 6000];               // ms between colonies of bats, at random
const BATS_PER_COLONY = [3, 9];
const MAX_BATS = 40;                          // on screen at once
const WITCH_EVERY = [9000, 22000];            // ms between witch flights; one or two fly each time
const LIGHTNING_EVERY = [5000, 16000];        // ms between strikes
const CLOUDS = 9;
const RAIN_DENSITY = 7000;                    // px² of window per raindrop

// Cobweb: spokes out of the corner, joined by threads that sag towards it
const WEB_R = 64;
const spokes = [0, 20, 45, 70, 90].map(d => d * Math.PI / 180);
let web = spokes.map(a => `M0 0L${(Math.cos(a) * WEB_R).toFixed(1)} ${(Math.sin(a) * WEB_R).toFixed(1)}`).join('');
// The spider's silk is tied where the 70 degree spoke meets the 42px thread
const anchor = { x: Math.cos(spokes[3]) * 42, y: Math.sin(spokes[3]) * 42 };
for (const r of [14, 28, 42, 56]) {
    for (let i = 0; i < spokes.length - 1; i++) {
        const [a, b] = [spokes[i], spokes[i + 1]], mid = (a + b) / 2, sag = r * 0.8;
        web += `M${(Math.cos(a) * r).toFixed(1)} ${(Math.sin(a) * r).toFixed(1)}` +
            `Q${(Math.cos(mid) * sag).toFixed(1)} ${(Math.sin(mid) * sag).toFixed(1)} ` +
            `${(Math.cos(b) * r).toFixed(1)} ${(Math.sin(b) * r).toFixed(1)}`;
    }
}

logoBox.insertAdjacentHTML('afterbegin', `
    <svg class="cobweb" viewBox="0 0 ${WEB_R} ${WEB_R}" aria-hidden="true">
        <path d="${web}" fill="none" stroke="#ffffff" stroke-width="0.8"/>
    </svg>
    <button class="pumpkin" type="button" aria-label="Jack-o'-lantern. Something is inside.">
        <svg viewBox="0 0 58 48" aria-hidden="true">
            <path d="M27 9C26 4 29 1 33 1L32 4C30 4 30 6 31 10Z" fill="#4f6b2a"/>
            <path d="M33 4C38 0 44 2 45 6C41 4 37 5 34 7Z" fill="#6c8f39"/>
            <ellipse cx="17" cy="29" rx="15" ry="17" fill="#d9661a"/>
            <ellipse cx="41" cy="29" rx="15" ry="17" fill="#d9661a"/>
            <ellipse cx="29" cy="29" rx="14" ry="18.5" fill="#e8761c"/>
            <g fill="none" stroke="#b9530f" stroke-width="1">
                <path d="M22 12C17 20 17 38 22 46M36 12C41 20 41 38 36 46"/>
            </g>
            <g class="glow" fill="#ffd23f">
                <path d="M14 24L21 19L22 27Z"/>
                <path d="M44 24L37 19L36 27Z"/>
                <path d="M29 28L32 33L26 33Z"/>
                <path d="M12 34C18 42 40 42 46 34L42 35L40 38L37 36L34 39L31 37L28 39L25 37L22 39L19 36L16 38Z"/>
            </g>
        </svg>
    </button>
    <span class="silk" aria-hidden="true"></span>
    <div class="spider" aria-hidden="true">
        <svg viewBox="0 0 30 24">
            <g class="legs" fill="none" stroke="#111" stroke-width="1.3" stroke-linecap="round">
                <path d="M12 11L6 6L2 9M12 13L5 11L1 15M12 15L5 16L2 21M13 17L8 20L6 24"/>
                <path d="M18 11L24 6L28 9M18 13L25 11L29 15M18 15L25 16L28 21M17 17L22 20L24 24"/>
            </g>
            <ellipse cx="15" cy="15" rx="5.5" ry="6.5" fill="#111" stroke="#ffffff" stroke-opacity="0.35" stroke-width="0.6"/>
            <circle cx="15" cy="8" r="3.6" fill="#111" stroke="#ffffff" stroke-opacity="0.35" stroke-width="0.6"/>
            <circle cx="13.6" cy="7.6" r="0.9" fill="#ff3b30"/>
            <circle cx="16.4" cy="7.6" r="0.9" fill="#ff3b30"/>
            <path d="M15 12L13 15L15 18L17 15Z" fill="#c1121f"/>
        </svg>
    </div>
`);

// The ghost: each click on the pumpkin lets one out to float off and fade
const pumpkin = logoBox.querySelector('.pumpkin');
pumpkin.addEventListener('click', () => {
    if (logoBox.querySelectorAll('.ghost').length >= 3) return;
    const ghost = make(`
        <div class="ghost" role="status">
            <svg viewBox="0 0 40 46" aria-hidden="true">
                <path d="M4 44L4 18C4 6 12 0 20 0C28 0 36 6 36 18L36 44L30 39L25 44L20 39L15 44L10 39Z"
                      fill="#f6f6fb" stroke="#c9c9d6" stroke-width="1"/>
                <ellipse cx="14" cy="17" rx="3" ry="4.5" fill="#222"/>
                <ellipse cx="26" cy="17" rx="3" ry="4.5" fill="#222"/>
                <ellipse cx="20" cy="27" rx="3.2" ry="4" fill="#222"/>
            </svg>
        </div>`);
    logoBox.appendChild(ghost);
    says(ghost, pick(GHOST_SAYS), { duration: 3600 });

    const sway = rand(14, 26) * (chance(0.5) ? -1 : 1);
    const frames = calm
        ? [{ opacity: 1 }, { opacity: 1, offset: 0.8 }, { opacity: 0 }]
        : [
            { transform: 'translate(0, 34px)', opacity: 0.2 },
            { transform: 'translate(0, -6px)', opacity: 1, offset: 0.15 },
            { transform: `translate(${sway}px, -40px)`, opacity: 1, offset: 0.45 },
            { transform: `translate(${-sway / 2}px, -70px)`, opacity: 0.9, offset: 0.75 },
            { transform: `translate(${sway}px, -110px)`, opacity: 0 }
        ];
    ghost.animate(frames, { duration: 3600, easing: 'ease-in-out' }).finished.then(() => ghost.remove());
});

// The spider lets itself down from the web on its silk and climbs back up, at random,
// staying on the box. At rest it sits on the web with its head on the anchor. Get too
// close (hover, or a tap) and it scurries back up to the web.
const spider = logoBox.querySelector('.spider');
const silk = logoBox.querySelector('.silk');
const HEAD = 8;                               // px from the top of the spider to its head
silk.style.left = anchor.x + 'px';
silk.style.top = anchor.y + 'px';
spider.style.left = anchor.x - spider.offsetWidth / 2 + 'px';
spider.style.top = anchor.y - HEAD + 'px';
const maxDrop = () => Math.max(30, logoBox.clientHeight - anchor.y - spider.offsetHeight);
const { nap, wake } = napper();

function lower(px, ms) {
    spider.style.transitionDuration = silk.style.transitionDuration = ms + 'ms';
    spider.style.transform = `translateY(${px}px)`;
    silk.style.height = px + 'px';
    spider.classList.add('scurry');
    return wait(ms).then(() => spider.classList.remove('scurry'));
}

let startled = false;
function startle() {
    if (startled) return;
    startled = true;
    lower(0, 250);
    wake();
}
spider.addEventListener('pointerenter', startle);
spider.addEventListener('pointerdown', startle);

async function spiderLife() {
    await nap(rand(1500, 5000));
    for (;;) {
        if (startled) {
            await wait(rand(3000, 7000));
            startled = false;
        }
        await lower(rand(25, maxDrop()), rand(1800, 3200));
        for (let i = Math.floor(rand(1, 4)); i > 0 && !startled; i--) {
            await nap(rand(1200, 4000));
            if (!startled) await lower(rand(25, maxDrop()), rand(600, 1500));
        }
        if (startled) continue;
        await nap(rand(1000, 3000));
        if (startled) continue;
        await lower(0, rand(1500, 2500));
        await nap(rand(2000, 8000));
    }
}

// A stormy sky: a bank of cloud along the top, and clouds drifting across
const storm = sky('storm-sky');
cloudBank(storm, '#575c64', '#41454c');
clouds(storm, { count: CLOUDS });

// Muddy ground with gravestones on the skyline
const mud = ground('mud', `
    <svg class="tombstone" viewBox="0 0 34 44" style="left: 5%">
        <path d="M3 44V16C3 7 9 2 17 2S31 7 31 16V44Z" fill="#8b8f96" stroke="#5d6168" stroke-width="1.5"/>
        <text x="17" y="22" text-anchor="middle" font-family="Anton, Impact, sans-serif" font-size="9" fill="#5d6168">RIP</text>
        <path d="M8 30H26M8 35H22" stroke="#6d7178" stroke-width="1.2"/>
    </svg>
    <svg class="tombstone" viewBox="0 0 34 44" style="right: 7%">
        <path d="M13 44V20H5V12H13V3H21V12H29V20H21V44Z" fill="#7d8188" stroke="#55595f" stroke-width="1.5"/>
    </svg>
    <svg class="tombstone" viewBox="0 0 34 44" style="right: 13%; width: 26px">
        <path d="M4 44V10C4 6 7 4 10 4H24C27 4 30 6 30 10V44Z" fill="#7f838a" stroke="#55595f" stroke-width="1.5" transform="rotate(-7 17 44)"/>
    </svg>`);

// Rain, slanting in the wind, splashing where it lands on the ground
const WIND = 0.18;                            // sideways speed as a share of falling speed
weather({
    ground: mud, area: RAIN_DENSITY,
    spawn: anywhere => ({
        x: rand(-60, innerWidth), y: anywhere ? rand(-innerHeight, innerHeight) : rand(-80, -10),
        len: rand(10, 22), speed: rand(650, 1000)
    }),
    step(d, dt) {
        d.y += d.speed * dt;
        d.x += d.speed * WIND * dt;
    },
    draw(pen, d) {
        pen.strokeStyle = 'rgba(210, 220, 235, 0.45)';
        pen.lineWidth = 1;
        pen.beginPath();
        pen.moveTo(d.x, d.y);
        pen.lineTo(d.x - d.len * WIND, d.y - d.len);
        pen.stroke();
    },
    splash: d => ({
        life: 0.3,
        draw(pen, k) {
            pen.strokeStyle = `rgba(210, 220, 235, ${(0.6 * (1 - k)).toFixed(2)})`;
            pen.beginPath();
            pen.ellipse(d.x, d.land, 2 + k * 7, 0.8 + k * 2, 0, Math.PI, 0);
            pen.stroke();
        }
    })
});

// Bats fly in colonies: several at once, all one way, at about the same height
function batColony(count) {
    const leftward = chance(0.5), y = rand(0.04, 0.7) * innerHeight;
    for (let i = 0; i < count; i++) {
        if (document.querySelectorAll('.bat').length >= MAX_BATS) return;
        const bat = make(`
            <div class="bat">
                <svg viewBox="0 0 60 30">
                    <path class="wing" d="M28 13C22 4 12 3 2 7C6 9 7 13 6 16C10 13 14 15 15 18C18 14 23 15 28 16Z" fill="#1c1426"/>
                    <path class="wing" d="M32 13C38 4 48 3 58 7C54 9 53 13 54 16C50 13 46 15 45 18C42 14 37 15 32 16Z" fill="#1c1426"/>
                    <ellipse cx="30" cy="15" rx="4" ry="6" fill="#241a31"/>
                    <path d="M27 10L27.5 6L29 9.5M33 10L32.5 6L31 9.5" fill="#241a31"/>
                    <circle cx="28.6" cy="12" r="0.8" fill="#ffcf3f"/>
                    <circle cx="31.4" cy="12" r="0.8" fill="#ffcf3f"/>
                </svg>
            </div>`);
        bat.querySelectorAll('.wing').forEach(w => { w.style.animationDuration = rand(0.16, 0.28) + 's'; });
        fly(bat, {
            leftward, y: y + rand(-60, 60), size: rand(0.45, 1.15), duration: rand(3500, 7500),
            waves: rand(2, 5), amp: rand(12, 50), climb: rand(-40, 40), delay: i * rand(80, 500), flip: false
        });
    }
}

// Witches on broomsticks, with a cackle now and then
function flyWitch(delay, leftward, y) {
    const witch = make(`
        <div class="witch">
            <svg viewBox="-4 -12 128 76">
                <path d="M18 47L-2 38L1 45L-4 50L2 56L-1 61L18 51Z" fill="#c9a24a"/>
                <path d="M18 47L4 44M18 49L0 52M18 50L5 57" stroke="#9c7a2c" stroke-width="1"/>
                <path d="M12 49L104 39" stroke="#6b4423" stroke-width="3.2" stroke-linecap="round"/>
                <path class="cape" d="M52 20C42 26 30 32 18 36C28 40 40 44 54 42Z" fill="#141019"/>
                <path d="M54 12C50 17 46 22 43 28M56 13C53 18 50 24 48 30" fill="none" stroke="#e0702a" stroke-width="2"/>
                <path d="M48 20C46 30 46 38 50 45L68 42C67 33 63 25 58 20Z" fill="#2a1f3d"/>
                <path d="M58 26L72 39" stroke="#2a1f3d" stroke-width="4.5" stroke-linecap="round"/>
                <circle cx="72.5" cy="39.5" r="2.4" fill="#7fb069"/>
                <path d="M57 42L64 52" stroke="#2a1f3d" stroke-width="4.5" stroke-linecap="round"/>
                <path d="M61 50L71 52.5L62 55Z" fill="#111"/>
                <circle cx="60" cy="14" r="6.5" fill="#7fb069"/>
                <path d="M65.5 13L72 16.5L65 17.5Z" fill="#6c9c58"/>
                <circle cx="62.5" cy="12.5" r="1" fill="#111"/>
                <ellipse cx="58" cy="8.5" rx="12" ry="2.6" fill="#111"/>
                <path d="M51.5 8C52 2 50-4 40-11C50-8 58-2 64.5 8Z" fill="#111"/>
                <path d="M52.2 5.8L63.5 6.6L64 8L51.8 7.6Z" fill="#7b2cbf"/>
            </svg>
        </div>`);
    fly(witch, {
        leftward, y, delay, size: rand(0.75, 1.1), duration: rand(9000, 14000),
        waves: 3, amp: rand(8, 18), arc: 40, tilt: -4
    });
    if (chance(0.6)) says(witch, pick(WITCH_SAYS), { delay: delay + rand(2500, 5000), flipped: leftward });
}

// Lightning over everything: a forked bolt and a flash of the whole window, twice in
// quick succession. That is two flashes in 700ms, inside the three-a-second limit for
// flashing content.
const flash = make('<div class="lightning-flash" aria-hidden="true"></div>');
const bolt = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
bolt.setAttribute('class', 'lightning-bolt');
bolt.setAttribute('aria-hidden', 'true');
const boltPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
bolt.appendChild(boltPath);
document.body.append(flash, bolt);

function forkPath(x, y, bottom, spread) {
    let d = `M${x.toFixed(0)} ${y.toFixed(0)}`;
    const forks = [];
    while (y < bottom) {
        y += rand(14, 40);
        x += rand(-spread, spread);
        d += `L${x.toFixed(0)} ${y.toFixed(0)}`;
        if (chance(0.12)) forks.push([x, y]);
    }
    return { d, forks };
}

function strike() {
    const w = innerWidth, h = innerHeight;
    bolt.setAttribute('viewBox', `0 0 ${w} ${h}`);
    const main = forkPath(rand(0.1, 0.9) * w, 0, rand(0.4, 0.8) * h, 28);
    let d = main.d;
    for (const [x, y] of main.forks.slice(0, 3)) d += forkPath(x, y, y + rand(40, 130), 22).d;
    boltPath.setAttribute('d', d);

    const strobe = [
        { opacity: 0 }, { opacity: 1, offset: 0.04 }, { opacity: 0.1, offset: 0.2 },
        { opacity: 0.9, offset: 0.34 }, { opacity: 0, offset: 1 }
    ];
    bolt.animate(strobe, { duration: 700, easing: 'ease-out' });
    flash.animate(strobe.map(f => ({ ...f, opacity: f.opacity * 0.55 })), { duration: 700, easing: 'ease-out' });
}

if (calm) {
    // No rain, bats, witches or lightning; the clouds hold still and the spider just hangs
    lower(Math.min(55, maxDrop()), 0);
} else {
    spiderLife();
    wait(800).then(() => batColony(12));      // a swarm to start the night
    every(BAT_EVERY, () => batColony(Math.round(rand(...BATS_PER_COLONY))));
    every(WITCH_EVERY, () => {
        const leftward = chance(0.5), y = rand(0.05, 0.4) * innerHeight;
        flyWitch(0, leftward, y);
        if (chance(0.5)) flyWitch(rand(900, 2000), leftward, y + rand(40, 90));
    }, rand(2500, 6000));
    every(LIGHTNING_EVERY, strike, rand(3000, 7000));
}
