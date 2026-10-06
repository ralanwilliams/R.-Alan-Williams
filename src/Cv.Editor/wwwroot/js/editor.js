// The editor page: one input per line on the left, the rendered CV on the right.
// All document logic is in model.js (tested) and on the server; this file is the DOM.

import { api, ApiError } from './api.js';
import { CvDraft, NEUTRAL_LOCALE } from './model.js';

const $ = id => document.getElementById(id);

const TYPE_LABELS = {
  name: 'Name', headline: 'Headline', contact: 'Contact', section: 'Section', entry: 'Entry',
  subtitle: 'Organisation', location: 'Location', paragraph: 'Paragraph', bullet: 'Bullet', skill_group: 'Skill group', skill: 'Skill',
};
const PLACEHOLDERS = {
  name: 'Your name', headline: 'One-line professional title', contact: 'Email, phone, website or place',
  section: 'Section heading', entry: 'Role, degree or project', subtitle: 'Organisation', location: 'City, country',
  paragraph: 'A paragraph of text', bullet: 'An achievement or responsibility',
  skill_group: 'Group label, e.g. Languages', skill: 'A skill',
};
/** Types where Enter starts another line of the same type. */
const LIST_TYPES = new Set(['bullet', 'skill', 'contact']);
const STATUS_LABELS = { missing: 'Missing', stale: 'Out of date', hidden: 'Left out' };
const ATTRIBUTE_LABELS = { start: 'From', end: 'To', kind: 'Type' };

const state = {
  session: null,
  types: new Map(),
  locales: [],          // publishable locales, source first
  version: null,        // the VersionDto the draft is based on; null before the first save
  seedFile: null,       // the seed file the unsaved first draft came from, if any
  draft: null,
  baseline: '',         // draft fingerprint when loaded or last saved
  locale: null,
  analysis: null,
  analysisRequest: 0,
  analyzeTimer: 0,
  focusedId: null,
  busy: false,
};

// ---- helpers ---------------------------------------------------------------

function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (value == null || value === false) continue;
    if (key === 'class') node.className = value;
    else if (key === 'value') node.value = value; // a property: <textarea> ignores the attribute
    else if (key === 'dataset') Object.assign(node.dataset, value);
    else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
    else if (key in node && typeof value !== 'string') node[key] = value;
    else node.setAttribute(key, value === true ? '' : value);
  }
  node.append(...children.flat().filter(c => c != null && c !== false));
  return node;
}

const localeName = code => state.locales.find(l => l.code === code)?.name ?? code;
const sourceLocale = () => state.session.sourceLocale;
const isDirty = () => state.draft.fingerprint() !== state.baseline;
const typeOf = code => state.types.get(code) ?? { code, hasText: true, children: [] };
const formatDate = iso => new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso));

let toastTimer = 0;
function toast(message, kind = 'info') {
  const box = $('toast');
  box.textContent = message;
  box.dataset.kind = kind;
  box.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { box.hidden = true; }, kind === 'error' ? 9000 : 4000);
}

function confirmDialog(title, message, okLabel = 'OK') {
  const dialog = $('confirm-dialog');
  $('confirm-title').textContent = title;
  $('confirm-message').textContent = message;
  $('confirm-ok').textContent = okLabel;
  dialog.returnValue = '';
  dialog.showModal();
  return new Promise(resolve => dialog.addEventListener('close', () => resolve(dialog.returnValue === 'ok'), { once: true }));
}

async function busy(work) {
  if (state.busy) return;
  state.busy = true;
  updateButtons();
  try {
    await work();
  } catch (error) {
    reportError(error);
  } finally {
    state.busy = false;
    updateButtons();
  }
}

function reportError(error) {
  if (error instanceof ApiError) {
    if (error.code === 'invalid') renderIssues(error.problem.issues ?? []);
    toast(error.message, 'error');
  } else {
    console.error(error);
    toast(error.message ?? String(error), 'error');
  }
}

// ---- loading ---------------------------------------------------------------

async function start() {
  try {
    state.session = await api.session();
  } catch (error) {
    document.body.replaceChildren(el('main', { class: 'fatal' },
      el('h1', {}, 'The editor cannot start'),
      el('p', {}, error.message),
      el('p', {}, 'See docs/cv-editor.md for setup and troubleshooting.')));
    return;
  }
  state.types = new Map(state.session.nodeTypes.map(t => [t.code, t]));
  state.locales = state.session.locales.filter(l => l.isPublishable);
  state.locale = sourceLocale();
  load(await api.currentDocument());
}

