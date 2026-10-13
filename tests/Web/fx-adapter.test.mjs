import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';
import { Buffer } from 'node:buffer';

const adapterSource = readFileSync(
  new URL('../../src/Web/fx-adapter.js', import.meta.url),
  'utf8',
);

function createHarness(options = {})
{
  const calls =
  {
    addEventListener: [],
    clearTrail: 0,
    destroy: 0,
    messages: [],
    pointerCancel: [],
    pointerDown: [],
    pointerMove: [],
    pointerUp: [],
    pointerTimes: [],
    booms: [],
    setFxParams: [],
    setCompositingReference: [],
    setPaused: [],
    setThemeColor: [],
    updateConfig: [],
  };
  const windowListeners = new Map();
  const hostListeners = new Map();
  const canvasListeners = new Map();

  class FakeFx
  {
    constructor(config)
    {
      if (options.constructorError)
      {
        throw options.constructorError;
      }

      this.width = 800;
      this.height = 600;
      this.config = { clickEnabled: true, trailEnabled: true, trailAlways: false, inputSamplingRate: 0, ...config };
      this.shards = [];
      this.trailStrokes = [];
      this.currentTrailStroke = null;
      this.activePointerId = null;
      this.activePointerSource = null;
      this.lastPointerPosition = null;
      this.lastPointerTime = 0;
      this.trailDistanceSinceShard = 0;
      this.activeTrailOwnerId = null;
      this.nextTrailOwnerId = 1;
      this.fxConfig = this.getFxConfig();
      this.nextOwnerId = 1;
      this.trailShardCounts = new Map();
      this.resolvedEffectBackend = 'pending';
      this.resolvedBloomBackend = 'pending';
      this.resolvedHostCompositing = 'pending';
      this.compositingWarning = null;
      this.canvas =
      {
        addEventListener(type, listener)
        {
          calls.addEventListener.push(type);
          canvasListeners.set(type, listener);
        },
      };
      FakeFx.instance = this;
    }

    destroy()
    {
      calls.destroy++;
    }

    clearTrail()
    {
      calls.clearTrail++;
    }

    getConfig()
    {
      return {
        effectBackend: this.config.effectBackend,
        bloomBackend: this.config.bloomBackend,
        resolvedEffectBackend: this.resolvedEffectBackend,
        resolvedBloomBackend: this.resolvedBloomBackend,
        hostCompositing: this.config.hostCompositing,
        resolvedHostCompositing: this.resolvedHostCompositing,
        hostCompositingSurface: this.config.hostCompositingSurface,
        compositingWarning: this.compositingWarning,
        trailTimeScale: this.config.trailTimeScale,
        clickEnabled: this.config.clickEnabled,
      };
    }

    getFxConfig()
    {
      if (this.fxConfig) return structuredClone(this.fxConfig);
      return {
        rings: { count: 2, hdrIntensity: 5.992157, radiusMin: 68.92571232 },
        disk: { radius: 64.8, lifetimeMs: 200 },
        trail: {
          lifetimeMs: 300,
          geometryWidth: 4,
          width: 3,
          minVertexDistance: 5,
          outerGlowWidth: 7,
        },
        shards: {
          hdrIntensity: 5.992157,
          trailRadius: 11,
          trailSpeedMin: 13,
          trailSpeedMax: 17,
          trailSpacing: 19,
        },
        bloom: {
          intensity: 1.7,
          threshold: 1, softKnee: 0, ringBlur: 80, resolutionScale: 0.5,
        },
      };
    }

    pointerCancel(pointerId)
    {
      calls.pointerCancel.push(pointerId);
      return true;
    }

    pointerDown(input)
    {
      const normalized = this._normalizePointerInput(input);
      calls.pointerDown.push(normalized);
      calls.pointerTimes.push({ type: 'down', time: this._getTrailInputTime() });
      this.currentTrailStroke = { ownerId: this.nextOwnerId++, active: true, points: [normalized] };
      this.trailStrokes.push(this.currentTrailStroke);
      return true;
    }

    pointerMove(input)
    {
      const normalized = this._normalizePointerInput(input);
      calls.pointerMove.push(normalized);
      calls.pointerTimes.push({ type: 'move', time: this._getTrailInputTime() });
      if (!this.currentTrailStroke && this.config.trailAlways)
      {
        this.currentTrailStroke = { ownerId: this.nextOwnerId++, active: true, points: [] };
        this.trailStrokes.push(this.currentTrailStroke);
      }
      this.currentTrailStroke?.points.push(normalized);
      return true;
    }

    pointerUp(pointerId)
    {
      calls.pointerUp.push(pointerId);
      if (this.currentTrailStroke) this.currentTrailStroke.active = false;
      this.currentTrailStroke = null;
      return true;
    }

    _normalizePointerInput(input)
    {
      return { ...input, x: Math.max(0, Math.min(this.width, input.x)), y: Math.max(0, Math.min(this.height, input.y)) };
    }

    _getTrailInputTime()
    {
      return 1000;
    }

    setPaused(paused, pauseOptions)
    {
      calls.setPaused.push({ paused, pauseOptions });
    }

    setCompositingReference(source, referenceOptions)
    {
      calls.setCompositingReference.push({ source, referenceOptions });
      return true;
    }

    setFxParams(patch, options)
    {
      calls.setFxParams.push({ patch, options });
      for (const [path, value] of Object.entries(patch))
      {
        const segments = path.split('.');
        this.fxConfig[segments[0]][segments[1]] = value;
      }
      return { committed: true };
    }

    setThemeColor(color)
    {
      calls.setThemeColor.push(color);
    }

    updateConfig(config)
    {
      calls.updateConfig.push(config);
      Object.assign(this.config, config);
    }
  }

  if (options.vendorPointerInput)
  {
    const vendorContext = { atob: (encoded) => Buffer.from(encoded, 'base64').toString('latin1'), structuredClone,
      performance: { now: () => 1000 } };
    vm.runInNewContext(readFileSync(new URL('../../src/Web/vendor/ba-click-fx.iife.js', import.meta.url), 'utf8'), vendorContext);
    const prototype = vendorContext.BAClickFX.BAClickFX.prototype;
    for (const method of ['pointerDown', 'pointerMove', 'pointerUp', 'pointerCancel', '_normalizePointerInput',
      '_pointerMoveAtTime', '_beginTrailOwner', '_startTrailStroke', '_ensureCurrentTrailStroke',
      '_appendPointerSample', '_releaseActivePointer', '_acceptInputSample', '_isInputSampleDue'])
      FakeFx.prototype[method] = prototype[method];
    FakeFx.prototype._getScale = function () { return this.config.scale; };
    FakeFx.prototype._requestRender = function () {};
    FakeFx.prototype._invalidateCanvasBoundsScope = function () {};
    FakeFx.prototype._spawnTrailShards = function () {};
    FakeFx.prototype._spawnClick = function (horizontal, vertical) { calls.booms.push([horizontal, vertical]); };
  }

  const windowMock =
  {
    __basparkRendererGeneration: 'test-generation',
    BAClickFX:
    {
      BAClickFX: FakeFx,
      BLOOM_BACKEND_CHANGE_EVENT: 'baclickfxbackendchange',
      EFFECT_BACKEND_CHANGE_EVENT: 'baclickfxeffectbackendchange',
      HOST_COMPOSITING_CHANGE_EVENT: 'baclickfxhostcompositingchange',
    },
    chrome:
    {
      webview:
      {
        addEventListener(type, listener)
        {
          hostListeners.set(type, listener);
        },
        postMessage(message)
        {
          calls.messages.push(JSON.parse(message));
        },
      },
    },
    addEventListener(type, listener)
    {
      windowListeners.set(type, listener);
    },
  };
  const context =
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
      readyState: 'complete',
      addEventListener()
      {
        throw new Error('DOMContentLoaded listener is not expected for a complete document.');
      },
    },
    window: windowMock,
  };

  vm.runInNewContext(adapterSource, context, { filename: 'fx-adapter.js' });

  return {
    calls,
    canvasListeners,
    hostListeners,
    fx: FakeFx.instance,
    window: windowMock,
    windowListeners,
  };
}

