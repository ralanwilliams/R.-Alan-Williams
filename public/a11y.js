// Accessibility settings for the site. Loaded as a plain script in <head>, before the page
// draws, so the page and its seasonal theme see the settings from the start: each one
// becomes a data- attribute on <html>, which CSS and scripts read.
//
//   data-contrast  normal | high   stronger colours, outlines and focus rings
//   data-motion    full | reduce   no animation, moving scenery or flashing
//   data-decor     on | off        the seasonal theme
//   data-list      off | on        a plain list of every project, under the dig site
//   data-newtab    on | off        project links open in a new tab
//
// Each setting comes from the URL (?contrast=high), else the visitor's saved choice, else
// the OS (prefers-contrast, prefers-reduced-motion), else its default. Choices are saved in
// localStorage; where that is blocked they ride along in the URL instead. Motion and
// decorations can't be changed under a running theme, so changing them reloads the page.
//
// Page scripts read and change settings through window.siteSettings, and hear about
// changes from the 'site-settings' event on document.
(() => {
    const SETTINGS = [
        {
            key: 'contrast', on: 'high', off: 'normal', os: '(prefers-contrast: more)',
            label: 'High contrast', hint: 'Stronger colours, outlines and focus rings.'
        },
        {
            key: 'motion', on: 'reduce', off: 'full', os: '(prefers-reduced-motion: reduce)', reload: true,
            label: 'Reduce motion and flashing', hint: 'Stops animation, moving scenery and flashes. Reloads the page.'
        },
        {
            key: 'decor', on: 'on', off: 'off', initial: 'on', reload: true,
            label: 'Seasonal decorations', hint: 'The holiday scene, with its characters and games. Reloads the page.'
        },
        {
            key: 'list', on: 'on', off: 'off', initial: 'off',
            label: 'List of projects', hint: 'Every project as a plain list of links, under the dig site.'
        },
        {
            key: 'newtab', on: 'on', off: 'off', initial: 'on',
            label: 'Open projects in a new tab', hint: 'Off: they open in this tab.'
        },
    ];
    // The most accessible value of each, for the one-click preset
    const STRICT = { contrast: 'high', motion: 'reduce', decor: 'off', list: 'on', newtab: 'off' };
    const STORE = 'site-settings';
    const root = document.documentElement;
    const byKey = Object.fromEntries(SETTINGS.map(s => [s.key, s]));

    function load() {
        try { return JSON.parse(localStorage.getItem(STORE)) || {}; } catch { return {}; }
    }

    function store(values) {
        try {
            if (Object.keys(values).length) localStorage.setItem(STORE, JSON.stringify(values));
            else localStorage.removeItem(STORE);
            return true;
        } catch { return false; }
    }

    const valid = (s, v) => v === s.on || v === s.off;
    const fallback = s => s.os ? (matchMedia(s.os).matches ? s.on : s.off) : s.initial;
    const params = new URLSearchParams(location.search);
    const saved = load();
    const value = {};
    for (const s of SETTINGS) {
        value[s.key] = [params.get(s.key), saved[s.key]].find(v => valid(s, v)) ?? fallback(s);
        root.dataset[s.key] = value[s.key];
    }

    // Puts the settings in the URL: those already there, so they don't override the new
    // choice on the next load, and all of them when they couldn't be saved
    function updateUrl(all) {
        const url = new URL(location.href);
        for (const s of SETTINGS) {
            if (all || url.searchParams.has(s.key)) url.searchParams.set(s.key, value[s.key]);
        }
        history.replaceState(null, '', url);
    }

    // Applies new values; returns true if the page is reloading for them
    function change(values, hash) {
        let reload = false;
        for (const [key, v] of Object.entries(values)) {
            if (!byKey[key] || !valid(byKey[key], v) || value[key] === v) continue;
            value[key] = v;
            saved[key] = v;
            if (byKey[key].reload) reload = true;
            root.dataset[key] = v;
            document.dispatchEvent(new CustomEvent('site-settings', { detail: { key, value: v } }));
        }
        updateUrl(!store(saved));
        if (reload) {
            location.hash = hash;
            location.reload();
        }
        return reload;
    }

    // Back to the defaults (and the OS settings): forgets the saved choices and URL settings
    function reset() {
        for (const key of Object.keys(saved)) delete saved[key];
        store(saved);
        const url = new URL(location.href);
        SETTINGS.forEach(s => url.searchParams.delete(s.key));
        history.replaceState(null, '', url);
        const values = Object.fromEntries(SETTINGS.map(s => [s.key, fallback(s)]));
        const reload = SETTINGS.some(s => s.reload && values[s.key] !== value[s.key]);
        if (reload) {
            location.hash = 'settings-reset';
            location.reload();
            return true;
        }
        for (const s of SETTINGS) {
            if (value[s.key] === values[s.key]) continue;
            value[s.key] = values[s.key];
            root.dataset[s.key] = values[s.key];
            document.dispatchEvent(new CustomEvent('site-settings', { detail: { key: s.key, value: values[s.key] } }));
        }
        return false;
    }

    // Follow the OS while the visitor hasn't chosen, for settings that apply without a reload
    for (const s of SETTINGS) {
        if (!s.os || s.reload) continue;
        matchMedia(s.os).addEventListener('change', e => {
            if (saved[s.key] || params.has(s.key)) return;
            value[s.key] = e.matches ? s.on : s.off;
            root.dataset[s.key] = value[s.key];
            document.dispatchEvent(new CustomEvent('site-settings', { detail: { key: s.key, value: value[s.key] } }));
        });
    }

    window.siteSettings = {
        get: key => value[key],
        set: (key, v) => change({ [key]: v }, 'settings-' + key),
    };

    const ICON = `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <circle cx="12" cy="4" r="2.2" fill="currentColor"/>
        <path d="M4 8.5L12 10L20 8.5M12 10V14.5M12 14.5L8.5 21.5M12 14.5L15.5 21.5" fill="none"
              stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>`;

    // The button, top right, that opens the settings dialog
    function buildUi() {
        const opener = document.createElement('button');
        opener.type = 'button';
        opener.className = 'a11y-open';
        opener.setAttribute('aria-haspopup', 'dialog');
        opener.innerHTML = `${ICON}<span>Accessibility</span>`;

        const dialog = document.createElement('dialog');
        dialog.className = 'a11y-dialog';
        dialog.setAttribute('aria-labelledby', 'a11y-title');
        dialog.innerHTML = `<div class="a11y-body">
            <h2 id="a11y-title">Accessibility settings</h2>
            <p class="a11y-intro">Saved in this browser. Until you change them here, contrast and motion follow your device's settings.</p>
            <ul class="a11y-options">${SETTINGS.map(s => `
                <li>
                    <label for="a11y-${s.key}">${s.label}</label>
                    <span class="a11y-state" aria-hidden="true"></span>
                    <input type="checkbox" role="switch" class="a11y-switch" id="a11y-${s.key}" aria-describedby="a11y-${s.key}-hint">
                    <span class="a11y-hint" id="a11y-${s.key}-hint">${s.hint}</span>
                </li>`).join('')}
            </ul>
            <div class="a11y-actions">
                <button type="button" data-action="strict">Use the most accessible settings</button>
                <button type="button" data-action="reset">Reset to defaults</button>
                <button type="button" data-action="close">Close</button>
            </div>
            <p class="a11y-status" role="status"></p>
        </div>`;

        const boxes = Object.fromEntries(SETTINGS.map(s => [s.key, dialog.querySelector('#a11y-' + s.key)]));
        const status = dialog.querySelector('.a11y-status');
        const sync = () => SETTINGS.forEach(s => { boxes[s.key].checked = value[s.key] === s.on; });
        const describe = key => `${byKey[key].label}: ${value[key] === byKey[key].on ? 'on' : 'off'}.`;
        sync();
        document.addEventListener('site-settings', sync);

        for (const s of SETTINGS) {
            boxes[s.key].addEventListener('change', () => {
                if (!window.siteSettings.set(s.key, boxes[s.key].checked ? s.on : s.off)) status.textContent = describe(s.key);
            });
        }
        dialog.addEventListener('click', e => {
            const action = e.target.closest('button')?.dataset.action;
            if (action === 'close' || e.target === dialog) dialog.close();
            if (action === 'strict' && !change(STRICT, 'settings-strict')) status.textContent = 'The most accessible settings are on.';
            if (action === 'reset' && !reset()) status.textContent = 'Settings are back to their defaults.';
        });
        dialog.addEventListener('close', () => { status.textContent = ''; opener.focus(); });
        opener.addEventListener('click', () => dialog.showModal());

        // Escape hides the button's tooltip until the pointer or focus leaves the button
        document.addEventListener('keydown', e => {
            if (e.key === 'Escape' && !dialog.open && opener.matches(':hover, :focus-visible')) opener.classList.add('quiet');
        });
        opener.addEventListener('pointerleave', () => opener.classList.remove('quiet'));
        opener.addEventListener('blur', () => opener.classList.remove('quiet'));

        document.body.prepend(opener);
        document.body.append(dialog);

        // Back from a reload made by a setting: reopen the dialog where the visitor was
        const back = /^#settings-(\w+)$/.exec(location.hash)?.[1];
        if (back) {
            history.replaceState(null, '', location.pathname + location.search);
            dialog.showModal();
            if (byKey[back]) {
                boxes[back].focus();
                status.textContent = describe(back);
            } else {
                dialog.querySelector(`[data-action="${back}"]`)?.focus();
                status.textContent = back === 'strict' ? 'The most accessible settings are on.' : 'Settings are back to their defaults.';
            }
        }
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', buildUi);
    else buildUi();
})();
