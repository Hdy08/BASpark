import assert from 'node:assert/strict';
import { Buffer } from 'node:buffer';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const expectedSha256 =
  '66009D7C1B662B27AE9CF283F025F9A59750B5F38E5953EEF49ECA9D2C0DA7B7';
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
  const eventListeners = new Map();
  const math = Object.assign(Object.create(Math),
    {
      random()
      {
        return 0.5;
      },
    });

  class FakeMouseEvent
  {
    constructor(type, options = {})
    {
      this.type = type;
      Object.assign(this, options);
    }
  }

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
    addEventListener(type, listener)
    {
      eventListeners.set(type, listener);
    },
    dispatchEvent(event)
    {
      eventListeners.get(event.type)?.(event);
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
      Math: math,
      MouseEvent: FakeMouseEvent,
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
    eventListeners,
    window: windowMock,
  };
}

test('vendored artifact matches BASpark\'s reviewed 1px sampling patch', () =>
{
  const bytes = readFileSync(vendorPath);
  const actual = createHash('sha256').update(bytes).digest('hex').toUpperCase();

  assert.equal(actual, expectedSha256);
});

test('vendored renderer uses a fixed 1px trail sample threshold', () =>
{
  const source = readFileSync(vendorPath, 'utf8');

  assert.equal(
    source.includes('i=this._getScale(),a=1;if(r<a)return;let o=Math.min(512,Math.floor(r/a))'),
    true,
  );
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
    structuredClone(value)
    {
      return JSON.parse(JSON.stringify(value));
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

  const glowPatch = context.BAClickFX.applyFxParamPatch(
    {
      'bloom.trailEmission': 23.968628 * 3,
      'bloom.clickEmissionScale': 3,
    },
    {
      strict: true,
    },
  );
  assert.equal(glowPatch.committed, true);
  assert.equal(glowPatch.rejected.length, 0);
  assert.equal(glowPatch.applied.length, 2);
  assert.equal(glowPatch.applied[0].path, 'bloom.trailEmission');
  assert.equal(glowPatch.applied[0].value, 23.968628 * 3);
  assert.equal(glowPatch.applied[1].path, 'bloom.clickEmissionScale');
  assert.equal(glowPatch.applied[1].value, 3);

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
  const { bufferContext, eventListeners, window } = harness;

  assert.equal(window.spark.scale, 1);
  assert.equal(window.spark.trailScale, 1);
  assert.equal(window.spark.clickScale, 1);
  assert.equal(window.spark.trailGlowIntensity, 1);
  assert.equal(window.spark.clickGlowIntensity, 1);

  const mouseMove = eventListeners.get('mousemove');
  assert.equal(typeof mouseMove, 'function');
  window.spark.isDown = true;
  window.spark.lastPos = { x: 10, y: 10 };
  mouseMove({ clientX: 11, clientY: 10 });
  assert.equal(window.spark.trail.length, 1);

  window.externalTrailStart(0.5, 0.25);
  assert.equal(window.spark.isDown, true);
  assert.equal(window.spark.lastPos.x, 400);
  assert.equal(window.spark.lastPos.y, 150);
  assert.equal(window.spark.waves.length, 0);
  assert.equal(window.spark.sparks.length, 0);

  window.updateEffectSettings('invalid', 'invalid', 1, 1, 1);
  assert.equal(window.spark.scale, 1);
  assert.equal(window.spark.trailScale, 1);
  assert.equal(window.spark.clickScale, 1);
  assert.equal(window.spark.trailGlowIntensity, 1);
  assert.equal(window.spark.clickGlowIntensity, 1);

  window.updateEffectSettings(0.5, 3, 1, 1, 1, 1, 1);

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

  window.updateEffectSettings(1, 1, 1, 1, 1, 0, 3);
  assert.equal(window.spark.trailGlowIntensity, 0);
  assert.equal(window.spark.clickGlowIntensity, 3);
});

test('legacy environment filtering releases input without clearing existing effects', () =>
{
  const { window } = createLegacyHarness();
  const spark = window.spark;

  window.externalTrailStart(0.5, 0.25);
  spark.trail = [{ x: 10, y: 10, life: 1 }];
  spark.waves = [{ x: 10, y: 10, r: 4, life: 1 }];
  spark.sparks = [{ x: 10, y: 10, life: 1 }];

  assert.equal(window.setEnvironmentInputSuppressed(true), true);
  assert.equal(spark.isDown, false);
  assert.equal(spark.trail.length, 1);
  assert.equal(spark.waves.length, 1);
  assert.equal(spark.sparks.length, 1);

  window.externalTrailStart(0.4, 0.5);
  assert.equal(spark.isDown, false);

  assert.equal(window.setEnvironmentInputSuppressed(false), true);
  window.externalTrailStart(0.4, 0.5);
  assert.equal(spark.isDown, true);
});