test('initializes the vendored renderer in manual WebView2 mode', () =>
{
  const harness = createHarness();

  assert.equal(harness.fx.config.inputSource, 'manual');
  assert.equal(harness.fx.config.effectBackend, 'webgl2');
  assert.equal(harness.fx.config.bloomBackend, 'webgl2');
  assert.equal(harness.fx.config.outputCompositing, 'browser-overlay');
  assert.equal(harness.fx.config.overlayAlphaPolicy, 'visual-max');
  assert.equal(harness.fx.config.overlayColorCompensation, 'none');
  assert.equal(harness.fx.config.overlayAlphaLimit, 0.85);
  assert.equal(harness.fx.config.hostCompositing, 'source-over');
  assert.equal(harness.fx.config.hostCompositingSurface, 'transparent-window');
  assert.equal(harness.fx.config.isolatedCompositing, false);
  assert.equal(harness.fx.config.lightBackgroundContrastAlpha, 0);
  assert.equal(harness.fx.config.maxDpr, 2);
  assert.equal(harness.calls.updateConfig.at(-1).scale, 2 / 3);
  assert.equal(harness.calls.setCompositingReference.length, 0);
  assert.equal(harness.calls.messages.at(-1).type, 'ready');
  assert.equal(harness.calls.messages.at(-1).generation, 'test-generation');
  assert.equal(harness.calls.messages.at(-1).requestedEffectBackend, 'webgl2');
  assert.equal(harness.calls.messages.at(-1).resolvedEffectBackend, 'pending');
  assert.equal(harness.calls.messages.at(-1).requestedBloomBackend, 'webgl2');
  assert.equal(harness.calls.messages.at(-1).resolvedBloomBackend, 'pending');
  assert.deepEqual(
    harness.calls.addEventListener,
    [
      'baclickfxbackendchange',
      'baclickfxeffectbackendchange',
      'baclickfxhostcompositingchange',
    ],
  );
});