function load({ version, document, seedFile = null }) {
  state.version = version;
  state.seedFile = seedFile;
  state.draft = new CvDraft(document, { sourceLocale: sourceLocale() });
  state.baseline = state.draft.fingerprint();
  state.analysis = null;
  renderIssues([]);
  renderAll();
  analyzeSoon(0);
}

// ---- analysis and preview ----------------------------------------------------

function analyzeSoon(delay = 300) {
  clearTimeout(state.analyzeTimer);
  state.analyzeTimer = setTimeout(analyze, delay);
}

async function analyze() {
  const request = ++state.analysisRequest;
  try {
    const result = await api.analyze(state.version?.id ?? null, state.draft.toJSON(), state.locale);
    if (request !== state.analysisRequest) return; // a newer request is under way
    state.analysis = result;
    showPreview(result.html);
    renderIssues(result.issues);
    renderTabs();
    updateLineStatuses();
    renderStatus();
  } catch (error) {
    if (request !== state.analysisRequest) return;
    if (error instanceof ApiError && error.code === 'conflict') await handleConflict(error);
    else if (error instanceof ApiError && error.code === 'invalid') renderIssues(error.problem.issues ?? []);
    else reportError(error);
  }
}

function showPreview(html) {
  const frame = $('preview');
  const scroll = frame.contentWindow?.scrollY ?? 0;
  frame.addEventListener('load', () => {
    const doc = frame.contentDocument;
    frame.contentWindow.scrollTo(0, scroll);
    doc.addEventListener('click', event => {
      const target = event.target.closest('[data-node-id]');
      if (target) {
        event.preventDefault(); // links in the preview should not navigate
        focusLine(target.dataset.nodeId);
      }
    });
    highlightInPreview(state.focusedId);
  }, { once: true });
  frame.srcdoc = html;
}

function highlightInPreview(nodeId) {
  const doc = $('preview').contentDocument;
  if (!doc) return;
  doc.querySelectorAll('.is-focused').forEach(n => n.classList.remove('is-focused'));
  const target = nodeId && doc.querySelector(`[data-node-id="${CSS.escape(nodeId)}"]`);
  if (target) {
    target.classList.add('is-focused');
    target.scrollIntoView({ block: 'nearest' });
  }
}

// ---- rendering -------------------------------------------------------------

function renderAll() {
  renderTabs();
  renderLines();
  renderAddBar();
  renderStatus();
  updateButtons();
}

function renderTabs() {
  const readiness = new Map((state.analysis?.readiness ?? []).map(r => [r.locale, r]));
  $('locale-tabs').replaceChildren(...state.locales.map(locale => {
    const r = readiness.get(locale.code);
    return el('button', {
      type: 'button', role: 'tab', class: 'tab',
      'aria-selected': String(locale.code === state.locale),
      onclick: () => switchLocale(locale.code),
    },
    el('span', {}, locale.name),
    r?.missing ? el('span', { class: 'badge badge-missing', title: `${r.missing} missing` }, String(r.missing)) : null,
    r?.stale ? el('span', { class: 'badge badge-stale', title: `${r.stale} out of date` }, String(r.stale)) : null);
  }));
}

function switchLocale(code) {
  if (code === state.locale) return;
  state.locale = code;
  renderAll();
  updateLineStatuses();
  analyzeSoon(0);
}

function renderLines() {
  const lines = state.draft.lines();
  $('lines').replaceChildren(...lines.map(({ node, depth }) => lineElement(node, depth)));
  updateLineStatuses();
}

