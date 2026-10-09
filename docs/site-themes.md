# Seasonal themes

The landing page (`public/index.html`) can dress up for the time of year. Each theme adds its own markup, styles and behaviour on top of the page, and turns itself on between the dates set for it. There is no build step and nothing to deploy on the day: the visitor's browser checks the date.

```
public/themes/
├── themes.js            the list of themes and their dates, and the loader
├── kit.js, kit.css      shared pieces: sky, ground, weather, flyers, characters, speech bubbles
├── new-year/            fireworks over a city, confetti, bunting and a party hat
├── groundhog/           Gobbler's Knob from the crowd, waiting on Phil's forecast
├── st-patricks/         hills, shamrocks, a rainbow, a pot of gold and a leprechaun
├── easter/              an egg hunt, the Easter Bunny, balloons, and an egg to drop from the hen's nest
├── spring/              a meadow, petals, butterflies, songbirds, showers and a rainbow
├── halloween/           a storm, bats, witches, a jack-o'-lantern and a spider
├── thanksgiving/        a sunset harvest, autumn leaves, geese and a turkey
└── christmas/           a snowy night, Santa's sleigh, presents and lights
```

Each theme folder holds `<name>.js` and `<name>.css`.

## Setting the dates

Themes and their dates are listed in `THEMES` at the top of [`public/themes/themes.js`](../public/themes/themes.js):

| Theme | Shown | Notes |
| --- | --- | --- |
| `new-year` | 31 Dec to 2 Jan | |
| `groundhog` | 31 Jan to 3 Feb | Groundhog Day is 2 Feb |
| `st-patricks` | 10 to 18 Mar | |
| `easter` | 22 Mar to 25 Apr | every date Easter Sunday can fall on |
| `spring` | 19 Mar to 20 Jun | listed after Easter, which wins where they overlap |
| `halloween` | 1 to 31 Oct | |
| `thanksgiving` | 1 to 28 Nov | US Thanksgiving is the 22nd to the 28th |
| `christmas` | 29 Nov to 30 Dec | |

- `MM-DD` dates repeat every year. A range may run over New Year (`'12-20'` to `'01-06'`).
- `YYYY-MM-DD` dates apply to that year only, for holidays that move, such as Easter.
- Both ends are inclusive and use the visitor's local time.
- Where ranges overlap, the first theme in the list wins. One theme is shown at a time.

## Previewing

Add `?theme=<name>` to the URL to see a theme on any day, for example `http://localhost:8788/?theme=christmas` under `npm run dev`. `?theme=none` shows the page with no theme.

`?theme=all` is for testing: it shows every theme in `THEMES` in turn, `ALL_SECONDS` (10) each, with a tag in the corner naming the theme and counting down to the next. Themes can't be unloaded, so it reloads the page between them. Which theme is up comes from the clock (each 10-second slot belongs to one theme), so nothing has to be remembered between reloads.

Without `?theme=` in the URL, the date alone decides; nothing in the code can force a theme for every visitor. A test checks this.

## How a theme is loaded

The loader adds `kit.css` and the theme's stylesheet, then, once both have loaded, runs the theme's script as an ES module. The script runs after the page's own script, so the holes and logo box are already there. Being a module, it has its own scope (the page's script declares top-level names a classic script would clash with) and can import the kit.

## The kit

[`kit.js`](../public/themes/kit.js) holds what several themes share. Its styles are in [`kit.css`](../public/themes/kit.css); the theme's own CSS sets colours and sizes.

