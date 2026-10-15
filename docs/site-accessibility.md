# Site accessibility: settings and layout

The landing page (`public/index.html`) and the public HTML CV can be switched into a form that aims at WCAG 2.2 AAA, without giving up the default design. The landing page has an accessibility settings dialog; the CV page has display options. Why it works this way is in [ADR 0005](adr/0005-site-accessibility-settings.md).

```
public/
├── a11y.js       the settings: where each comes from, saving, the button and dialog
├── a11y.css      their styles, focus rings, and high contrast over a theme
└── index.html    the page's own styles under each setting, the project list, the layouts
src/Cv.Core/Rendering/
├── cv.css        the CV's display options (screen only), after the shared CV styles
└── HtmlRenderer  RenderOptions.DisplayOptions adds the checkboxes to the public HTML
```

## Landing page settings

A small wooden button at the top right, carrying the accessibility symbol, opens the settings dialog. It is the first thing in the tab order. Its name, *Accessibility*, is in the markup for screen readers and speech control, and shows as a tooltip on hover and keyboard focus.

| Setting | URL value | Default | Does |
|---|---|---|---|
| High contrast | `contrast=normal \| high` | the OS (`prefers-contrast: more`) | The sign in solid dark wood with white lettering, and labels, list and theme badges black on white. Hole openings get a black outline with a white halo, and focus rings are thick. |
| Reduce motion and flashing | `motion=full \| reduce` | the OS (`prefers-reduced-motion`) | Stops the jackhammer, chips and pop-ups, and makes every theme still: no weather, flyers, fireworks, lightning or camera flashes. Reloads the page. |
| Seasonal decorations | `decor=on \| off` | on | Off: no theme, whatever the date or `?theme=`. Reloads the page. |
| List of projects | `list=off \| on` | off | A plain list of every project, with links, under the dig site |
| Open projects in a new tab | `newtab=on \| off` | on | Off: same-tab links. On: the link names say "(opens in a new tab)". |

- **Use the most accessible settings** turns all five to their strictest values: high contrast, reduce motion, decorations off, list on, same tab. **Reset to defaults** goes back to the defaults and the OS.
- **Where a setting comes from:** the URL, then the visitor's saved choice (`localStorage`, key `site-settings`), then the OS, then the default. Changing a setting that came from the URL updates the URL too, so the old value can't override the new choice.
- **When storage is blocked** (private windows, blocked site data), the settings travel in the URL instead.
- **Motion and decorations reload the page,** because a running theme can't be unloaded. The hint under each switch says so (WCAG 3.2.2). After the reload, the dialog reopens with focus on the same switch, and the change is announced.
- **How the page reads them:** `a11y.js` is a plain script at the top of `<head>`. It puts each setting on `<html>` as a `data-` attribute (`data-contrast="high"`) before anything draws.
  - CSS selects on the attributes.
  - The theme kit's `calm` reads `data-motion`.
  - The theme loader skips the theme when `data-decor="off"`.
  - Page scripts use `window.siteSettings.get(key)` and `.set(key, value)`, and listen for the `site-settings` event on `document`.

### The dialog

The button and dialog are made from the sign's materials, but the look costs nothing in accessibility:

- **The look:** the button is a dark wooden plank with gilded lettering, the dialog a cream panel framed in the sign's wood, and the switches brass knobs in a groove.
- **Contrast:** every text pair is 7.7:1 or more, and every control outline 7.7:1 or more. The wood grain only darkens the wood, so it can't lower the contrast of the light lettering.
- **The switches:** each is a real checkbox with `role="switch"`, and says ON or OFF in words. Its label makes a target at least 44px tall.
- **Other display modes:** high contrast turns the dialog black and white. Forced colours (Windows High Contrast) draws it in system colours.
- **Focus:** the native `<dialog>` keeps focus inside it. Escape closes it and returns focus to the button.

## Landing page layout

- **Narrow screens** (320–360px, which is also what 400% zoom gives a 1280px window):
  - The sign's lettering scales with the window.
  - Nothing scrolls sideways in any theme. `html` and `body` both clip, and `body` is the containing block for what themes place on the page.
- **Phones on their side** (landscape, at most 500px tall):
  - The sign is sized by the screen's height, and the digger stands in front of its lower part.
  - The whole dig site fits without scrolling: holes, cracks and the current project's label.
  - Theme badges that sit under the sign move beside it, where he can't cover them.