function lineElement(node, depth) {
  const type = typeOf(node.type);
  const { draft, locale } = state;
  const isSource = locale === sourceLocale();
  const shared = draft.isShared(node.id);
  const omitted = draft.isOmitted(node.id, locale);
  const label = TYPE_LABELS[node.type] ?? node.type;

  const line = el('li', { class: 'line', dataset: { id: node.id, type: node.type } });
  line.style.setProperty('--depth', depth);
  line.classList.toggle('is-omitted', omitted);

  const main = el('div', { class: 'line-main' });
  if (type.hasText) {
    const value = isSource
      ? draft.text(node.id, shared ? NEUTRAL_LOCALE : locale)?.content ?? ''
      : omitted ? '' : draft.text(node.id, locale)?.content ?? '';
    const source = shared ? draft.text(node.id, NEUTRAL_LOCALE)?.content : draft.text(node.id, sourceLocale())?.content;
    // In a translation, the source text is shown under the input, and shared text is the fallback.
    const placeholder = omitted ? `Left out of ${localeName(locale)}`
      : isSource ? PLACEHOLDERS[node.type] ?? label
      : shared ? source ?? ''
      : `${localeName(locale)} translation`;

    const control = el(node.type === 'paragraph' ? 'textarea' : 'input', {
      class: 'line-input',
      value,
      placeholder,
      disabled: omitted,
      rows: node.type === 'paragraph' ? 2 : null,
      'aria-label': `${label} (${localeName(locale)})`,
      oninput: event => onTextInput(node.id, event.target.value),
      onkeydown: event => onLineKey(event, node),
      onfocus: () => setFocused(node.id),
    });
    if (control.tagName === 'INPUT') control.type = 'text';
    main.append(control);

    if (!isSource && source && !shared) {
      main.append(el('p', { class: 'line-source' }, el('span', { class: 'line-source-tag' }, sourceLocale().toUpperCase()), source));
    }
  } else {
    main.append(el('span', { class: 'line-structural' }, label));
  }

  const rules = state.session.attributes[node.type] ?? [];
  if (rules.length) main.append(attributeControls(node, rules));

  line.append(
    el('span', { class: 'line-type', title: type.description ?? '' }, label),
    main,
    el('span', { class: 'chips' }),
    el('button', {
      type: 'button', class: 'line-menu-button', 'aria-label': `Actions for this ${label.toLowerCase()}`, title: 'Line actions',
      onclick: event => openLineMenu(node.id, event.currentTarget),
    }, '⋯'));
  return line;
}

function attributeControls(node, rules) {
  const box = el('div', { class: 'attrs' });
  for (const rule of rules) {
    const value = node.attrs[rule.key] ?? '';
    const onchange = event => {
      state.draft.setAttr(node.id, rule.key, event.target.value.trim());
      changed();
    };
    if (rule.kind === 'choice') {
      box.append(el('label', { class: 'attr' }, el('span', {}, ATTRIBUTE_LABELS[rule.key] ?? rule.key),
        el('select', { onchange },
          el('option', { value: '', selected: value === '' }, '—'),
          ...rule.options.map(option => el('option', { value: option, selected: option === value }, option)))));
    } else {
      box.append(el('label', { class: 'attr' }, el('span', {}, ATTRIBUTE_LABELS[rule.key] ?? rule.key),
        el('input', {
          type: 'text', value, inputmode: 'numeric', maxlength: 7, size: 7,
          placeholder: rule.key === 'end' ? 'present' : '2021-03',
          pattern: '\\d{4}(-(0[1-9]|1[0-2]))?', oninput: onchange,
        })));
    }
  }
  return box;
}

function updateLineStatuses() {
  const status = state.analysis?.nodeStatus ?? {};
  for (const line of $('lines').children) {
    const id = line.dataset.id;
    const flag = status[id]?.[state.locale];
    const chips = [];
    if (flag) chips.push(el('span', { class: `chip chip-${flag}` }, STATUS_LABELS[flag]));
    if (state.draft.isShared(id) && !state.draft.text(id, state.locale)) chips.push(el('span', { class: 'chip chip-shared', title: 'The same text in every language' }, 'All languages'));
    line.querySelector('.chips').replaceChildren(...chips);
    line.classList.toggle('is-missing', flag === 'missing');
  }
}

function renderAddBar() {
  const rootType = typeOf(state.draft.node(state.draft.rootId)?.type ?? 'root');
  $('add-bar').replaceChildren(...rootType.children.map(type => el('button', {
    type: 'button', class: 'button ghost small',
    onclick: () => structural(() => state.draft.appendChild(state.draft.rootId, type)),
  }, `+ ${TYPE_LABELS[type] ?? type}`)));
}

function renderIssues(issues) {
  const box = $('issues');
  box.hidden = issues.length === 0;
  box.replaceChildren(
    el('p', { class: 'issues-title' }, issues.length === 1 ? 'Fix this before saving:' : `Fix these ${issues.length} problems before saving:`),
    el('ul', {}, ...issues.map(issue => el('li', {},
      issue.nodeId
        ? el('button', { type: 'button', class: 'link', onclick: () => focusLine(issue.nodeId) }, issue.message)
        : issue.message))));
}