| Export | What it does |
| --- | --- |
| `sky(className)` | A fixed layer behind the page; the theme's CSS paints it |
| `clouds`, `cloudBank`, `cloudSvg`, `stars` | Drifting puffy clouds, an overcast band, one cloud's SVG, a starry night |
| `ground(className, markup)` | Ground from just above the top of the hole cracks to the bottom of the page. It is measured again whenever the holes are resized. Its `pin(el, dy)` keeps a clickable element on the ground's top edge. |
| `weather({ spawn, step, draw, splash, ground, area, density })` | Particles on a full-window canvas (rain, snow, leaves, petals, confetti, shamrocks). With a ground, each lands at a random depth on it. `wind` and `density` can change while it runs. |
| `snowfall`, `snowCap` | Snow on a ground; snow and icicles on the logo box |
| `fly(el, options)` | Sends something across the window on a wavy path (bats, witches, geese, Santa, butterflies). Returns the animation, whose `at(t)` gives its position partway. |
| `percher(el, options)` | A character that walks or hops along the top of the logo box (the turkey, leprechaun and cardinal) |
| `costume({ hat, under, over, tool, label })`, `tools`, `GOLD` | Dresses the digger and hands him a tool (see below) |
| `says(parent, text)` | A speech bubble that fades in and out |
| `every([min, max], fn)`, `napper()`, `tween()`, `rand`, `pick`, `chance`, `wait` | Random timing, pauses that can be cut short, easing a value |
| `calm` | True when the visitor has asked for reduced motion |
| `backdrop(el, layer)`, `make(markup)`, `logoBox` | Lower-level helpers |

Backdrop layers sit behind the page at `z-index: -1`, in this order: sky, sky weather (fireworks), ground, weather.

## The digger

The stick figure at the dig site is an inline SVG in `index.html`, drawn in a 250 × 237 box. His tool reaches the ground at (92, 214), which the holes are lined up with. Without a theme he wears a Santa hat and works a jackhammer, whose bit hammers up and down by CSS.

Each theme dresses him with `costume()` from the kit:

- `hat` replaces the Santa hat. It is drawn about the centre of his head (`HEAD`, radius 16.5), so `(0, -16.5)` is the top of his head. `''` leaves him bare-headed; leaving it out keeps the Santa hat.
- `under` is drawn behind him and `over` in front of him, in the drawing's own coordinates. His neck is at (142, 72) and his hips at (156, 128).
- `tool` replaces his jackhammer. `tools` in the kit has a jackhammer, spade, snow shovel, hoe, garden fork and ice chopper, each gripped where his hands are and reaching the ground at the bit. Each takes optional colours, such as `tools.spade({ handle: '#2e8b57', metal: GOLD })`. A jackhammer's bit hammers; hand tools bob up and down as he digs.
- `label` replaces his description for screen readers.

| Theme | Outfit | Tool |
| --- | --- | --- |
| `new-year` | party hat and a party blower | gold jackhammer |
| `groundhog` | top hat and bow tie, like the Inner Circle | ice chopper |
| `st-patricks` | leprechaun hat and an orange beard | golden spade |
| `easter` | bunny ears | garden fork with a ribbon |
| `spring` | straw sun hat with a flower | hoe |
| `halloween` | a jack-o'-lantern for a head, and a cape | old spade |
| `thanksgiving` | pilgrim hat and collar | spade |
| `christmas` | his Santa hat, and a scarf | snow shovel |

## Adding a theme

1. Create `public/themes/<name>/` with `<name>.css` and `<name>.js`.
2. Add `{ name: '<name>', from: …, to: … }` to `THEMES`.
3. Import what you need from `../kit.js`, and give the digger an outfit with `costume()`. With `calm` set, skip animation that moves things about.

## Tests

`npm test` includes `tests/Site.Js`. It checks how dates are matched, that every theme in `THEMES` has valid dates and both its files, that every theme folder is listed, and which theme each 2027 holiday gets.

## The themes

**New Year.** Fireworks burst over a city skyline at night: peonies, rings, willows, crackles and two-colour bursts, launched every second or two (`LAUNCH_EVERY`), with a finale every half-minute or so (`FINALE_EVERY`). Clicking the sky launches one there. Confetti falls. A "Happy New Year" bunting with next year's number hangs over the logo box between two gold poles; the party hat on one throws confetti when clicked. On New Year's Eve a tag under the box counts down to midnight, which sets off a double finale.

**Groundhog Day.** Gobbler's Knob, seen from the crowd. The logo box is the sign, with a red "Gobbler's Knob" header, over a wooden stage with snowy pines behind it, and the Inner Circle stand along the stage in top hats. The backs of the crowd's heads, in beanies, hoods, top hats and groundhog hats, fill the bottom of the window; some hold phones up, and camera flashes go off (`FLASH_EVERY`). Snow falls in the dark before dawn, and the sun breaks through low on the horizon now and then (`SUN_EVERY`). Every so often (`FORECAST_EVERY`), or when his tree stump is clicked, Phil pops up out of the stump and his handler holds him up for his forecast. If the sun is out he sees his shadow and it's six more weeks of winter, to boos and heavier snow; if not, it's an early spring, to cheers, flashes and hats raised on stage. A tally under the sign keeps score. The stage and crowd are drawn to fit the window and redrawn when it changes.