test('advanced settings at their defaults leave all existing effects unchanged', () =>
{
  const { window, fx, calls } = createHarness();
  window.updateEffectSettings(1.4, 2.2, 0.6, 1.5, 0.7, 1.8);
  const before = JSON.stringify({ config: fx.config, parameters: fx.fxConfig });
  const configCalls = calls.updateConfig.length;
  const parameterCalls = calls.setFxParams.length;
  assert.equal(window.updateBaClickAdvancedSettings(true, { config: {}, parameters: {} }), true);
  assert.equal(window.updateBaClickAdvancedSettings(false, { config: { renderMode: 'native-bloom' }, parameters: { 'trail.width': 7 } }), true);
  assert.equal(JSON.stringify({ config: fx.config, parameters: fx.fxConfig }), before);
  assert.equal(calls.updateConfig.length, configCalls);
  assert.equal(calls.setFxParams.length, parameterCalls);
  assert.equal(fx.config.themeColorMode, 'hue-only');
});

test('advanced shapes compose with existing scales and restore cleanly when disabled', () =>
{
  const { window, fx, calls } = createHarness();
  window.updateEffectSettings(2, 1, 0.6, 1.2, 0.8, 1.8);
  const advanced = { config: { renderMode: 'native-bloom', maxDpr: 1.5, themeColorMode: 'relative-oklch' },
    parameters: { 'trail.width': 5.5, 'trail.minVertexDistance': 7, 'rings.count': 4, 'bloom.threshold': 2 } };
  assert.equal(window.updateBaClickAdvancedSettings(true, advanced), true);
  assert.equal(fx.fxConfig.trail.width, 11);
  assert.equal(fx.fxConfig.trail.minVertexDistance, 14);
  assert.equal(fx.fxConfig.rings.count, 4);
  assert.equal(fx.fxConfig.bloom.threshold, 2);
  assert.equal(fx.fxConfig.bloom.intensity, 1.7 * 1.8);
  assert.equal(fx.config.effectBackend, 'canvas2d');
  assert.equal(fx.config.bloomBackend, 'native');
  assert.equal(fx.config.opacity, 0.6);
  assert.equal(fx.config.clickTimeScale, 0.8);
  assert.equal(fx.config.hostCompositingSurface, 'transparent-window');
  const parameterCalls = calls.setFxParams.length;
  assert.equal(window.updateBaClickAdvancedSettings(true, advanced), true);
  assert.equal(calls.setFxParams.length, parameterCalls);
  window.updateEffectSettings(3, 1, 0.6, 1.2, 0.8, 1.8);
  assert.equal(fx.fxConfig.trail.width, 16.5);
  assert.equal(window.updateBaClickAdvancedSettings(false, advanced), true);
  assert.equal(fx.fxConfig.trail.width, 9);
  assert.equal(fx.fxConfig.trail.minVertexDistance, 15);
  assert.equal(fx.fxConfig.rings.count, 2);
  assert.equal(fx.fxConfig.bloom.threshold, 1);
  assert.equal(fx.fxConfig.bloom.intensity, 1.7 * 1.8);
  assert.equal(fx.config.effectBackend, 'webgl2');
  assert.equal(fx.config.bloomBackend, 'webgl2');
  assert.equal(fx.config.maxDpr, 2);
  assert.equal(fx.config.themeColorMode, 'hue-only');
});

test('advanced render modes retain transparent-window capabilities and explicit software Bloom', () =>
{
  const { window, fx, canvasListeners } = createHarness();
  for (const [mode, backend, bloom, hdr] of [
    ['full-webgpu-sdr', 'webgpu', 'webgl2', false], ['full-webgpu', 'webgpu', 'webgl2', true],
    ['webgl2-bloom', 'canvas2d', 'webgl2', true], ['software-bloom', 'canvas2d', 'software', true],
    ['native-bloom', 'canvas2d', 'native', true], ['full-webgl2', 'webgl2', 'webgl2', true],
  ])
  {
    assert.equal(window.updateBaClickAdvancedSettings(true, { config: { renderMode: mode }, parameters: {} }), true);
    assert.equal(fx.config.effectBackend, backend);
    assert.equal(fx.config.bloomBackend, bloom);
    assert.equal(fx.config.webgpuPreferHdr, hdr);
    assert.equal(fx.config.outputCompositing, 'browser-overlay');
    assert.equal(fx.config.hostCompositing, 'source-over');
    assert.equal(fx.config.hostCompositingSurface, 'transparent-window');
  }
  window.updateBaClickAdvancedSettings(true, { config: { renderMode: 'software-bloom' }, parameters: {} });
  fx.resolvedEffectBackend = 'canvas2d';
  fx.resolvedBloomBackend = 'software';
  canvasListeners.get('baclickfxbackendchange')({ detail: {} });
  assert.equal(fx.config.bloomBackend, 'software');
});