function renderStatus() {
  const { version } = state;
  const dirty = isDirty();
  let text;
  if (!version) text = state.seedFile ? `New CV from ${state.seedFile} · not saved yet` : 'New CV · not saved yet';
  else {
    text = `v${version.number}`;
    text += dirty ? ' · unsaved changes' : ` · saved ${formatDate(version.createdAt)}`;
    if (version.publishedLocales.length) text += ` · live in ${version.publishedLocales.map(localeName).join(', ')}`;
  }
  $('status').textContent = text;
  $('status').classList.toggle('is-dirty', dirty);
}

function updateButtons() {
  const dirty = state.draft ? isDirty() : false;
  $('save-button').disabled = state.busy || !(dirty || !state.version);
  $('discard-button').disabled = state.busy || !dirty;
  $('publish-button').disabled = state.busy || !state.draft;
  $('pdf-button').disabled = state.busy || !state.draft;
  $('history-button').disabled = state.busy;
}

// ---- editing ---------------------------------------------------------------

function changed() {
  renderStatus();
  updateButtons();
  analyzeSoon();
}

function onTextInput(nodeId, value) {
  const { draft, locale } = state;
  if (locale === sourceLocale() && draft.isShared(nodeId)) draft.setText(nodeId, NEUTRAL_LOCALE, value);
  else draft.setText(nodeId, locale, value);
  changed();
}

/** Applies a structural change, re-renders, and focuses the line it returns (if any). */
function structural(change, focusId) {
  const result = change();
  renderLines();
  const target = focusId ?? (typeof result === 'string' ? result : state.focusedId);
  if (target) focusLine(target);
  changed();
}

function setFocused(nodeId) {
  state.focusedId = nodeId;
  for (const line of $('lines').children) line.classList.toggle('is-focused', line.dataset.id === nodeId);
  highlightInPreview(nodeId);
}

function focusLine(nodeId, { atEnd = true } = {}) {
  const line = $('lines').querySelector(`[data-id="${CSS.escape(nodeId)}"]`);
  if (!line) return;
  const input = line.querySelector('.line-input:not(:disabled), input, select, button');
  input?.focus();
  if (atEnd && input?.setSelectionRange && typeof input.value === 'string') input.setSelectionRange(input.value.length, input.value.length);
  line.scrollIntoView({ block: 'nearest' });
  setFocused(nodeId);
}

function neighbourLine(nodeId, delta) {
  const order = state.draft.lines().map(l => l.node.id);
  return order[order.indexOf(nodeId) + delta] ?? null;
}

function onLineKey(event, node) {
  const { draft } = state;
  const input = event.target;
  const isTextarea = input.tagName === 'TEXTAREA';

  if (event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
    event.preventDefault();
    const delta = event.key === 'ArrowUp' ? -1 : 1;
    if (draft.canMove(node.id, delta)) structural(() => draft.move(node.id, delta), node.id);
    return;
  }
  if (event.key === 'Enter' && !isTextarea && !event.shiftKey) {
    event.preventDefault();
    if (LIST_TYPES.has(node.type)) structural(() => draft.insertAfter(node.id, node.type));
    else if (neighbourLine(node.id, 1)) focusLine(neighbourLine(node.id, 1));
    return;
  }
  if (event.key === 'Backspace' && input.value === '' && draft.childrenOf(node.id).length === 0
      && (LIST_TYPES.has(node.type) || node.type === 'paragraph') && state.locale === sourceLocale()) {
    event.preventDefault();
    structural(() => draft.remove(node.id));
    return;
  }
  if (!isTextarea && !event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
    const next = neighbourLine(node.id, event.key === 'ArrowUp' ? -1 : 1);
    if (next) {
      event.preventDefault();
      focusLine(next);
    }
  }
}

// ---- line menu -------------------------------------------------------------