**St. Patrick's Day.** Rolling green hills under a clear sky, with shamrocks tumbling down and, now and then, a golden four-leaf clover. A rainbow comes down behind the hills to a pot of gold, which spills coins when clicked. A leprechaun hops and jigs along the logo box; catch him (click) and he leaps and throws coins of his own.

**Easter.** A pastel morning with a soft sun over a meadow, with an egg-hunt signpost and an overflowing basket on the skyline. Decorated eggs are hidden in tufts of grass about the meadow, away from the dig site; click one to find it, and a score under the sign counts them. Find them all for a burst of confetti and a fresh batch. The Easter Bunny hops across the meadow with his basket every so often (`BUNNY_EVERY`), hiding more eggs as he goes; click him for a word. Baby chicks potter about the grass, peeping. Egg-shaped balloons float up the window (`BALLOON_EVERY`) and pop into confetti when clicked. Pastel blossom petals drift down, and a garland of little eggs hangs under the sign. On top of the sign, a nest holds an egg and a mama hen hops back and forth, sometimes sitting on it. Visitors can drag the egg out of the nest with the left mouse button (or a finger). When they let go, it falls to the bottom of the window. A fall of up to half the window height bounces. A longer fall cracks the egg, and a chick says where it's going (`EGG_SAYS`) for `EGG_WAIT` ms before `EGG_URL` opens in a new tab. A browser may block a new tab opened that long after the click. In that case the chick's speech bubble becomes a link.

**Spring.** A bright sky with a turning sun over a meadow, where flowers grow along the skyline and sway. Blossom petals drift down; butterflies (`BUTTERFLY_EVERY`) and pairs of songbirds (`BIRDS_EVERY`) pass by. Every so often (`SHOWER_EVERY`) the sky greys over for a shower, and a rainbow comes out afterwards. A flowering vine grows up and over the logo box, with a bee buzzing about it that zips off when touched.

**Halloween.** A gray, cloudy sky, with rain (`RAIN_DENSITY`) falling and splashing on muddy ground and gravestones on the skyline. Lightning strikes every so often (`LIGHTNING_EVERY`): a forked bolt and a flash of the whole window, twice in quick succession, which keeps it under three flashes a second. Colonies of bats (`BATS_PER_COLONY`) cross the window every few seconds (`BAT_EVERY`), starting with a swarm when the page opens. One or two witches fly over on broomsticks now and then (`WITCH_EVERY`), sometimes cackling (`WITCH_SAYS`). A jack-o'-lantern sits on the logo box, its candle flickering; clicking it lets a ghost out, which says something from `GHOST_SAYS` and floats away. A spider lets itself down on its silk from a cobweb in the box's corner, and scurries back up if the pointer touches it.

**Thanksgiving.** A sunset over a harvest field, with hay, corn shocks, pumpkins and a scarecrow on the skyline. Autumn leaves tumble down and rest a while where they land; gusts (`GUST_EVERY`) blow them sideways and bring more down. Geese fly over in a V (`GEESE_EVERY`). A turkey struts along the logo box, pecking and fanning its tail to gobble; click it and it panics, losing a few feathers and running to the far end.

**Christmas.** A starry night with a full moon and snow falling, in gusts, on a snowy yard with a snowman, pines, a lit tree and a cabin with smoke from its chimney. The logo box is snowed on, hung with icicles and strung with lights; clicking the lights changes their pattern (twinkle, chase, alternate, steady). Santa's sleigh flies over (`SANTA_EVERY`) and often drops a present, which lands in the snow; click it to open it.

**Reduced motion.** With reduced motion turned on, every theme keeps its scene but drops the movement: no weather, flyers, fireworks or wandering characters, and the clouds hold still. Clicking still works where it doesn't need movement, such as changing the Christmas lights, asking the groundhog for a forecast or making the turkey gobble.
