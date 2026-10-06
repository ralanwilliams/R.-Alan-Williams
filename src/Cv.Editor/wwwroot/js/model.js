// The editor's working copy of the CV: a tree of nodes plus text per node per locale.
// It mirrors Cv.Core.Drafts.Draft and contains no DOM code, so it is unit tested with
// `node --test "tests/Cv.Editor.Js/*.test.mjs"`. Sort keys and translation source hashes are not
// handled here: the server derives them from the base version (ADR 0002).

export const NEUTRAL_LOCALE = 'zxx';

/** A time-ordered UUID (RFC 9562 version 7), so a new line has its stable id before it is saved (ADR 0001 §12). */
export function uuidv7(now = Date.now(), random = crypto.getRandomValues(new Uint8Array(16))) {
  const bytes = Uint8Array.from(random);
  const timestamp = BigInt(now);
  for (let i = 0; i < 6; i++) {
    bytes[i] = Number((timestamp >> BigInt(8 * (5 - i))) & 0xffn);
  }
  bytes[6] = (bytes[6] & 0x0f) | 0x70; // version 7
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // RFC 4122 variant
  const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

const textKey = (nodeId, locale) => `${nodeId}|${locale}`;

export class CvDraft {
  /**
   * @param {{nodes: Array<{id: string, parentId: string|null, type: string, attrs?: object}>,
   *          texts: Array<{nodeId: string, locale: string, content: string|null, omitted?: boolean}>}} draft
   * @param {{sourceLocale: string, newId?: () => string}} options
   */
  constructor(draft, { sourceLocale, newId = () => uuidv7() }) {
    this.sourceLocale = sourceLocale;
    this.newId = newId;
    this.nodes = new Map();
    this.children = new Map();
    this.texts = new Map();
    this.rootId = null;

    for (const node of draft.nodes) {
      this.nodes.set(node.id, { id: node.id, parentId: node.parentId ?? null, type: node.type, attrs: { ...(node.attrs ?? {}) } });
      this.children.set(node.id, this.children.get(node.id) ?? []);
      if (node.parentId == null) {
        this.rootId ??= node.id;
      } else {
        if (!this.children.has(node.parentId)) this.children.set(node.parentId, []);
        this.children.get(node.parentId).push(node.id);
      }
    }
    for (const text of draft.texts) {
      this.texts.set(textKey(text.nodeId, text.locale), {
        content: text.content ?? '',
        omitted: Boolean(text.omitted),
        reviewed: false,
      });
    }
  }

  node(id) { return this.nodes.get(id); }
  parentOf(id) { return this.nodes.get(id)?.parentId ?? null; }
  childrenOf(id) { return [...(this.children.get(id) ?? [])]; }

  /** Every node except the root, in document order, with its depth (the root's children are depth 0). */
  lines() {
    const result = [];
    const walk = (id, depth) => {
      for (const child of this.children.get(id) ?? []) {
        result.push({ node: this.nodes.get(child), depth });
        walk(child, depth + 1);
      }
    };
    if (this.rootId) walk(this.rootId, 0);
    return result;
  }

  /** The node and all its descendants, in document order. */
  subtree(id) {
    const ids = [id];
    for (const child of this.children.get(id) ?? []) ids.push(...this.subtree(child));
    return ids;
  }

  // ---- text ---------------------------------------------------------------

  text(id, locale) { return this.texts.get(textKey(id, locale)); }

  /** True when the line's text is language-neutral (stored under zxx and shown in every locale). */
  isShared(id) { return this.texts.has(textKey(id, NEUTRAL_LOCALE)); }

  /** The text shown for a line in a locale: its own, else the shared text. */
  displayText(id, locale) {
    const own = this.text(id, locale);
    if (own?.omitted) return '';
    return own?.content || this.text(id, NEUTRAL_LOCALE)?.content || '';
  }

  setText(id, locale, content) {
    const key = textKey(id, locale);
    // An empty translation means "no text in this locale". A shared line keeps its (empty)
    // neutral entry so it stays shared while being retyped.
    if (content === '' && locale !== NEUTRAL_LOCALE) {
      this.texts.delete(key);
      return;
    }
    this.texts.set(key, { content, omitted: false, reviewed: false });
  }

  setOmitted(id, locale, omitted) {
    const key = textKey(id, locale);
    if (omitted) this.texts.set(key, { content: '', omitted: true, reviewed: false });
    else this.texts.delete(key);
  }

  isOmitted(id, locale) { return Boolean(this.text(id, locale)?.omitted); }

  /** Moves the line's text between the source locale and the language-neutral slot. Translations are kept. */
  setShared(id, shared) {
    if (shared === this.isShared(id)) return;
    if (shared) {
      const content = this.text(id, this.sourceLocale)?.content ?? '';
      this.texts.delete(textKey(id, this.sourceLocale));
      this.texts.set(textKey(id, NEUTRAL_LOCALE), { content, omitted: false, reviewed: false });
    } else {
      const content = this.text(id, NEUTRAL_LOCALE)?.content ?? '';
      this.texts.delete(textKey(id, NEUTRAL_LOCALE));
      if (content !== '') this.texts.set(textKey(id, this.sourceLocale), { content, omitted: false, reviewed: false });
    }
  }

  /** "This translation still matches the source text": the server records the current source hash for it. */
  markReviewed(id, locale) {
    const entry = this.text(id, locale);
    if (entry && !entry.omitted) entry.reviewed = true;
  }

  /** After a save the marks are stored as source hashes, so the working copy starts clean again. */
  clearReviewMarks() {
    for (const entry of this.texts.values()) entry.reviewed = false;
  }

  // ---- attributes ---------------------------------------------------------

  setAttr(id, key, value) {
    const attrs = this.nodes.get(id).attrs;
    if (value === '' || value == null) delete attrs[key];
    else attrs[key] = value;
  }

  // ---- structure ----------------------------------------------------------

  /** Adds a new line of `type` right after `afterId` (and after its subtree), as its next sibling. */
  insertAfter(afterId, type) {
    const parentId = this.parentOf(afterId);
    const siblings = this.children.get(parentId);
    return this.#add(parentId, type, siblings.indexOf(afterId) + 1);
  }

  appendChild(parentId, type) {
    return this.#add(parentId, type, (this.children.get(parentId) ?? []).length);
  }

  /** Removes the line, its subtree and all their text. Returns the line to focus afterwards. */
  remove(id) {
    if (id === this.rootId) throw new Error('The root cannot be removed.');
    const order = this.lines().map(l => l.node.id);
    const removed = new Set(this.subtree(id));
    const focus = order.slice(0, order.indexOf(id)).reverse().find(x => !removed.has(x))
      ?? order.find(x => !removed.has(x)) ?? null;

    const siblings = this.children.get(this.parentOf(id));
    siblings.splice(siblings.indexOf(id), 1);
    for (const nodeId of removed) {
      this.nodes.delete(nodeId);
      this.children.delete(nodeId);
    }
    for (const key of [...this.texts.keys()]) {
      if (removed.has(key.slice(0, key.indexOf('|')))) this.texts.delete(key);
    }
    return focus;
  }

  canMove(id, delta) {
    const siblings = this.children.get(this.parentOf(id)) ?? [];
    const target = siblings.indexOf(id) + delta;
    return target >= 0 && target < siblings.length;
  }

  /** Swaps the line (with its subtree) with its previous (-1) or next (+1) sibling. */
  move(id, delta) {
    if (!this.canMove(id, delta)) return false;
    const siblings = this.children.get(this.parentOf(id));
    const index = siblings.indexOf(id);
    [siblings[index], siblings[index + delta]] = [siblings[index + delta], siblings[index]];
    return true;
  }

  #add(parentId, type, index) {
    const id = this.newId();
    this.nodes.set(id, { id, parentId, type, attrs: {} });
    this.children.set(id, []);
    this.children.get(parentId).splice(index, 0, id);
    return id;
  }

  // ---- serialisation ------------------------------------------------------

  /**
   * The draft as the API expects it: nodes in document order, then texts. Empty text is
   * dropped (no text = missing). Output is deterministic, so two drafts with the same content
   * serialise identically; the editor compares them to know whether there are unsaved changes.
   */
  toJSON() {
    const nodes = [];
    const walk = id => {
      const { parentId, type, attrs } = this.nodes.get(id);
      nodes.push({ id, parentId, type, attrs: Object.fromEntries(Object.entries(attrs).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))) });
      for (const child of this.children.get(id) ?? []) walk(child);
    };
    if (this.rootId) walk(this.rootId);

    const texts = [...this.texts.entries()]
      .filter(([, t]) => t.omitted || t.content.trim() !== '')
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
      .map(([key, t]) => {
        const [nodeId, locale] = key.split('|');
        return t.omitted
          ? { nodeId, locale, content: null, omitted: true }
          : { nodeId, locale, content: t.content, omitted: false, ...(t.reviewed ? { reviewed: true } : {}) };
      });
    return { nodes, texts };
  }

  /**
   * Comparable snapshot of the content, for "are there unsaved changes?". Review marks count:
   * saving one changes the translation's stored source hash.
   */
  fingerprint() {
    return JSON.stringify(this.toJSON());
  }
}
