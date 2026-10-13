(function ()
{
    'use strict';

    const DEFAULT_POINTER_ID = 1;
    const touchPointers = new Map();
    const defaultPointerContext = { input: {}, renderer: {} };
    const POINTER_STATE_KEYS = Object.freeze([
        'activePointerId', 'activePointerSource', 'lastPointerPosition', 'lastPointerTime',
        'trailDistanceSinceShard', 'currentTrailStroke', 'activeTrailOwnerId',
        'lastInputSampleSourceTime',
    ]);
    const INPUT_STATE_KEYS = Object.freeze([
        'pointerId', 'inputMode', 'alwaysTrailEnabled', 'effectiveAlwaysTrail',
        'activePointerKind', 'strokeId', 'lastBoomX', 'lastBoomY', 'lastBoomTime', 'lastMoveX', 'lastMoveY',
    ]);
    const DEFAULT_COLOR = '#4ca7ff';
    const BASE_RENDER_CONFIG = Object.freeze({
        inputSource: 'manual', effectBackend: 'webgl2', bloomBackend: 'webgl2',
        themeColorMode: 'hue-only', outputCompositing: 'browser-overlay',
        overlayAlphaPolicy: 'visual-max', overlayColorCompensation: 'none', overlayAlphaLimit: 0.85,
        hostCompositing: 'source-over', hostCompositingSurface: 'transparent-window',
        isolatedCompositing: false, lightBackgroundContrastAlpha: 0, maxDpr: 2,
        webgpuPreferHdr: true, webgpuHdrPeak: 3, webgpuHdrBrightness: 1,
        webgpuHdrColorPreservation: 0, webgpuHdrWhiteCore: 0.6,
        webgpuHdrWhiteStart: 1, webgpuHdrWhiteEnd: 5,
    });
    const ADVANCED_RENDER_OPTIONS = Object.freeze([
        'themeColorMode', 'maxDpr', 'overlayAlphaPolicy', 'overlayColorCompensation',
        'overlayAlphaLimit', 'isolatedCompositing', 'lightBackgroundContrastAlpha',
        'webgpuHdrPeak', 'webgpuHdrBrightness', 'webgpuHdrColorPreservation',
        'webgpuHdrWhiteCore', 'webgpuHdrWhiteStart', 'webgpuHdrWhiteEnd',
    ]);
    const ADVANCED_RENDER_MODES = Object.freeze({
        'full-webgl2': { effectBackend: 'webgl2', bloomBackend: 'webgl2', webgpuPreferHdr: true },
        'full-webgpu-sdr': { effectBackend: 'webgpu', bloomBackend: 'webgl2', webgpuPreferHdr: false },
        'full-webgpu': { effectBackend: 'webgpu', bloomBackend: 'webgl2', webgpuPreferHdr: true },
        'webgl2-bloom': { effectBackend: 'canvas2d', bloomBackend: 'webgl2', webgpuPreferHdr: true },
        'software-bloom': { effectBackend: 'canvas2d', bloomBackend: 'software', webgpuPreferHdr: true },
        'native-bloom': { effectBackend: 'canvas2d', bloomBackend: 'native', webgpuPreferHdr: true },
    });
    const HOST_GENERATION =
        typeof window.__basparkRendererGeneration === 'string'
            ? window.__basparkRendererGeneration
            : '';
    const DEFAULT_SETTINGS = Object.freeze(
        {
            trailScale: 1,
            clickScale: 1,
            opacity: 1,
            trailSpeed: 1,
            clickSpeed: 1,
            glowIntensity: 1,
        });
    const TRAIL_SCALE_PARAM_PATHS = Object.freeze(
        [
            'trail.geometryWidth',
            'trail.width',
            'trail.minVertexDistance',
            'trail.outerGlowWidth',
            'shards.trailRadius',
            'shards.trailSpeedMin',
            'shards.trailSpeedMax',
            'shards.trailSpacing',
        ]);
    const DOM_CONTENT_LOADED_OPTIONS =
    {
        once: true,
    };
    const state =
    {
        fx: null,
        pointerId: DEFAULT_POINTER_ID,
        initialized: false,
        paused: false,
        environmentInputSuppressed: false,
        inputMode: 'mouse',
        alwaysTrailEnabled: false,
        effectiveAlwaysTrail: false,
        activePointerKind: null,
        strokeId: null,
        color: DEFAULT_COLOR,
        settings:
        {
            ...DEFAULT_SETTINGS,
        },
        trailScaleBaseline: null,
        appliedTrailScaleRatio: 1,
        glowIntensityBaseline: null,
        appliedGlowIntensity: 1,
        advancedEnabled: false,
        advancedSettings: { config: {}, parameters: {} },
        advancedFxBaseline: null,
        appliedAdvancedConfig: {},
        appliedAdvancedParameters: {},
        advancedSignature: null,
        lastBoomX: -1,
        lastBoomY: -1,
        lastBoomTime: 0,
        lastMoveX: -1,
        lastMoveY: -1,
    };

    function clamp(value, minimum, maximum)
    {
        return Math.max(minimum, Math.min(maximum, value));
    }

    function hasOwnValue(value, key)
    {
        return Object.prototype.hasOwnProperty.call(value, key);
    }

    function errorMessage(error)
    {
        if (error instanceof Error)
        {
            return error.message;
        }

        return String(error);
    }

    function postHostMessage(type, detail = null)
    {
        const message =
        {
            source: 'baspark-fx',
            generation: HOST_GENERATION,
            type,
            ...(detail || Object.create(null)),
        };

        try
        {
            if (
                window.chrome &&
                window.chrome.webview &&
                typeof window.chrome.webview.postMessage === 'function'
            )
            {
                // 宿主统一按 JSON 字符串解析，避免 WebView2 对象封送行为随版本变化。
                window.chrome.webview.postMessage(JSON.stringify(message));
            }
        }
        catch (error)
        {
            console.error('[BASpark FX] 无法向宿主发送消息:', error);
        }
    }

    function reportError(phase, error)
    {
        const message = errorMessage(error);

        console.error(`[BASpark FX] ${phase}:`, error);
        postHostMessage(
            'error',
            {
                phase,
                message,
            });
    }

    function invokeFx(phase, action)
    {
        try
        {
            return action();
        }
        catch (error)
        {
            reportError(phase, error);
            return false;
        }
    }

    function resetInputCache()
    {
        state.activePointerKind = null;
        state.lastBoomX = -1;
        state.lastBoomY = -1;
        state.lastBoomTime = 0;
        state.lastMoveX = -1;
        state.lastMoveY = -1;
    }

    function capturePointerContext(context)
    {
        for (const key of INPUT_STATE_KEYS) context.input[key] = state[key];
        for (const key of POINTER_STATE_KEYS) context.renderer[key] = state.fx[key];
    }

    function restorePointerContext(context)
    {
        Object.assign(state, context.input);
        Object.assign(state.fx, context.renderer);
        if (state.fx.config.trailAlways !== state.effectiveAlwaysTrail) applyInputContext();
    }

    function withTouchPointer(pointerId, action, release = false)
    {
        if (!state.fx) return false;
        capturePointerContext(defaultPointerContext);
        const contact = touchPointers.get(pointerId) || {
            input: {
                ...defaultPointerContext.input, pointerId, inputMode: 'touch', effectiveAlwaysTrail: false,
                activePointerKind: null, strokeId: null,
                lastBoomX: -1, lastBoomY: -1, lastBoomTime: 0, lastMoveX: -1, lastMoveY: -1,
            },
            renderer: {
                activePointerId: null, activePointerSource: null, lastPointerPosition: null, lastPointerTime: 0,
                trailDistanceSinceShard: 0, currentTrailStroke: null, activeTrailOwnerId: null,
            },
        };
        restorePointerContext(contact);
        try
        {
            return action();
        }
        finally
        {
            if (release) touchPointers.delete(pointerId);
            else
            {
                capturePointerContext(contact);
                touchPointers.set(pointerId, contact);
            }
            restorePointerContext(defaultPointerContext);
        }
    }

    function releaseTouchPointers()
    {
        for (const pointerId of touchPointers.keys())
            withTouchPointer(pointerId, () => state.fx.pointerUp(pointerId), true);
    }

    function canAcceptHostInput()
    {
        return !state.paused && !state.environmentInputSuppressed && state.fx !== null;
    }

    function tagCurrentTrail()
    {
        if (state.fx.currentTrailStroke && state.strokeId !== null)
        {
            state.fx.currentTrailStroke.basparkStrokeId = state.strokeId;
            state.fx.currentTrailStroke.basparkPointerId = state.pointerId;
        }
    }

    function pointerType()
    {
        return state.inputMode === 'touch' ? 'touch' : 'mouse';
    }

    function toCanvasPoint(percentX, percentY, allowOutside = false)
    {
        if (!state.fx)
        {
            return null;
        }

        const normalizedX = Number(percentX);
        const normalizedY = Number(percentY);

        if (!Number.isFinite(normalizedX) || !Number.isFinite(normalizedY))
        {
            return null;
        }

        // C# 传入窗口内归一化坐标；公开 API 要求 Canvas 局部 CSS 像素。
        return (
            {
                x: (allowOutside ? normalizedX : clamp(normalizedX, 0, 1)) * state.fx.width,
                y: (allowOutside ? normalizedY : clamp(normalizedY, 0, 1)) * state.fx.height,
                pointerId: state.pointerId,
                pointerType: pointerType(),
            });
    }

    function cancelPointerImmediately()
    {
        const fx = state.fx;
        const owners = new Set(fx.trailStrokes
            .filter((stroke) => stroke.basparkPointerId === state.pointerId)
            .map((stroke) => stroke.ownerId));
        const accepted = fx.pointerCancel(state.pointerId);

        if (state.pointerId === DEFAULT_POINTER_ID) fx.clearTrail();
        else
        {
            fx.trailStrokes = fx.trailStrokes.filter((stroke) => stroke.basparkPointerId !== state.pointerId);
            fx.shards = fx.shards.filter((shard) => shard.kind !== 'trail' || !owners.has(shard.ownerId));
            for (const owner of owners) fx.trailShardCounts.delete(owner);
        }
        state.activePointerKind = null;
        return accepted;
    }

    function cancelActivePointer()
    {
        if (!state.fx || state.activePointerKind === null)
        {
            return false;
        }

        return cancelPointerImmediately();
    }

    function applyInputContext()
    {
        if (!state.fx)
        {
            return;
        }

        state.fx.updateConfig(
            {
                trailAlways: state.effectiveAlwaysTrail,
            });
    }

    function applyColor()
    {
        if (!state.fx)
        {
            return;
        }

        state.fx.setThemeColor(state.color);
    }

    function applyEffectSettings()
    {
        if (!state.fx)
        {
            return;
        }

        state.fx.updateConfig(
            {
                scale: Math.max(0.01, state.settings.clickScale / 1.5),
                opacity: state.settings.opacity,
                trailTimeScale: state.settings.trailSpeed,
                clickTimeScale: state.settings.clickSpeed,
            });
        applyTrailScale();
        applyTrailShardScale();
        applyGlowIntensity();
    }

    function applyAdvancedSettings()
    {
        if (!state.fx || !state.advancedFxBaseline) return true;
        const config = state.advancedEnabled ? state.advancedSettings.config || {} : {};
        const parameters = state.advancedEnabled ? state.advancedSettings.parameters || {} : {};
        const signature = JSON.stringify({ config, parameters });
        if (signature === state.advancedSignature) return true;
        const configPatch = {};
        for (const key of new Set([...Object.keys(state.appliedAdvancedConfig), ...Object.keys(config)]))
        {
            if (key === 'renderMode')
            {
                const mode = config.renderMode || 'full-webgl2';
                if (!hasOwnValue(ADVANCED_RENDER_MODES, mode)) throw new Error('Invalid advanced render mode.');
                Object.assign(configPatch, ADVANCED_RENDER_MODES[mode]);
            }
            else if (ADVANCED_RENDER_OPTIONS.includes(key))
            {
                configPatch[key] = hasOwnValue(config, key) ? config[key] : BASE_RENDER_CONFIG[key];
            }
            else throw new Error(`Unsupported advanced render setting: ${key}`);
        }
        const patch = {};
        const baselineChanges = {};
        for (const path of new Set([...Object.keys(state.appliedAdvancedParameters), ...Object.keys(parameters)]))
        {
            if (!/^(rings|disk|shards|trail|bloom)\.[A-Za-z]+$/.test(path) || path === 'bloom.intensity')
                throw new Error(`Unsupported advanced effect parameter: ${path}`);
            const value = hasOwnValue(parameters, path) ? parameters[path] : readFxConfigNumber(state.advancedFxBaseline, path);
            if (!Number.isFinite(value)) throw new Error(`Invalid advanced effect parameter: ${path}`);
            if (TRAIL_SCALE_PARAM_PATHS.includes(path))
            {
                baselineChanges[path] = value;
                patch[path] = value * getTrailScaleRatio();
            }
            else patch[path] = value;
        }
        if (Object.keys(patch).length && state.fx.setFxParams(patch, { strict: true })?.committed !== true)
            throw new Error('Advanced effect parameters could not be applied.');
        Object.assign(state.trailScaleBaseline, baselineChanges);
        if (Object.keys(configPatch).length) state.fx.updateConfig(configPatch);
        state.appliedAdvancedConfig = { ...config };
        state.appliedAdvancedParameters = { ...parameters };
        state.advancedSignature = signature;
        return true;
    }

    window.updateBaClickAdvancedSettings = function (enabled, settings)
    {
        state.advancedEnabled = enabled === true;
        state.advancedSettings = settings && typeof settings === 'object' ? settings : { config: {}, parameters: {} };
        return invokeFx('updateBaClickAdvancedSettings', applyAdvancedSettings);
    };

    function parseRgbColor(rgbString)
    {
        const channels = String(rgbString).split(',').map((channel) =>
        {
            return Number(channel.trim());
        });

        if (
            channels.length !== 3 ||
            channels.some((channel) => !Number.isFinite(channel))
        )
        {
            return null;
        }

        return `#${channels.map((channel) =>
        {
            return Math.round(clamp(channel, 0, 255))
                .toString(16)
                .padStart(2, '0');
        }).join('')}`;
    }

    function normalizeSpeed(value, fallback)
    {
        const numeric = Number(value);

        if (!Number.isFinite(numeric))
        {
            return fallback;
        }

        return clamp(numeric, 0.2, 3);
    }

    function normalizeScale(value, fallback)
    {
        const numeric = Number(value);

        if (!Number.isFinite(numeric))
        {
            return fallback;
        }

        return clamp(numeric, 0.5, 3);
    }

    function normalizeGlowIntensity(value, fallback)
    {
        const numeric = Number(value);

        if (!Number.isFinite(numeric))
        {
            return fallback;
        }

        return clamp(numeric, 0, 3);
    }

    function readFxConfigNumber(config, path)
    {
        let value = config;

        for (const segment of path.split('.'))
        {
            if (!value || typeof value !== 'object')
            {
                return null;
            }

            value = value[segment];
        }

        const numeric = Number(value);
        return Number.isFinite(numeric) ? numeric : null;
    }

    function captureTrailScaleBaseline()
    {
        if (!state.fx || typeof state.fx.getFxConfig !== 'function')
        {
            return;
        }

        const config = state.fx.getFxConfig();
        const baseline = Object.create(null);

        for (const path of TRAIL_SCALE_PARAM_PATHS)
        {
            const value = readFxConfigNumber(config, path);

            if (!Number.isFinite(value))
            {
                return;
            }

            baseline[path] = value;
        }

        state.trailScaleBaseline = baseline;
        state.appliedTrailScaleRatio = 1;
    }

    function captureGlowIntensityBaseline()
    {
        if (!state.fx || typeof state.fx.getFxConfig !== 'function')
        {
            return;
        }

        const config = state.fx.getFxConfig();
        const intensity = readFxConfigNumber(config, 'bloom.intensity');

        if (!Number.isFinite(intensity))
        {
            return;
        }

        state.glowIntensityBaseline = intensity;
        state.appliedGlowIntensity = 1;
    }

    function getTrailScaleRatio()
    {
        return state.settings.trailScale / state.settings.clickScale;
    }

    function applyTrailScale()
    {
        if (
            !state.fx ||
            !state.trailScaleBaseline ||
            typeof state.fx.setFxParams !== 'function'
        )
        {
            return;
        }

        const ratio = getTrailScaleRatio();

        if (
            !Number.isFinite(ratio) ||
            Math.abs(ratio - state.appliedTrailScaleRatio) < 0.000001
        )
        {
            return;
        }

        const patch = Object.create(null);

        for (const path of TRAIL_SCALE_PARAM_PATHS)
        {
            patch[path] = state.trailScaleBaseline[path] * ratio;
        }

        const result = state.fx.setFxParams(patch, { strict: true });

        if (result?.committed === true)
        {
            state.appliedTrailScaleRatio = ratio;
        }
        else
        {
            console.warn('[BASpark FX] 拖尾缩放参数未能应用。');
        }
    }

    function applyTrailShardScale()
    {
        if (!Array.isArray(state.fx?.shards))
        {
            return;
        }

        const ratio = getTrailScaleRatio();

        if (!Number.isFinite(ratio))
        {
            return;
        }

        for (const shard of state.fx.shards)
        {
            if (shard?.kind !== 'trail' || !Number.isFinite(shard.size))
            {
                continue;
            }

            const baseSize = Number.isFinite(shard.__basparkTrailBaseSize)
                ? shard.__basparkTrailBaseSize
                : shard.size;

            shard.__basparkTrailBaseSize = baseSize;
            shard.size = baseSize * ratio;
        }
    }

    function applyGlowIntensity()
    {
        if (
            !state.fx ||
            !state.glowIntensityBaseline ||
            typeof state.fx.setFxParams !== 'function'
        )
        {
            return;
        }

        const glowIntensity = state.settings.glowIntensity;

        if (
            !Number.isFinite(glowIntensity) ||
            Math.abs(glowIntensity - state.appliedGlowIntensity) < 0.000001
        )
        {
            return;
        }

        const result = state.fx.setFxParams(
            {
                'bloom.intensity': state.glowIntensityBaseline * glowIntensity,
            },
            {
                strict: true,
            });

        if (result?.committed === true)
        {
            state.appliedGlowIntensity = glowIntensity;
        }
        else
        {
            console.warn('[BASpark FX] 辉光亮度参数未能应用。');
        }
    }

    window.externalBoom = function (percentX, percentY)
    {
        if (!canAcceptHostInput())
        {
            return false;
        }

        const numericX = Number(percentX);
        const numericY = Number(percentY);
        const now = Date.now();

        if (
            numericX === state.lastBoomX &&
            numericY === state.lastBoomY &&
            now - state.lastBoomTime < 25
        )
        {
            return false;
        }

        const point = toCanvasPoint(numericX, numericY);

        if (!point)
        {
            return false;
        }

        state.lastBoomX = numericX;
        state.lastBoomY = numericY;
        state.lastBoomTime = now;

        return invokeFx('externalBoom', function ()
        {
            // 丢失抬起事件时先恢复指针状态，避免后续点击被单指针上限永久拒绝。
            cancelActivePointer();
            const accepted = state.fx.pointerDown(point);

            if (accepted)
            {
                state.activePointerKind = 'press';
                tagCurrentTrail();
            }

            return accepted;
        });
    };

    window.externalTrailStart = function (percentX, percentY, allowOutside = false)
    {
        if (!canAcceptHostInput())
        {
            return false;
        }

        const point = toCanvasPoint(percentX, percentY, allowOutside);

        if (!point)
        {
            return false;
        }

        state.lastMoveX = Number(percentX);
        state.lastMoveY = Number(percentY);

        return invokeFx('externalTrailStart', function ()
        {
            // BAClickFX starts a pressed trail through pointerDown. Temporarily
            // disabling clicks preserves that input state without adding a boom.
            const clickEnabled = state.fx.getConfig().clickEnabled !== false;
            cancelActivePointer();
            state.fx.updateConfig(
                {
                    clickEnabled: false,
                });

            try
            {
                const accepted = state.fx.pointerDown(point);

                if (accepted)
                {
                    state.activePointerKind = 'press';
                    tagCurrentTrail();
                }

                return accepted;
            }
            finally
            {
                state.fx.updateConfig(
                    {
                        clickEnabled,
                    });
            }
        });
    };

    window.externalMove = function (percentX, percentY, allowOutside = false)
    {
        if (!canAcceptHostInput())
        {
            return false;
        }

        const numericX = Number(percentX);
        const numericY = Number(percentY);

        if (
            numericX === state.lastMoveX &&
            numericY === state.lastMoveY
        )
        {
            return false;
        }

        const point = toCanvasPoint(numericX, numericY, allowOutside);

        if (!point)
        {
            return false;
        }

        state.lastMoveX = numericX;
        state.lastMoveY = numericY;

        return invokeFx('externalMove', function ()
        {
            const accepted = state.fx.pointerMove(point);

            if (accepted)
            {
                applyTrailShardScale();
                tagCurrentTrail();
            }

            if (
                accepted &&
                state.activePointerKind === null &&
                state.effectiveAlwaysTrail
            )
            {
                state.activePointerKind = 'hover';
            }

            return accepted;
        });
    };

    window.externalTrailContinue = function (history, percentX, percentY, pressed, beginStroke, finishStroke)
    {
        if (!canAcceptHostInput() || !toCanvasPoint(percentX, percentY, true) || !Array.isArray(history))
        {
            return false;
        }

        const fx = state.fx;
        const normalizeInput = fx._normalizePointerInput;
        const inputTime = fx._getTrailInputTime;
        const currentTime = typeof inputTime === 'function' ? inputTime.call(fx) : 0;
        const timeScale = Number(fx.getConfig().trailTimeScale) || 1;
        const lifetime = Number(fx.getFxConfig().trail.lifetimeMs) || 300;
        const validSamples = history.slice(-1024).filter((sample) =>
            Array.isArray(sample) && sample.length === 3 && sample.every(Number.isFinite) && sample[2] >= 0);
        const recentSamples = validSamples.filter((sample) => sample[2] * timeScale <= lifetime);
        if (recentSamples.length === 0 && validSamples.length > 0)
        {
            const lastSample = validSamples.at(-1);
            recentSamples.push([lastSample[0], lastSample[1], 0]);
        }

        let replayTime = currentTime;
        if (typeof normalizeInput === 'function')
        {
            fx._normalizePointerInput = function (input)
            {
                const normalized = normalizeInput.call(this, input);
                if (normalized)
                {
                    normalized.x = input.x;
                    normalized.y = input.y;
                }
                return normalized;
            };
        }
        if (typeof inputTime === 'function') fx._getTrailInputTime = () => replayTime;

        try
        {
            if (beginStroke)
            {
                window.externalUp();
                state.activePointerKind = null;
                if (state.strokeId !== null && Array.isArray(fx.trailStrokes))
                {
                    const owners = new Set(fx.trailStrokes
                        .filter((stroke) => stroke.basparkStrokeId === state.strokeId)
                        .map((stroke) => stroke.ownerId));
                    fx.trailStrokes = fx.trailStrokes.filter((stroke) => stroke.basparkStrokeId !== state.strokeId);
                    if (Array.isArray(fx.shards))
                    {
                        fx.shards = fx.shards.filter((shard) => shard.kind !== 'trail' || !owners.has(shard.ownerId));
                    }
                    for (const owner of owners) fx.trailShardCounts?.delete(owner);
                }
                state.lastMoveX = NaN;
                state.lastMoveY = NaN;
                const samples = recentSamples.length > 0 ? recentSamples : [[Number(percentX), Number(percentY), 0]];
                for (let index = 0; index < samples.length; index++)
                {
                    const sample = samples[index];
                    replayTime = Math.max(0, currentTime - sample[2] * timeScale);
                    if (index === 0 && pressed)
                    {
                        window.externalTrailStart(sample[0], sample[1], true);
                    }
                    else
                    {
                        window.externalMove(sample[0], sample[1], true);
                    }
                }
            }

            replayTime = currentTime;
            const accepted = window.externalMove(percentX, percentY, true);
            if (finishStroke) window.externalUp();
            return accepted;
        }
        finally
        {
            if (typeof normalizeInput === 'function') fx._normalizePointerInput = normalizeInput;
            if (typeof inputTime === 'function') fx._getTrailInputTime = inputTime;
        }
    };

    window.externalUp = function ()
    {
        if (state.paused || !state.fx)
        {
            return false;
        }

        return invokeFx('externalUp', function ()
        {
            const accepted = state.fx.pointerUp(state.pointerId);

            if (accepted)
            {
                state.activePointerKind = null;
            }

            return accepted;
        });
    };

    window.setEnvironmentInputSuppressed = function (suppressed)
    {
        const nextSuppressed = Boolean(suppressed);

        if (nextSuppressed === state.environmentInputSuppressed)
        {
            return false;
        }

        if (nextSuppressed)
        {
            // End the active stroke without cancelling its already-emitted visuals.
            window.externalUp();
            releaseTouchPointers();
            resetInputCache();
        }

        state.environmentInputSuppressed = nextSuppressed;
        return true;
    };

    window.externalCancel = function ()
    {
        if (!state.fx)
        {
            resetInputCache();
            return false;
        }

        return invokeFx('externalCancel', function ()
        {
            if (state.pointerId === DEFAULT_POINTER_ID) releaseTouchPointers();
            const accepted = cancelPointerImmediately();

            resetInputCache();
            return accepted;
        });
    };

    window.setRenderingPaused = function (paused)
    {
        state.paused = Boolean(paused);

        if (!state.fx)
        {
            return;
        }

        invokeFx('setRenderingPaused', function ()
        {
            if (state.paused)
            {
                releaseTouchPointers();
                resetInputCache();
                state.fx.setPaused(
                    true,
                    {
                        clear: true,
                    });
                return true;
            }

            state.fx.setPaused(false);
            return true;
        });
    };

    window.setInputContext = function (mode, alwaysTrailEnabled)
    {
        const nextMode = mode === 'touch' ? 'touch' : 'mouse';
        const nextAlwaysTrailEnabled = Boolean(alwaysTrailEnabled);
        const nextEffectiveAlwaysTrail =
            nextMode === 'mouse' && nextAlwaysTrailEnabled;

        if (
            nextMode !== state.inputMode ||
            (
                !nextEffectiveAlwaysTrail &&
                state.activePointerKind === 'hover'
            )
        )
        {
            invokeFx('setInputContext.cancel', function ()
            {
                return cancelActivePointer();
            });
        }

        state.inputMode = nextMode;
        state.alwaysTrailEnabled = nextAlwaysTrailEnabled;
        state.effectiveAlwaysTrail = nextEffectiveAlwaysTrail;

        invokeFx('setInputContext', function ()
        {
            applyInputContext();
            return true;
        });
    };

    window.updateColor = function (rgbString)
    {
        const color = parseRgbColor(rgbString);

        if (!color)
        {
            // 配置损坏不代表渲染器失效，保留当前颜色即可继续运行。
            console.warn('[BASpark FX] 忽略非法 RGB 颜色:', rgbString);
            return false;
        }

        state.color = color;

        return invokeFx('updateColor', function ()
        {
            applyColor();
            return true;
        });
    };

    window.updateEffectSettings = function (
        trailScale,
        clickScale,
        opacity,
        trailSpeed,
        clickSpeed,
        glowIntensity
    )
    {
        const numericOpacity = Number(opacity);
        const safeTrailSpeed = normalizeSpeed(
            trailSpeed,
            DEFAULT_SETTINGS.trailSpeed,
        );

        state.settings =
        {
            trailScale: normalizeScale(
                trailScale,
                DEFAULT_SETTINGS.trailScale,
            ),
            clickScale: normalizeScale(
                clickScale,
                DEFAULT_SETTINGS.clickScale,
            ),
            opacity: Number.isFinite(numericOpacity)
                ? clamp(numericOpacity, 0.1, 1)
                : DEFAULT_SETTINGS.opacity,
            trailSpeed: safeTrailSpeed,
            clickSpeed: normalizeSpeed(clickSpeed, safeTrailSpeed),
            glowIntensity: normalizeGlowIntensity(
                glowIntensity,
                DEFAULT_SETTINGS.glowIntensity,
            ),
        };

        return invokeFx('updateEffectSettings', function ()
        {
            applyEffectSettings();
            return true;
        });
    };

    function readBackendState(detail = Object.create(null))
    {
        const config = state.fx.getConfig();
        const requestedEffectBackend =
            detail.requestedEffectBackend || config.effectBackend;
        const resolvedEffectBackend =
            detail.resolvedEffectBackend || config.resolvedEffectBackend;
        const requestedBloomBackend =
            detail.requestedBloomBackend || config.bloomBackend;
        const resolvedBloomBackend =
            detail.resolvedBloomBackend || config.resolvedBloomBackend;
        const requestedHostCompositing =
            detail.requestedHostCompositing ||
            config.hostCompositing ||
            'unknown';
        const resolvedHostCompositing =
            detail.resolvedHostCompositing ||
            config.resolvedHostCompositing ||
            config.hostCompositing ||
            'unknown';
        const hostCompositingSurface =
            detail.hostCompositingSurface ||
            config.hostCompositingSurface ||
            'unknown';
        const compositingWarning =
            detail.compositingWarning ?? config.compositingWarning ?? null;

        return (
            {
                backend: resolvedEffectBackend === 'webgl2'
                    ? resolvedEffectBackend
                    : resolvedBloomBackend,
                requestedEffectBackend,
                resolvedEffectBackend,
                requestedBloomBackend,
                resolvedBloomBackend,
                requestedHostCompositing,
                resolvedHostCompositing,
                hostCompositingSurface,
                compositingWarning,
            });
    }

    function handleBackendChange(event)
    {
        const backendState = readBackendState(
            event.detail || Object.create(null),
        );

        postHostMessage(
            'backend',
            backendState,
        );

        if (
            backendState.resolvedEffectBackend !== 'webgl2' &&
            backendState.resolvedEffectBackend !== 'webgpu' &&
            backendState.resolvedBloomBackend === 'software' &&
            state.fx.config.bloomBackend !== 'software'
        )
        {
            // 全屏 Float32 软件 Bloom 对桌面覆盖层代价过高，GPU 不可用时改用原生辉光。
            state.fx.updateConfig(
                {
                    bloomBackend: 'native',
                });
        }
    }

    function initialize()
    {
        if (state.initialized)
        {
            return;
        }

        state.initialized = true;

        try
        {
            if (
                !window.BAClickFX ||
                typeof window.BAClickFX.BAClickFX !== 'function'
            )
            {
                throw new Error('ba-click-fx IIFE 未在适配器之前注入');
            }

            state.fx = new window.BAClickFX.BAClickFX({ ...BASE_RENDER_CONFIG });
            captureTrailScaleBaseline();
            captureGlowIntensityBaseline();
            state.advancedFxBaseline = state.fx.getFxConfig();

            const bloomBackendEventName =
                window.BAClickFX.BLOOM_BACKEND_CHANGE_EVENT ||
                'baclickfxbackendchange';
            const effectBackendEventName =
                window.BAClickFX.EFFECT_BACKEND_CHANGE_EVENT ||
                'baclickfxeffectbackendchange';
            const hostCompositingEventName =
                window.BAClickFX.HOST_COMPOSITING_CHANGE_EVENT ||
                'baclickfxhostcompositingchange';

            state.fx.canvas.addEventListener(
                bloomBackendEventName,
                handleBackendChange,
            );
            state.fx.canvas.addEventListener(
                effectBackendEventName,
                handleBackendChange,
            );
            state.fx.canvas.addEventListener(
                hostCompositingEventName,
                handleBackendChange,
            );

            applyInputContext();
            applyColor();
            applyEffectSettings();
            applyAdvancedSettings();

            if (state.paused)
            {
                state.fx.setPaused(
                    true,
                    {
                        clear: true,
                    });
            }

            postHostMessage(
                'ready',
                readBackendState(),
            );
        }
        catch (error)
        {
            reportError('initialize', error);
        }
    }

    function installCompatibilityShims()
    {
        if (typeof Array.prototype.at !== 'function')
        {
            Object.defineProperty(
                Array.prototype,
                'at',
                {
                    configurable: true,
                    writable: true,
                    value: function (index)
                    {
                        const length = this.length >>> 0;
                        const integer = Math.trunc(Number(index) || 0);
                        const offset = integer < 0 ? length + integer : integer;

                        if (offset < 0 || offset >= length)
                        {
                            return undefined;
                        }

                        return this[offset];
                    },
                });
        }

        if (typeof window.structuredClone !== 'function')
        {
            // 上游只克隆由数字、布尔值、数组和普通对象组成的配置快照。
            window.structuredClone = function (value)
            {
                return JSON.parse(JSON.stringify(value));
            };
        }
    }

    window.addEventListener('error', function (event)
    {
        reportError('window.error', event.error || event.message);
    });

    window.addEventListener('unhandledrejection', function (event)
    {
        reportError('unhandledrejection', event.reason);
    });

    window.addEventListener('beforeunload', function ()
    {
        if (!state.fx)
        {
            return;
        }

        invokeFx('beforeunload', function ()
        {
            state.fx.destroy();
            touchPointers.clear();
            state.fx = null;
            resetInputCache();
            return true;
        });
    });

    window.externalPointerInput = function (input)
    {
        const pointerId = input.pointerId ?? DEFAULT_POINTER_ID;
        if (!Number.isSafeInteger(pointerId) || pointerId < DEFAULT_POINTER_ID) return false;
        if (pointerId !== DEFAULT_POINTER_ID)
        {
            const release = input.type === 'up' || input.type === 'cancel';
            if (release && !touchPointers.has(pointerId)) return false;
            if (!release && (!canAcceptHostInput() || touchPointers.size >= 128 && !touchPointers.has(pointerId))) return false;
            return withTouchPointer(pointerId, () => dispatchPointerInput(input), release);
        }
        return dispatchPointerInput(input);
    };

    function dispatchPointerInput(input)
    {
        if (Number.isSafeInteger(input.strokeId) && input.strokeId >= 0)
        {
            state.strokeId = input.strokeId;
        }

        if (
            input.mode !== state.inputMode ||
            Boolean(input.alwaysTrail) !== state.alwaysTrailEnabled
        )
        {
            window.setInputContext(input.mode, input.alwaysTrail);
        }

        switch (input.type)
        {
            case 'down':
                return window.externalBoom(input.x, input.y);
            case 'trailStart':
                return window.externalTrailStart(input.x, input.y);
            case 'trailContinue':
                return window.externalTrailContinue(input.history, input.x, input.y,
                    Boolean(input.pressed), Boolean(input.beginStroke), Boolean(input.finishStroke));
            case 'move':
                return window.externalMove(input.x, input.y);
            case 'up':
                return window.externalUp();
            case 'cancel':
                return window.externalCancel();
        }
    }

    function handleHostInput(event)
    {
        const input = event.data;
        if (!input || input.source !== 'baspark-input' || input.generation !== HOST_GENERATION) return;
        window.externalPointerInput(input);
    }

    window.chrome?.webview?.addEventListener('message', handleHostInput);
    installCompatibilityShims();

    if (document.readyState === 'loading')
    {
        document.addEventListener(
            'DOMContentLoaded',
            initialize,
            DOM_CONTENT_LOADED_OPTIONS,
        );
    }
    else
    {
        initialize();
    }
})();
