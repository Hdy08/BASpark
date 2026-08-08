import assert from 'node:assert/strict';
import { Buffer } from 'node:buffer';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const expectedSha256 =
  '038A80308F04517BFF69C0A8AB37776F1D0412D80E455E8508E21F51FA7B3839';
const vendorPath = new URL(
  '../../src/Web/vendor/ba-click-fx.iife.js',
  import.meta.url,
);
const adapterPath = new URL('../../src/Web/fx-adapter.js', import.meta.url);
const templatePath = new URL('../../src/Web/index.html', import.meta.url);
const legacyTemplatePath = new URL(
  '../../src/Web/index.legacy.html',
  import.meta.url,
);

function createLegacyHarness()
{
  function createContext()
  {
    return {
      arcs: [],
      beginPath()
      {
      },
      clearRect()
      {
      },
      createLinearGradient()
      {
        return {
          addColorStop()
          {
          },
        };
      },
      drawImage()
      {
      },
      fill()
      {
      },
      lineTo()
      {
      },
      moveTo()
      {
      },
      restore()
      {
      },
      rotate()
      {
      },
      save()
      {
      },
      setTransform()
      {
      },
      stroke()
      {
      },
      translate()
      {
      },
      arc(...args)
      {
        this.arcs.push(args);
      },
    };
  }

  const mainContext = createContext();
  const bufferContext = createContext();
  const mainCanvas =
  {
    height: 0,
    width: 0,
    getContext()
    {
      return mainContext;
    },
  };
  const bufferCanvas =
  {
    height: 0,
    width: 0,
    getContext()
    {
      return bufferContext;
    },
  };
  const windowMock =
  {
    addEventListener()
    {
    },
    devicePixelRatio: 1,
    innerHeight: 600,
    innerWidth: 800,
  };
  const legacyHtml = readFileSync(legacyTemplatePath, 'utf8');
  const script = legacyHtml.match(/<script>([\s\S]+)<\/script>/i)?.[1];

  assert.ok(script, 'legacy renderer must contain an inline script');

  vm.runInNewContext(
    script,
    {
      console:
      {
        error()
        {
        },
        warn()
        {
        },
      },
      document:
      {
        createElement()
        {
          return bufferCanvas;
        },
        getElementById()
        {
          return mainCanvas;
        },
      },
      performance:
      {
        now()
        {
          return 0;
        },
      },
      requestAnimationFrame()
      {
      },
      window: windowMock,
    },
    { filename: 'index.legacy.html' },
  );

  return {
    bufferContext,
    window: windowMock,
  };
}

test('vendored artifact matches the reviewed v1.2.23 build', () =>
{
  const bytes = readFileSync(vendorPath);
  const actual = createHash('sha256').update(bytes).digest('hex').toUpperCase();

  assert.equal(actual, expectedSha256);
});

test('vendored IIFE exposes every host API required by BASpark', () =>
{
  const source = readFileSync(vendorPath, 'utf8');
  const context =
  {
    // 当前 IIFE 在模块初始化时解码内嵌纹理，浏览器会原生提供 atob。
    atob(encoded)
    {
      return Buffer.from(encoded, 'base64').toString('latin1');
    },
  };

  vm.runInNewContext(source, context, { filename: 'ba-click-fx.iife.js' });

  assert.equal(typeof context.BAClickFX.BAClickFX, 'function');
  assert.equal(typeof context.BAClickFX.createConfig, 'function');
  assert.equal(context.BAClickFX.BLOOM_BACKEND_CHANGE_EVENT, 'baclickfxbackendchange');
  assert.equal(context.BAClickFX.EFFECT_BACKEND_CHANGE_EVENT, 'baclickfxeffectbackendchange');
  assert.equal(context.BAClickFX.HOST_COMPOSITING_CHANGE_EVENT, 'baclickfxhostcompositingchange');

  const lightBackgroundConfig = context.BAClickFX.createConfig(
    {
      outputCompositing: 'browser-overlay',
      overlayAlphaPolicy: 'visual-max',
      overlayColorCompensation: 'bright-core',
      overlayAlphaLimit: 0.85,
      hostCompositing: 'source-over',
      hostCompositingSurface: 'transparent-window',
    },
  );

  assert.equal(lightBackgroundConfig.outputCompositing, 'browser-overlay');
  assert.equal(lightBackgroundConfig.overlayAlphaPolicy, 'visual-max');
  assert.equal(lightBackgroundConfig.overlayColorCompensation, 'bright-core');
  assert.equal(lightBackgroundConfig.overlayAlphaLimit, 0.85);
  assert.equal(lightBackgroundConfig.hostCompositing, 'source-over');
  assert.equal(lightBackgroundConfig.hostCompositingSurface, 'transparent-window');

  const prototype = context.BAClickFX.BAClickFX.prototype;
  for (const method of [
    'pointerDown',
    'pointerMove',
    'pointerUp',
    'pointerCancel',
    'clearTrail',
    'setPaused',
    'updateConfig',
    'setThemeColor',
    'setFxParams',
    'getFxConfig',
    'destroy',
  ])
  {
    assert.equal(typeof prototype[method], 'function', `${method} must remain public`);
  }
});

test('inline resources cannot terminate their script element early', () =>
{
  const vendor = readFileSync(vendorPath, 'utf8');
  const adapter = readFileSync(adapterPath, 'utf8');

  assert.equal(/<\/script/i.test(vendor), false);
  assert.equal(/<\/script/i.test(adapter), false);
  assert.equal(vendor.includes('sourceMappingURL'), false);
});

test('renderer template contains one deterministic injection marker', () =>
{
  const template = readFileSync(templatePath, 'utf8');
  const marker = '<!-- BASPARK_RENDERER_SCRIPTS -->';

  assert.equal(template.split(marker).length - 1, 1);
});

test('legacy renderer applies independent trail and click scales', () =>
{
  const harness = createLegacyHarness();
  const { bufferContext, window } = harness;

  window.updateEffectSettings(0.5, 3, 1, 1, 1);

  const spark = window.spark;
  spark.trail = [{ x: 10, y: 10, life: 1 }];
  spark.lastPos = { x: 10.1, y: 10 };
  spark._updateTrail(0);
  assert.equal(bufferContext.arcs.at(-1)[2], 1.5);

  spark.trail = [
    { x: 10, y: 10, life: 1 },
    { x: 20, y: 10, life: 1 },
  ];
  spark.lastPos = { x: 30, y: 10 };
  spark._updateTrail(0);
  assert.ok(Math.abs(bufferContext.lineWidth - 5 / 3) < 0.000001);
  assert.equal(bufferContext.shadowBlur, 1);

  const lineWidths = [];
  spark._strokeRingSegment = (...args) =>
  {
    lineWidths.push(args[5]);
  };
  spark.waves =
  [
    {
      x: 10,
      y: 10,
      r: 0,
      life: 1,
      ring:
      {
        ang: 0,
        rs: 0,
        segs:
        [
          { off: 0, len: 1, rRoundRate: 0 },
          { off: 0, len: 1, rRoundRate: 0 },
        ],
      },
    },
  ];
  spark._updateWaves(1);

  assert.equal(lineWidths.at(0), 0.8);
});