function openLineMenu(nodeId, anchor) {
  const { draft, locale } = state;
  const node = draft.node(nodeId);
  const type = typeOf(node.type);
  const parentType = typeOf(draft.node(draft.parentOf(nodeId)).type);
  const isSource = locale === sourceLocale();
  const status = state.analysis?.nodeStatus?.[nodeId]?.[locale];
  const nested = draft.subtree(nodeId).length - 1;
  const menu = $('line-menu');

  const item = (label, action, { danger = false, disabled = false } = {}) => el('button', {
    type: 'button', class: danger ? 'menu-item danger' : 'menu-item', role: 'menuitem', disabled,
    onclick: () => { menu.hidePopover(); action(); },
  }, label);
  const group = (title, ...items) => {
    const present = items.filter(Boolean);
    return present.length ? el('div', { class: 'menu-group', role: 'group' }, el('p', { class: 'menu-title' }, title), ...present) : null;
  };

  menu.replaceChildren(...[
    group('Add after this line', ...parentType.children.map(t => item(TYPE_LABELS[t] ?? t, () => structural(() => draft.insertAfter(nodeId, t))))),
    group('Add inside', ...type.children.map(t => item(TYPE_LABELS[t] ?? t, () => structural(() => draft.appendChild(nodeId, t))))),
    group('Arrange',
      item('Move up', () => structural(() => draft.move(nodeId, -1), nodeId), { disabled: !draft.canMove(nodeId, -1) }),
      item('Move down', () => structural(() => draft.move(nodeId, 1), nodeId), { disabled: !draft.canMove(nodeId, 1) })),
    group(localeName(locale),
      type.hasText && isSource
        ? item(draft.isShared(nodeId) ? 'Use separate text per language' : 'Use the same text in every language',
          () => structural(() => draft.setShared(nodeId, !draft.isShared(nodeId)), nodeId))
        : null,
      type.hasText && !isSource && status === 'stale'
        ? item('Mark translation as up to date', () => { draft.markReviewed(nodeId, locale); changed(); })
        : null,
      item(draft.isOmitted(nodeId, locale) ? `Include in ${localeName(locale)}` : `Leave out of ${localeName(locale)}`,
        () => structural(() => draft.setOmitted(nodeId, locale, !draft.isOmitted(nodeId, locale)), nodeId))),
    group('Delete', item(nested ? `Delete line and ${nested} below it` : 'Delete line', async () => {
      if (nested && !await confirmDialog('Delete these lines?', `This removes the line and the ${nested} nested under it, in every language.`, 'Delete')) return;
      structural(() => draft.remove(nodeId));
    }, { danger: true })),
  ].filter(Boolean));

  menu.showPopover();
  const rect = anchor.getBoundingClientRect();
  const top = Math.min(rect.bottom + 4, window.innerHeight - menu.offsetHeight - 8);
  menu.style.top = `${Math.max(8, top)}px`;
  menu.style.left = `${Math.max(8, rect.right - menu.offsetWidth)}px`;
}

// ---- saving and publishing -------------------------------------------------

function openSaveDialog({ publish, version = null }) {
  const dialog = $('save-dialog');
  const publishingExisting = version !== null;
  $('save-title').textContent = publishingExisting ? `Publish v${version.number}` : publish ? 'Save and publish' : 'Save a new version';
  $('save-confirm').textContent = publishingExisting ? 'Publish' : publish ? 'Save and publish' : 'Save';
  $('save-summary').closest('.field').hidden = publishingExisting;
  $('save-summary').value = '';
  $('publish-locales').hidden = !publish;

  const readiness = new Map((publishingExisting ? [] : state.analysis?.readiness ?? []).map(r => [r.locale, r]));
  $('publish-locale-list').replaceChildren(...state.locales.map(locale => {
    const r = readiness.get(locale.code);
    const note = !r ? '' : !r.canPublish ? `${r.missing} missing` : r.stale ? `${r.stale} out of date` : 'ready';
    return el('label', { class: 'check' },
      el('input', { type: 'checkbox', name: 'locale', value: locale.code, checked: !r || r.canPublish, disabled: r ? !r.canPublish : false }),
      el('span', {}, locale.name),
      note ? el('small', { class: r && !r.canPublish ? 'note-bad' : r?.stale ? 'note-warn' : 'note-ok' }, note) : null);
  }));

  $('save-cancel').onclick = () => dialog.close();
  $('save-form').onsubmit = event => {
    event.preventDefault();
    const locales = [...dialog.querySelectorAll('input[name=locale]:checked')].map(i => i.value);
    if (publish && locales.length === 0) {
      toast('Choose at least one language to publish.', 'error');
      return;
    }
    dialog.close();
    const summary = $('save-summary').value.trim() || null;
    busy(() => publishingExisting ? publishVersion(version.id, locales) : save({ summary, publish: publish ? locales : [] }));
  };
  dialog.showModal();
  (publishingExisting ? $('save-confirm') : $('save-summary')).focus();
}

