// Unit tests for the editor's document model (src/Cv.Editor/wwwroot/js/model.js).
// Run: node --test "tests/Cv.Editor.Js/*.test.mjs"

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CvDraft, NEUTRAL_LOCALE, uuidv7 } from '../../src/Cv.Editor/wwwroot/js/model.js';

// A small CV: root > name (shared), section > entry > bullet, bullet
function sample() {
  let next = 0;
  const draft = new CvDraft({
    nodes: [
      { id: 'root', parentId: null, type: 'root' },
      { id: 'name', parentId: 'root', type: 'name' },
      { id: 'section', parentId: 'root', type: 'section' },
      { id: 'entry', parentId: 'section', type: 'entry', attrs: { start: '2021-03' } },
      { id: 'b1', parentId: 'entry', type: 'bullet' },
      { id: 'b2', parentId: 'entry', type: 'bullet' },
    ],
    texts: [
      { nodeId: 'name', locale: 'zxx', content: 'Ada Lovelace' },
      { nodeId: 'section', locale: 'en', content: 'Experience' },
      { nodeId: 'section', locale: 'nb', content: 'Erfaring' },
      { nodeId: 'b1', locale: 'en', content: 'First' },
      { nodeId: 'b2', locale: 'en', content: 'Second' },
    ],
  }, { sourceLocale: 'en', newId: () => `new${++next}` });
  return draft;
}

const order = draft => draft.lines().map(l => l.node.id);

test('uuidv7 has the version and variant bits and sorts by time', () => {
  const pattern = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
  const earlier = uuidv7(1_700_000_000_000);
  const later = uuidv7(1_700_000_000_001);
  assert.match(earlier, pattern);
  assert.ok(earlier < later);
  assert.equal(uuidv7(0x0123456789ab, new Uint8Array(16)).slice(0, 13), '01234567-89ab');
});

test('lines are in document order with depth, excluding the root', () => {
  const draft = sample();
  assert.deepEqual(draft.lines().map(l => [l.node.id, l.depth]),
    [['name', 0], ['section', 0], ['entry', 1], ['b1', 2], ['b2', 2]]);
});

test('insertAfter adds a sibling after the whole subtree; appendChild adds a last child', () => {
  const draft = sample();
  const afterEntry = draft.insertAfter('entry', 'paragraph');
  const lastBullet = draft.appendChild('entry', 'bullet');
  assert.deepEqual(order(draft), ['name', 'section', 'entry', 'b1', 'b2', lastBullet, afterEntry]);
  assert.equal(draft.node(afterEntry).parentId, 'section');
});

test('remove deletes the subtree and its text, and says which line to focus', () => {
  const draft = sample();
  const focus = draft.remove('entry');
  assert.equal(focus, 'section');
  assert.deepEqual(order(draft), ['name', 'section']);
  assert.equal(draft.text('b1', 'en'), undefined);
  assert.throws(() => draft.remove('root'));
});

test('move swaps with a sibling and refuses to leave the parent', () => {
  const draft = sample();
  assert.equal(draft.canMove('b1', -1), false);
  assert.equal(draft.move('b2', -1), true);
  assert.deepEqual(draft.childrenOf('entry'), ['b2', 'b1']);
  assert.equal(draft.move('b2', -1), false);
});

test('shared text moves between the source locale and zxx, keeping translations', () => {
  const draft = sample();
  draft.setShared('section', true);
  assert.equal(draft.text('section', 'en'), undefined);
  assert.equal(draft.text('section', NEUTRAL_LOCALE).content, 'Experience');
  assert.equal(draft.text('section', 'nb').content, 'Erfaring');
  assert.equal(draft.displayText('section', 'fr'), 'Experience');

  draft.setShared('section', false);
  assert.equal(draft.text('section', 'en').content, 'Experience');
  assert.equal(draft.isShared('section'), false);
});

test('clearing a translation removes it, so the line falls back or becomes missing', () => {
  const draft = sample();
  draft.setText('section', 'nb', '');
  assert.equal(draft.text('section', 'nb'), undefined);
  draft.setText('name', NEUTRAL_LOCALE, '');
  assert.equal(draft.isShared('name'), true, 'a shared line stays shared while it is being retyped');
});

test('omitting a line in a locale hides its text there only', () => {
  const draft = sample();
  draft.setOmitted('section', 'nb', true);
  assert.equal(draft.isOmitted('section', 'nb'), true);
  assert.equal(draft.displayText('section', 'nb'), '');
  assert.equal(draft.displayText('section', 'en'), 'Experience');
  draft.setOmitted('section', 'nb', false);
  assert.equal(draft.text('section', 'nb'), undefined);
});

test('toJSON sends nodes in document order and drops empty text', () => {
  const draft = sample();
  draft.setText('b2', 'en', '   ');
  const json = draft.toJSON();
  assert.deepEqual(json.nodes.map(n => n.id), ['root', 'name', 'section', 'entry', 'b1', 'b2']);
  assert.ok(!json.texts.some(t => t.nodeId === 'b2'));
  assert.deepEqual(json.nodes.find(n => n.id === 'entry').attrs, { start: '2021-03' });
  draft.setOmitted('b1', 'fr', true);
  assert.deepEqual(draft.toJSON().texts.find(t => t.nodeId === 'b1' && t.locale === 'fr'),
    { nodeId: 'b1', locale: 'fr', content: null, omitted: true });
});

test('the fingerprint detects real changes only', () => {
  const draft = sample();
  const baseline = draft.fingerprint();

  draft.setText('b1', 'en', 'Changed');
  assert.notEqual(draft.fingerprint(), baseline);
  draft.setText('b1', 'en', 'First');
  assert.equal(draft.fingerprint(), baseline, 'typing and undoing is not a change');

  draft.markReviewed('section', 'nb');
  assert.notEqual(draft.fingerprint(), baseline, 'a review mark changes the stored source hash, so it counts');
  draft.clearReviewMarks();
  assert.equal(draft.fingerprint(), baseline);
});

test('attributes are set and cleared', () => {
  const draft = sample();
  draft.setAttr('entry', 'end', '2024-01');
  assert.deepEqual(draft.node('entry').attrs, { start: '2021-03', end: '2024-01' });
  draft.setAttr('entry', 'end', '');
  assert.deepEqual(draft.node('entry').attrs, { start: '2021-03' });
});