- **Large screens** (1400 × 900 and up): everything is a little bigger.
- **No project is lost on a narrow screen.** When older holes don't fit, a **Show all N projects** button turns on the list (WCAG 1.4.10).
- **Headings:** the page's `h1` is *Robert Alan Williams, Full Stack Developer*. The carved letters and the spaced-out tagline are hidden from screen readers, which read the words once.
- **Hover labels** follow WCAG 1.4.13. The pointer can move onto a label, and Escape puts a raised project back down until the pointer or focus leaves it.
- **With a theme on a narrow screen,** the page leaves room under the settings button for what the theme puts above the sign. The theme loader sets `data-season` while a theme runs.

## The CV page

The public `/cv/{lang}.html` runs no scripts: its Content Security Policy allows inline styles only ([cv-public.md](cv-public.md#urls)). So its display options are two checkboxes above the CV, which `cv.css` reads with `:has()`, labelled in the CV's language. They aren't remembered between visits, and they never print. The PDF and the editor preview are rendered without them.

- **Readable layout** (WCAG 1.4.8):
  - 18px text with 1.6 line spacing, and more space between paragraphs.
  - Lines of at most 70 characters, everything left-aligned, and no hyphenation.
  - One contact per line, and links underlined.
- **Dark colours:** light text on a dark page, 16.7:1.
- **Always:** a long contact address may break on a narrow screen instead of running off it.

## WCAG: where this gets to

**Target: AAA, in the most accessible configuration.** That's high contrast, reduced motion, decorations off and the list on.

- **Contrast with high contrast on:** sign lettering 17.5:1, DEV 12.1:1, list links 15.3:1, labels and badges 21:1.
- **Every view** loads without script errors in every setting, and nothing scrolls sideways at 320px. These were checked in headless Chrome when the settings were added.
- **Not done:** no screen reader testing (VoiceOver, NVDA, TalkBack), and no audit by an accessibility specialist. "AAA" is the target, not a certificate.

**The default configuration has known gaps.** The supported route around them is the conforming alternate version: the same page with decorations off, which the first control on the page leads to (ADR 0005 §1).

- **Some default text contrast is below AA.** The carved sign lettering is 2.6:1, arguably exempt as a logotype. The hover labels' dates and the gilded tagline are 4.1:1. High contrast fixes them.
- **Theme toys don't all meet AAA.**
  - Easter's nest egg can only be dragged (2.5.7), and it opens a tab by itself after it breaks (3.2.5).
  - Several theme buttons are under 44 × 44px (2.5.5).
  - Groundhog's camera flashes can exceed three a second (2.3.2).
- **Hole openings can be under 44 × 44px.** The list offers the same links 48px tall, which the "equivalent" exception to 2.5.5 allows.

## Adding to the page or a theme

- **Reduced motion:** write CSS for it as `:root[data-motion="reduce"] …` rules, not `@media (prefers-reduced-motion)`, so the site setting applies as well as the OS one. In theme scripts, check `calm` from the kit. A test fails on the media query in any theme file.
- **High contrast:** give new text a solid background under `:root[data-contrast="high"]`, black on white or the reverse. Theme badges are listed in `a11y.css`.
- **Phones on their side:** don't put text right under the sign, where the digger stands, or move it beside the sign in a `@media (max-height: 500px) and (orientation: landscape)` block, as the tally, egg score and countdown do.
- **Things to click:** use a `<button>` with an `aria-label`, at least 44 × 44px where it matters.

## Tests

```powershell
npm test                                         # includes tests/Site.Js/a11y.test.mjs
dotnet test tests/Cv.Core.Tests                  # the CV's display options (HtmlRendererTests)
```

`a11y.test.mjs` runs `a11y.js` in a stand-in for the browser. It checks:

- where each setting comes from, and that values which aren't allowed are ignored
- saving, announcing and reloading
- that a choice updates the URL, and the fallback to the URL when storage is blocked
- that no theme uses the reduced-motion media query directly

The dialog, the layouts and the themes have no automated browser tests in the repo. Check them by hand in a phone-sized window, with `?contrast=high&motion=reduce&decor=off&list=on`, and on a phone on its side.
