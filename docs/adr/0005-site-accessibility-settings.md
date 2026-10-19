# ADR 0005: Site accessibility: settings that switch the site into a conforming version

- **Status:** Accepted
- **Date:** 2026-10-15
- **Implementation:** `public/a11y.js`, `public/a11y.css`, `public/index.html`, `public/themes/` (`kit.js` `calm`, the loader, each theme's reduced-motion rules), `src/Cv.Core/Rendering/HtmlRenderer.cs` (`RenderOptions.DisplayOptions`) and `cv.css`, `tests/Site.Js/a11y.test.mjs`, `tests/Cv.Core.Tests/Rendering/HtmlRendererTests.cs`; guide in [docs/site-accessibility.md](../site-accessibility.md)

## Context

The landing page is deliberately playful:

- a carved wooden sign
- a stick figure with a jackhammer
- project icons that pop out of holes
- seasonal themes ([site-themes.md](../site-themes.md)) with weather, fireworks, lightning, flyers and small games

Much of that sits badly with WCAG at its highest level (AAA):

- **Contrast:** the carved lettering is 2.6:1.
- **Pop-ups:** hover-only labels.
- **Motion:** continuous animation, and flashes that can exceed three a second.
- **Targets:** smaller than 44px.
- **Theme games:** one drag-only, and one that opens a tab by itself.
- **Narrow screens:** older projects simply dropped from the page.

The page only honoured the OS reduced-motion setting.

The public HTML CV (ADR 0003) is a stored file served with a Content Security Policy that allows no scripts. Its typography copies the printed Word CV, with 11pt text and 1.15 line spacing, which falls short of AAA's visual presentation criterion (1.4.8).

The requirement: the site must be configurable to meet AAA **without removing what doesn't conform**. Visitors who need it get a conforming site; everyone else keeps the design.

## Decision

### 1. A conforming alternate version, reached from the first control on the page

WCAG's conformance requirement 1 allows a page that doesn't conform, if a conforming alternate version can be reached from it by an accessible mechanism. The settings dialog is that mechanism. It is the first control on the page, and it conforms itself.

**Use the most accessible settings** produces the alternate version in one click: high contrast, reduced motion, no decorations, the project list, and same-tab links. Each setting can also be chosen on its own:

- high contrast
- reduce motion and flashing
- seasonal decorations
- a plain list of projects
- open projects in a new tab

The default design is unchanged.

### 2. Settings are attributes on `<html>`, decided before the page draws

`a11y.js` is a plain script at the top of `<head>`. Each setting is decided from the URL, then the saved choice, then the OS preference, then the default. It's set as a `data-` attribute on `<html>` before any style or script runs.

- **CSS** selects on the attributes.
- **The theme kit's `calm`** reads `data-motion`, so the OS preference and the site setting go through one path. Every theme's reduced-motion rules were rewritten from `@media (prefers-reduced-motion)` to `:root[data-motion="reduce"]`, and a test keeps it that way.
- **Storage:** choices are saved in `localStorage`. Where that is blocked, they travel in the URL.

### 3. Motion and decorations reload the page

A theme can't be unloaded or stilled once it runs: its loops, canvases and timers are its own. Rather than give every theme a stop mechanism, changing either setting reloads the page. The new value is in place before the theme starts.

- **Warning beforehand:** each switch says so in its hint, so the change of context is announced before it happens (3.2.2).
- **After the reload:** the dialog reopens on the same switch, and the change is announced.

### 4. The CV page's options are CSS only

The CV page runs no scripts, and that should stay true. So its two options are checkboxes that `cv.css` reads with `:has()`:

- **Readable layout:** 1.6 line spacing, 70-character lines, and left-aligned text.
- **Dark colours.**

They are not remembered between visits, which is the cost of no scripts. They are added only to the public HTML (`RenderOptions.DisplayOptions`). The PDF, which is printed from the HTML, and the editor preview are rendered without them, and the CSS hides them in print. The stored HTML changed, so `RendererVersion` went to `html/4`.

## Consequences

**Positive**

- The site can be configured to the target level (AAA) without removing anything from the default design.
- **One path for reduced motion:** the setting and the OS preference reach every theme through `calm` and `data-motion`.
- **Fixes everyone gets, whatever their settings:**
  - The page has a real `h1`.
  - Hover labels follow 1.4.13.
  - The page never scrolls: the scene is zoomed to fit the window, and the project list scrolls inside itself. Only below half size, when someone has zoomed far in, does the page scroll, so nothing is hidden from them.
- **The CV page still runs no scripts.**

**Negative / costs**

- The default configuration doesn't conform. That's accepted under conformance requirement 1, but a visitor has to find the settings, even though the button is first in the tab order.
- Narrow screens still show only the most recent projects, by choice: the dig site stays uncluttered, and the project list (part of the alternate version) has them all.
- Changing motion or decorations reloads the page.
- The CV's options reset on every visit.
- Themes must follow the conventions in [site-accessibility.md](../site-accessibility.md): `data-motion` rules, `calm`, and no text where the digger stands on phones held sideways.
- **Publishing:** the renderer version bump re-renders stored files. Published versions keep their `html/3` files until the next publish or a backfill.
- **Verification:** "AAA" rests on measured contrast, automated checks and screenshots. There has been no screen reader testing and no specialist audit.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Make the default design conform | Means removing or flattening the sign, the themes and their games: the opposite of the requirement |
| Rely on OS settings only (`prefers-reduced-motion`, `prefers-contrast`) | Covers motion and contrast only, can't turn off decorations or show the list, and many visitors don't know the OS settings exist |
| A third-party accessibility overlay | Adds a script and its vendor, changes the page unpredictably, and overlays are widely criticised for not delivering conformance |
| Give every theme a live stop for motion | Each theme would need its loops, timers and canvases made stoppable; a reload gets the same result once |
| Scripted display options on the CV page | Would weaken the CV page's no-script policy (ADR 0003) for a small gain: remembering two checkboxes |

## Follow-ups

- Test with screen readers (VoiceOver on iOS, TalkBack, NVDA) and act on what turns up.
- Consider bringing the default design's small text to AA (the 4.1:1 label dates and tagline) without changing its look much.
- Give the Easter egg a non-drag way to pick it up, if the decorations are ever to conform by default.