async function save({ summary, publish }, confirmStale = false) {
  try {
    const result = await api.save({
      baseVersionId: state.version?.id ?? null,
      document: state.draft.toJSON(),
      summary,
      publish,
      confirmStale,
    });
    // A "save & publish" with nothing new publishes the base version and returns the history.
    const version = result.version ?? result.versions.find(v => v.id === state.version.id);
    state.version = version;
    state.draft.clearReviewMarks();
    state.baseline = state.draft.fingerprint();
    toast(result.version
      ? `Saved v${version.number}${publish.length ? ` and published ${publish.map(localeName).join(', ')}` : ''}.`
      : `Published v${version.number} in ${publish.map(localeName).join(', ')}.`);
    renderStatus();
    analyzeSoon(0);
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    if (error.code === 'confirm-stale' && await confirmDialog(error.problem.title, error.message, 'Publish anyway')) {
      return save({ summary, publish }, true);
    }
    if (error.code === 'conflict') return handleConflict(error);
    if (error.code !== 'confirm-stale') throw error;
  }
}

async function publishVersion(versionId, locales, confirmStale = false) {
  try {
    const history = await api.publish(versionId, locales, confirmStale);
    applyHistory(history);
    toast(versionId ? `Published in ${locales.map(localeName).join(', ')}.` : `Unpublished ${locales.map(localeName).join(', ')}.`);
  } catch (error) {
    if (error instanceof ApiError && error.code === 'confirm-stale') {
      if (await confirmDialog(error.problem.title, error.message, 'Publish anyway')) return publishVersion(versionId, locales, true);
      return;
    }
    throw error;
  }
}

async function handleConflict(error) {
  const reload = await confirmDialog(error.problem.title ?? 'Saved somewhere else',
    `${error.message} Your unsaved changes in this tab will be lost; copy anything you need first.`, 'Reload');
  if (reload) load(await api.currentDocument());
}

async function discard() {
  if (!await confirmDialog('Discard changes?', 'Go back to the last saved version. Unsaved changes are lost.', 'Discard')) return;
  load(state.version ? await api.version(state.version.id) : await api.currentDocument());
  toast('Changes discarded.');
}