test('advanced settings reject host compositing changes and duplicate global glow controls', () =>
{
  const { window, fx, calls } = createHarness();
  const original = JSON.stringify(fx.config);
  assert.equal(window.updateBaClickAdvancedSettings(true, { config: { hostCompositingSurface: 'dom-backdrop' } }), false);
  assert.equal(window.updateBaClickAdvancedSettings(true, { parameters: { 'bloom.intensity': 4 } }), false);
  assert.equal(JSON.stringify(fx.config), original);
  assert.equal(calls.messages.filter(message => message.type === 'error').length, 2);
});

test('multi-touch keeps simultaneous clicks and independent trails in one vendored renderer', () =>
{
  const harness = createHarness({ vendorPointerInput: true });
  const send = (pointerId, type, horizontal, vertical) => harness.window.externalPointerInput({
    pointerId, type, x: horizontal, y: vertical, strokeId: pointerId + 40, mode: 'touch', alwaysTrail: false,
  });
  assert.equal(send(2, 'down', 0.2, 0.2), true);
  assert.equal(send(3, 'down', 0.8, 0.8), true);
  assert.deepEqual(harness.calls.booms, [[160, 120], [640, 480]]);
  assert.equal(harness.fx.trailStrokes.length, 2);
  send(2, 'move', 0.35, 0.2);
  send(3, 'move', 0.65, 0.8);
  const first = harness.fx.trailStrokes.find((stroke) => stroke.basparkPointerId === 2);
  const second = harness.fx.trailStrokes.find((stroke) => stroke.basparkPointerId === 3);
  assert.notEqual(first.ownerId, second.ownerId);
  assert.ok(first.points.length > 1);
  assert.ok(second.points.length > 1);
  assert.ok(first.points.every((point) => point.y === 120));
  assert.ok(second.points.every((point) => point.y === 480));
  send(2, 'up');
  assert.equal(first.active, false);
  assert.equal(second.active, true);
  const previousCount = second.points.length;
  assert.equal(send(3, 'move', 0.55, 0.8), true);
  assert.ok(second.points.length > previousCount);
  send(3, 'cancel');
  assert.ok(harness.fx.trailStrokes.includes(first));
  assert.ok(!harness.fx.trailStrokes.includes(second));
  assert.equal(send(3, 'up'), false);
  assert.equal(harness.calls.clearTrail, 0);
});

test('multi-touch cancellation and cross-screen replay preserve other pointers and aged history', () =>
{
  const harness = createHarness({ vendorPointerInput: true });
  const send = (pointerId, type, detail = {}) => harness.window.externalPointerInput({
    pointerId, type, x: 0.5, y: 0.5, strokeId: pointerId + 40, mode: 'touch', alwaysTrail: false, ...detail,
  });
  send(3, 'down', { y: 0.8 });
  send(3, 'move', { x: 0.7, y: 0.8 });
  const other = harness.fx.trailStrokes[0];
  send(2, 'trailContinue', { history: [[-0.2, 0.2, 120], [-0.05, 0.2, 40]],
    x: 0.1, y: 0.2, pressed: true, beginStroke: true, finishStroke: false });
  const first = harness.fx.trailStrokes.find((stroke) => stroke.basparkPointerId === 2);
  assert.ok(first.points.some((point) => point.x < 0));
  assert.ok(first.points[0].bornAt < first.points.at(-1).bornAt);
  send(2, 'cancel');
  assert.ok(harness.fx.trailStrokes.includes(other));
  assert.equal(other.active, true);
  assert.equal(send(3, 'move', { x: 0.8, y: 0.8 }), true);
  harness.window.setRenderingPaused(true);
  assert.equal(other.active, false);
  assert.equal(send(3, 'up'), false);
  harness.window.setRenderingPaused(false);
  assert.equal(send(4, 'down'), true);
});

test('multi-touch input restores a simultaneous mouse context and rejects stale generations', () =>
{
  const harness = createHarness({ vendorPointerInput: true });
  harness.window.setInputContext('mouse', true);
  harness.window.externalMove(0.1, 0.1);
  const mouse = harness.fx.currentTrailStroke;
  const dispatch = harness.hostListeners.get('message');
  const input = { source: 'baspark-input', generation: 'test-generation', pointerId: 2,
    strokeId: 42, type: 'down', x: 0.8, y: 0.8, mode: 'touch', alwaysTrail: true };
  dispatch({ data: { ...input, generation: 'old-generation' } });
  assert.equal(harness.calls.booms.length, 0);
  dispatch({ data: input });
  assert.equal(harness.calls.booms.length, 1);
  assert.equal(harness.fx.currentTrailStroke, mouse);
  assert.equal(harness.fx.activePointerId, 1);
  assert.equal(harness.fx.config.trailAlways, true);
  assert.equal(harness.window.externalMove(0.2, 0.1), true);
  assert.ok(mouse.points.length > 1);
  harness.window.externalCancel();
  assert.equal(harness.window.externalPointerInput({ ...input, type: 'up' }), false);
});

