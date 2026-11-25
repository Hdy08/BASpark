import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
import test from 'node:test';

const { getDamage } = createRequire(import.meta.url)('../../src/Web/layered-frame-presenter.js');

function makeMask(columns, rows, tiles)
{
  const mask = new Uint8Array(columns * rows * 4);
  for (const [column, row] of tiles) mask[(row * columns + column) * 4] = 255;
  return mask;
}

test('includes the complete extent of distant nonzero pixels and edge tiles', () =>
{
  const mask = makeMask(8, 5, [[0, 0], [7, 4]]);
  const damage = getDamage(mask, 8, 5, 123, 77, 154, 96, null);
  assert.deepEqual(damage.current, [0, 0, 154, 96]);
  assert.deepEqual(damage.dirty, damage.current);
});

test('includes the interpolation margin and clears the previous frame', () =>
{
  const previous = [0, 0, 24, 28];
  const mask = makeMask(8, 5, [[3, 2]]);
  const damage = getDamage(mask, 8, 5, 128, 80, 160, 100, previous);
  assert.deepEqual(damage.current, [58, 38, 82, 62]);
  assert.deepEqual(damage.dirty, [0, 0, 82, 62]);

  mask.fill(0);
  assert.deepEqual(getDamage(mask, 8, 5, 128, 80, 160, 100, damage.current), {
    current: null, dirty: damage.current,
  });
  assert.deepEqual(getDamage(mask, 8, 5, 128, 80, 160, 100, null), { current: null, dirty: null });
});