async function downloadPdf() {
  const { blob, fileName } = await api.pdf(state.version?.id ?? null, state.draft.toJSON(), state.locale);
  const url = URL.createObjectURL(blob);
  const link = el('a', { href: url, download: fileName });
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

// ---- history ---------------------------------------------------------------

let history = null;

function applyHistory(data) {
  history = data;
  if (state.version) {
    state.version = data.versions.find(v => v.id === state.version.id) ?? state.version;
  }
  renderStatus();
  if ($('history-dialog').open) renderHistory();
}

async function openHistory() {
  const data = await api.history();
  $('changes').replaceChildren(el('p', { class: 'muted' }, 'Choose a version to see what changed in it.'));
  $('history-dialog').showModal();
  applyHistory(data); // renders, now that the dialog is open
}

function renderHistory() {
  const published = new Map(history.published.map(p => [p.locale, p]));
  $('live-now').replaceChildren(
    el('p', { class: 'live-title' }, 'Live now'),
    el('ul', { class: 'live-list' }, ...state.locales.map(locale => {
      const p = published.get(locale.code);
      return el('li', {},
        el('strong', {}, locale.name), ' ',
        p ? `v${p.versionNumber}, since ${formatDate(p.publishedAt)}` : el('span', { class: 'muted' }, 'not published'),
        p ? el('button', {
          type: 'button', class: 'button ghost small',
          onclick: async () => {
            if (await confirmDialog(`Unpublish ${locale.name}?`, `/cv/${locale.code}.pdf will stop serving a CV until you publish again.`, 'Unpublish')) {
              busy(() => publishVersion(null, [locale.code]));
            }
          },
        }, 'Unpublish') : null);
    })));

  $('versions').replaceChildren(...history.versions.map(version => {
    const previous = history.versions.find(v => v.number === version.number - 1);
    return el('li', { class: version.id === state.version?.id ? 'version is-current' : 'version' },
      el('div', { class: 'version-head' },
        el('strong', {}, `v${version.number}`),
        el('span', { class: 'muted' }, formatDate(version.createdAt)),
        ...version.publishedLocales.map(code => el('span', { class: 'chip chip-live' }, `live · ${code}`))),
      el('p', { class: 'version-summary' }, version.summary ?? (version.restoredFromNumber ? `Restored from v${version.restoredFromNumber}` : el('span', { class: 'muted' }, 'No summary'))),
      el('div', { class: 'version-actions' },
        el('button', { type: 'button', class: 'button ghost small', onclick: () => showChanges(previous, version) }, 'Changes'),
        el('button', { type: 'button', class: 'button ghost small', onclick: () => openSaveDialog({ publish: true, version }) }, 'Publish…'),
        version.id !== history.versions[0]?.id
          ? el('button', { type: 'button', class: 'button ghost small', onclick: () => restore(version) }, 'Restore')
          : null));
  }));
}

async function showChanges(previous, version) {
  const box = $('changes');
  if (!previous) {
    box.replaceChildren(el('p', { class: 'muted' }, `v${version.number} is the first version.`));
    return;
  }
  const changes = await api.changes(previous.id, version.id);
  const describe = c => {
    const what = c.label ? `“${c.label}”` : TYPE_LABELS[c.nodeType] ?? 'a line';
    switch (c.change) {
      case 'added': return `Added ${TYPE_LABELS[c.nodeType]?.toLowerCase() ?? 'line'} ${what}`;
      case 'removed': return `Removed ${what}`;
      case 'moved': return `Moved ${what}`;
      case 'retyped': return `Changed ${what} from ${c.oldValue} to ${c.newValue}`;
      case 'attrs_changed': return `Changed the dates or details of ${what}`;
      case 'text_added': return `${localeName(c.locale)}: wrote ${c.newValue ? `“${c.newValue}”` : what}`;
      case 'text_removed': return `${localeName(c.locale)}: removed text of ${what}`;
      case 'text_edited': return `${localeName(c.locale)}: “${c.oldValue}” → “${c.newValue}”`;
      case 'omitted': return `${localeName(c.locale)}: left out ${what}`;
      case 'unomitted': return `${localeName(c.locale)}: included ${what} again`;
      default: return `${c.change} ${what}`;
    }
  };
  // Adding a line also reports its new text; keep only the structural row for added lines.
  const added = new Set(changes.filter(c => c.change === 'added').map(c => c.nodeId));
  const visible = changes.filter(c => !(added.has(c.nodeId) && c.change === 'text_added'));
  box.replaceChildren(
    el('h3', {}, `v${previous.number} → v${version.number}`),
    visible.length
      ? el('ul', { class: 'change-list' }, ...visible.map(c => el('li', { class: `change change-${c.change}` }, describe(c))))
      : el('p', { class: 'muted' }, 'No differences.'));
}

async function restore(version) {
  if (isDirty() && !await confirmDialog('Discard unsaved changes?', 'Restoring loads the restored version; unsaved changes in this tab are lost.', 'Restore')) return;
  await busy(async () => {
    try {
      const result = await api.restore(version.id, state.version?.id ?? null);
      load(await api.version(result.version.id));
      $('history-dialog').close();
      toast(`Restored v${version.number} as v${result.version.number}. It is not published until you publish it.`);
    } catch (error) {
      if (error instanceof ApiError && error.code === 'conflict') return handleConflict(error);
      throw error;
    }
  });
}

// ---- wiring ----------------------------------------------------------------

$('save-button').addEventListener('click', () => openSaveDialog({ publish: false }));
$('publish-button').addEventListener('click', () => openSaveDialog({ publish: true }));
$('discard-button').addEventListener('click', () => busy(discard));
$('pdf-button').addEventListener('click', () => busy(downloadPdf));
$('history-button').addEventListener('click', () => busy(openHistory));
$('history-close').addEventListener('click', () => $('history-dialog').close());

document.addEventListener('keydown', event => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
    event.preventDefault();
    if (!$('save-button').disabled) openSaveDialog({ publish: false });
  }
});

window.addEventListener('beforeunload', event => {
  if (state.draft && isDirty()) event.preventDefault();
});

start();