test('maps normalized host input and BASpark settings to BAClickFX', () =>
{
  const harness = createHarness();

  harness.window.updateEffectSettings(1, 1, 0.75, 1.2, 0.8);
  const settings = harness.calls.updateConfig.at(-1);
  assert.equal(settings.scale, 2 / 3);
  assert.equal(settings.opacity, 0.75);
  assert.equal(settings.trailTimeScale, 1.2);
  assert.equal(settings.clickTimeScale, 0.8);
  assert.equal(harness.calls.setFxParams.length, 0);

  harness.window.updateColor('76,167,255');
  assert.equal(harness.calls.setThemeColor.at(-1), '#4ca7ff');

  harness.window.setInputContext('mouse', true);
  assert.equal(harness.calls.updateConfig.at(-1).trailAlways, true);

  harness.window.externalMove(0.25, 0.5);
  assert.equal(harness.calls.pointerMove.at(-1).x, 200);
  assert.equal(harness.calls.pointerMove.at(-1).y, 300);
  assert.equal(harness.calls.pointerMove.at(-1).pointerType, 'mouse');

  harness.window.externalBoom(0.5, 0.25);
  assert.equal(harness.calls.pointerDown.at(-1).x, 400);
  assert.equal(harness.calls.pointerDown.at(-1).y, 150);
  assert.equal(harness.calls.pointerDown.at(-1).pointerId, 1);
  assert.equal(harness.calls.pointerCancel.length, 1);
  assert.equal(harness.calls.clearTrail, 1);

  harness.window.externalUp();
  assert.deepEqual(harness.calls.pointerUp, [1]);

  harness.window.externalCancel();
  assert.equal(harness.calls.pointerCancel.at(-1), 1);
  assert.equal(harness.calls.clearTrail, 2);
});

test('maps independent trail and click scales to the renderer', () =>
{
  const harness = createHarness();

  harness.window.updateEffectSettings(0.75, 3, 0.75, 1.2, 0.8);
  const settings = harness.calls.updateConfig.at(-1);
  const patch = harness.calls.setFxParams.at(-1).patch;

  assert.equal(settings.scale, 2);
  assert.equal(patch['trail.geometryWidth'], 1);
  assert.equal(patch['trail.width'], 0.75);
  assert.equal(patch['trail.minVertexDistance'], 1.25);
  assert.equal(patch['trail.outerGlowWidth'], 1.75);
  assert.equal(patch['shards.trailRadius'], 2.75);
  assert.equal(patch['shards.trailSpeedMin'], 3.25);
  assert.equal(patch['shards.trailSpeedMax'], 4.25);
  assert.equal(patch['shards.trailSpacing'], 4.75);
});

test('maps unified glow brightness to Bloom intensity', () =>
{
  const harness = createHarness();

  harness.window.updateEffectSettings(1, 1, 1, 1, 1, 1);
  assert.equal(harness.calls.setFxParams.length, 0);

  harness.window.updateEffectSettings(1, 1, 1, 1, 1, 0);
  let patch = harness.calls.setFxParams.at(-1).patch;
  assert.deepEqual(Object.keys(patch), ['bloom.intensity']);
  assert.equal(patch['bloom.intensity'], 0);

  harness.window.updateEffectSettings(1, 1, 1, 1, 1, 3);
  patch = harness.calls.setFxParams.at(-1).patch;
  assert.deepEqual(Object.keys(patch), ['bloom.intensity']);
  assert.equal(patch['bloom.intensity'], 5.1);
});

test('starts a pressed trail without creating a click boom', () =>
{
  const harness = createHarness();
  const updateCount = harness.calls.updateConfig.length;

  harness.window.externalTrailStart(0.5, 0.25);

  assert.equal(harness.calls.pointerDown.length, 1);
  assert.equal(harness.calls.pointerDown.at(-1).x, 400);
  assert.equal(harness.calls.pointerDown.at(-1).y, 150);
  const clickConfigUpdates = harness.calls.updateConfig.slice(updateCount);
  assert.equal(clickConfigUpdates.length, 2);
  assert.equal(clickConfigUpdates[0].clickEnabled, false);
  assert.equal(clickConfigUpdates[1].clickEnabled, true);
});

test('cross-screen departure preserves the old stroke and extends past the canvas edge', () =>
{
  const harness = createHarness();
  harness.window.externalBoom(0.5, 0.5);
  harness.window.externalMove(0.9, 0.5);
  const originalStroke = harness.fx.currentTrailStroke;
  const normalize = harness.fx._normalizePointerInput;
  const clock = harness.fx._getTrailInputTime;
  const cancels = harness.calls.pointerCancel.length;
  const clears = harness.calls.clearTrail;

  harness.window.externalTrailContinue([], 1.2, 0.5, true, false, true);

  assert.equal(harness.calls.pointerMove.at(-1).x, 960);
  assert.equal(harness.calls.pointerUp.at(-1), 1);
  assert.equal(harness.calls.pointerCancel.length, cancels);
  assert.equal(harness.calls.clearTrail, clears);
  assert.equal(harness.fx.trailStrokes[0], originalStroke);
  assert.equal(originalStroke.active, false);
  assert.equal(harness.fx._normalizePointerInput, normalize);
  assert.equal(harness.fx._getTrailInputTime, clock);
  harness.window.externalMove(1.3, 0.5);
  assert.equal(harness.calls.pointerMove.at(-1).x, 800);
});

test('cross-screen entry inherits unclipped geometry and sample ages without adding a click', () =>
{
  const harness = createHarness();
  const dispatch = harness.hostListeners.get('message');
  dispatch({ data: { source: 'baspark-input', generation: 'test-generation', type: 'trailContinue',
    mode: 'mouse', alwaysTrail: false, strokeId: 42, pressed: true, beginStroke: true, finishStroke: false,
    history: [[-0.75, 0.3, 180], [-0.25, 0.4, 90], [-0.05, 0.5, 25]], x: 0.125, y: 0.6 } });

  assert.equal(harness.calls.pointerDown.at(-1).x, -600);
  assert.equal(harness.calls.pointerDown.at(-1).y, 180);
  assert.deepEqual(harness.calls.pointerMove.map((point) => point.x), [-200, -40, 100]);
  assert.deepEqual(harness.calls.pointerTimes.map((sample) => sample.time), [820, 910, 975, 1000]);
  assert.equal(harness.fx.currentTrailStroke.basparkStrokeId, 42);
  assert.equal(harness.calls.pointerCancel.length, 0);
  assert.equal(harness.calls.clearTrail, 0);
  assert.deepEqual(harness.calls.updateConfig.filter((patch) => 'clickEnabled' in patch).map((patch) => patch.clickEnabled), [false, true]);
});

test('cross-screen followers keep one moving head until the physical pointer releases', () =>
{
  for (const pressed of [false, true])
  {
    const departure = createHarness();
    const arrival = createHarness();
    departure.window.setInputContext('mouse', !pressed);
    arrival.window.setInputContext('mouse', !pressed);
    if (pressed) departure.window.externalBoom(0.5, 0.5);
    else departure.window.externalMove(0.5, 0.5);
    departure.window.externalMove(0.9, 0.5);
    const stroke = departure.fx.currentTrailStroke;
    arrival.window.externalTrailContinue([[-0.5, 0.5, 30], [-0.1, 0.5, 10]], 0.1, 0.5, pressed, true, false);
    for (const horizontal of [0.1, 0.3, 0.6])
    {
      departure.window.externalTrailContinue([], 1 + horizontal, 0.5, pressed, false, false);
      arrival.window.externalMove(horizontal, 0.5);
      assert.equal(departure.fx.currentTrailStroke, stroke);
      assert.equal(stroke.active, true);
      assert.equal(departure.fx.trailStrokes.length, 1);
      assert.equal(arrival.fx.trailStrokes.length, 1);
      assert.ok(Math.abs(stroke.points.at(-1).x - 800 - arrival.fx.currentTrailStroke.points.at(-1).x) < 1e-8);
    }
    departure.window.externalUp();
    arrival.window.externalUp();
    assert.equal(stroke.active, false);
    assert.equal(departure.fx.currentTrailStroke, null);
    assert.equal(arrival.fx.currentTrailStroke, null);
    assert.equal(departure.calls.clearTrail, 0);
    assert.equal(arrival.calls.clearTrail, 0);
  }
});

test('cross-screen hover inherits slow trails and releases an intermediate screen naturally', () =>
{
  const harness = createHarness();
  harness.window.setInputContext('mouse', true);
  harness.window.updateEffectSettings(1, 1, 1, 0.2, 1);
  harness.window.externalTrailContinue([[-1, 0.5, 1000], [-0.1, 0.5, 100]], 1.2, 0.5, false, true, true);

  assert.equal(harness.calls.pointerDown.length, 0);
  assert.deepEqual(harness.calls.pointerMove.map((point) => point.x), [-800, -80, 960]);
  assert.deepEqual(harness.calls.pointerTimes.map((sample) => sample.time), [800, 980, 1000]);
  assert.equal(harness.fx.currentTrailStroke, null);
  assert.equal(harness.fx.trailStrokes.length, 1);
  assert.equal(harness.calls.clearTrail, 0);
});

test('cross-screen return replaces only the same trajectory without clearing other effects', () =>
{
  const harness = createHarness();
  const dispatch = harness.hostListeners.get('message');
  harness.fx.trailStrokes = [{ ownerId: 7, basparkStrokeId: 42 }, { ownerId: 8, basparkStrokeId: 43 }];
  harness.fx.shards = [{ kind: 'trail', ownerId: 7 }, { kind: 'trail', ownerId: 8 }, { kind: 'click' }];
  harness.fx.trailShardCounts.set(7, 1);
  dispatch({ data: { source: 'baspark-input', generation: 'test-generation', type: 'trailContinue',
    mode: 'mouse', alwaysTrail: false, strokeId: 42, pressed: true, beginStroke: true, finishStroke: false,
    history: [[1.2, 0.5, 40]], x: 0.8, y: 0.5 } });

  assert.equal(harness.fx.trailStrokes.filter((stroke) => stroke.basparkStrokeId === 42).length, 1);
  assert.equal(harness.fx.trailStrokes.some((stroke) => stroke.basparkStrokeId === 43), true);
  assert.equal(harness.fx.shards.some((shard) => shard.ownerId === 7), false);
  assert.equal(harness.fx.shards.some((shard) => shard.ownerId === 8), true);
  assert.equal(harness.fx.shards.some((shard) => shard.kind === 'click'), true);
  assert.equal(harness.fx.trailShardCounts.has(7), false);
  assert.equal(harness.calls.clearTrail, 0);
});

test('cross-screen replay rejects stale messages and invalid coordinates', () =>
{
  const harness = createHarness();
  const dispatch = harness.hostListeners.get('message');
  dispatch({ data: { source: 'baspark-input', generation: 'old-generation', type: 'trailContinue',
    history: [[-1, 0.5, 0]], x: 0.2, y: 0.5, beginStroke: true } });
  assert.equal(harness.calls.pointerMove.length, 0);
  assert.equal(harness.window.externalTrailContinue([], NaN, 0.5, true, true, false), false);
  harness.window.externalTrailContinue([[NaN, 0, 10], [-1, 0.5, -1], [-0.5, 0.5, 25]], 0.2, 0.5, true, true, false);
  assert.equal(harness.calls.pointerDown.length, 1);
  assert.equal(harness.calls.pointerDown.at(-1).x, -400);
});

test('uses 1.00 scale when host scale input is invalid', () =>
{
  const harness = createHarness();

  harness.window.updateEffectSettings('invalid', 'invalid', 1, 1, 1);
  assert.equal(harness.calls.updateConfig.at(-1).scale, 2 / 3);
});

test('keeps the current color when host configuration is invalid', () =>
{
  const harness = createHarness();
  const colorCallCount = harness.calls.setThemeColor.length;
  const errorMessageCount = harness.calls.messages.filter(
    (message) => message.type === 'error',
  ).length;

  assert.equal(harness.window.updateColor('45,not-a-number,255'), false);
  assert.equal(harness.calls.setThemeColor.length, colorCallCount);
  assert.equal(
    harness.calls.messages.filter((message) => message.type === 'error').length,
    errorMessageCount,
  );
});

test('disables always-trail for touch and blocks input while paused', () =>
{
  const harness = createHarness();

  harness.window.setInputContext('touch', true);
  assert.equal(harness.calls.updateConfig.at(-1).trailAlways, false);

  harness.window.externalBoom(0.1, 0.2);
  assert.equal(harness.calls.pointerDown.at(-1).pointerType, 'touch');

  harness.window.setRenderingPaused(true);
  assert.equal(harness.calls.setPaused.at(-1).paused, true);
  assert.equal(harness.calls.setPaused.at(-1).pauseOptions.clear, true);

  const downCount = harness.calls.pointerDown.length;
  assert.equal(harness.window.externalBoom(0.3, 0.4), false);
  assert.equal(harness.calls.pointerDown.length, downCount);

  harness.window.setRenderingPaused(false);
  assert.equal(harness.calls.setPaused.at(-1).paused, false);
  assert.equal(harness.calls.setPaused.at(-1).pauseOptions, undefined);
});

test('environment filtering releases input without clearing in-flight effects', () =>
{
  const harness = createHarness();

  harness.window.externalTrailStart(0.2, 0.3);
  assert.equal(harness.calls.pointerDown.length, 1);

  const cancelCount = harness.calls.pointerCancel.length;
  const clearTrailCount = harness.calls.clearTrail;
  const moveCount = harness.calls.pointerMove.length;

  assert.equal(harness.window.setEnvironmentInputSuppressed(true), true);
  assert.deepEqual(harness.calls.pointerUp, [1]);
  assert.equal(harness.calls.pointerCancel.length, cancelCount);
  assert.equal(harness.calls.clearTrail, clearTrailCount);

  assert.equal(harness.window.externalBoom(0.4, 0.5), false);
  assert.equal(harness.window.externalMove(0.4, 0.5), false);
  assert.equal(harness.window.externalTrailStart(0.4, 0.5), false);
  assert.equal(harness.calls.pointerMove.length, moveCount);

  assert.equal(harness.window.setEnvironmentInputSuppressed(false), true);
  assert.equal(harness.window.externalMove(0.4, 0.5), true);
  assert.equal(harness.calls.pointerMove.length, moveCount + 1);
});

test('changes a software Bloom fallback to the bounded native backend', () =>
{
  const harness = createHarness();
  const listener = harness.canvasListeners.get('baclickfxbackendchange');

  harness.fx.resolvedEffectBackend = 'canvas2d';
  harness.fx.resolvedBloomBackend = 'software';

  listener(
    {
      detail:
      {
        requestedBloomBackend: 'webgl2',
        resolvedBloomBackend: 'software',
      },
    },
  );

  assert.equal(harness.calls.messages.at(-1).type, 'backend');
  assert.equal(harness.calls.messages.at(-1).resolvedEffectBackend, 'canvas2d');
  assert.equal(harness.calls.messages.at(-1).resolvedBloomBackend, 'software');
  assert.equal(harness.calls.updateConfig.at(-1).bloomBackend, 'native');
});

test('reports Full WebGL backend resolution to the host', () =>
{
  const harness = createHarness();
  const listener = harness.canvasListeners.get('baclickfxeffectbackendchange');

  harness.fx.resolvedEffectBackend = 'webgl2';
  harness.fx.resolvedBloomBackend = 'webgl2';
  listener(
    {
      detail:
      {
        requestedEffectBackend: 'webgl2',
        resolvedEffectBackend: 'webgl2',
      },
    },
  );

  const message = harness.calls.messages.at(-1);
  assert.equal(message.type, 'backend');
  assert.equal(message.backend, 'webgl2');
  assert.equal(message.requestedEffectBackend, 'webgl2');
  assert.equal(message.resolvedEffectBackend, 'webgl2');
  assert.equal(message.requestedBloomBackend, 'webgl2');
  assert.equal(message.resolvedBloomBackend, 'webgl2');
});

test('reports resolved source-over transparent-window compositing to the host', () =>
{
  const harness = createHarness();
  const listener = harness.canvasListeners.get(
    'baclickfxhostcompositingchange',
  );

  harness.fx.resolvedHostCompositing = 'source-over';
  harness.fx.compositingWarning = null;
  listener(
    {
      detail:
      {
        requestedHostCompositing: 'source-over',
        resolvedHostCompositing: 'source-over',
        hostCompositingSurface: 'transparent-window',
        compositingWarning: null,
      },
    },
  );

  const message = harness.calls.messages.at(-1);
  assert.equal(message.type, 'backend');
  assert.equal(message.requestedHostCompositing, 'source-over');
  assert.equal(message.resolvedHostCompositing, 'source-over');
  assert.equal(message.hostCompositingSurface, 'transparent-window');
  assert.equal(message.compositingWarning, null);
});

test('reports initialization failure without announcing readiness', () =>
{
  const harness = createHarness(
    {
      constructorError: new Error('unsupported runtime'),
    },
  );

  assert.equal(harness.calls.messages.some((message) => message.type === 'ready'), false);
  assert.equal(harness.calls.messages.at(-1).type, 'error');
  assert.equal(harness.calls.messages.at(-1).phase, 'initialize');
});

test('native host messages preserve pointer ordering without evaluating per-move scripts', () =>
{
  const harness = createHarness();
  const dispatch = harness.hostListeners.get('message');
  const send = (type, extra = {}) => dispatch(
    {
      data:
      {
        source: 'baspark-input',
        generation: 'test-generation',
        mode: 'mouse',
        alwaysTrail: false,
        type,
        x: 0.25,
        y: 0.5,
        ...extra,
      },
    });

  send('down');
  for (let index = 0; index < 2000; index++)
    send('move', { x: index / 2000 });
  send('up');
  assert.equal(harness.calls.pointerDown.length, 1);
  assert.equal(harness.calls.pointerMove.length, 2000);
  assert.equal(harness.calls.pointerUp.length, 1);
  assert.equal(harness.calls.updateConfig.length, 2);
  assert.equal(harness.calls.pointerMove.at(-1).x, 799.6);
  send('move', { generation: 'previous-generation' });
  send('move', { source: 'another-channel' });
  assert.equal(harness.calls.pointerMove.length, 2000);
  send('trailStart');
  assert.equal(harness.calls.pointerDown.length, 2);
  send('cancel');
  assert.ok(harness.calls.pointerCancel.length > 0);
  send('move', { mode: 'touch', alwaysTrail: true });
  assert.equal(harness.fx.config.trailAlways, false);
  harness.window.setRenderingPaused(true);
  send('down');
  assert.equal(harness.calls.pointerDown.length, 2);
});

test('destroys renderer-owned resources before the document unloads', () =>
{
  const harness = createHarness();

  harness.windowListeners.get('beforeunload')();

  assert.equal(harness.calls.destroy, 1);
});
