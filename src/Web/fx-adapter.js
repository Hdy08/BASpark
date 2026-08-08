(function ()
{
    'use strict';

    const POINTER_ID = 1;
    const DEFAULT_COLOR = '#2dafff';
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
        });
    const TRAIL_SCALE_PARAM_PATHS = Object.freeze(
        [
            'trail.geometryWidth',
            'trail.width',
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
        initialized: false,
        paused: false,
        environmentInputSuppressed: false,
        inputMode: 'mouse',
        alwaysTrailEnabled: false,
        effectiveAlwaysTrail: false,
        activePointerKind: null,
        color: DEFAULT_COLOR,
        settings:
        {
            ...DEFAULT_SETTINGS,
        },
        trailScaleBaseline: null,
        appliedTrailScaleRatio: 1,
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

    function canAcceptHostInput()
    {
        return !state.paused && !state.environmentInputSuppressed && state.fx !== null;
    }

    function pointerType()
    {
        return state.inputMode === 'touch' ? 'touch' : 'mouse';
    }

    function toCanvasPoint(percentX, percentY)
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
                x: clamp(normalizedX, 0, 1) * state.fx.width,
                y: clamp(normalizedY, 0, 1) * state.fx.height,
                pointerId: POINTER_ID,
                pointerType: pointerType(),
            });
    }

    function cancelPointerImmediately()
    {
        const accepted = state.fx.pointerCancel(POINTER_ID);

        // 宿主取消表示输入所有权切换；清除全部拖尾，避免上一所有者的残留。
        state.fx.clearTrail();
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
    }

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
            }

            return accepted;
        });
    };

    window.externalTrailStart = function (percentX, percentY)
    {
        if (!canAcceptHostInput())
        {
            return false;
        }

        const point = toCanvasPoint(percentX, percentY);

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

    window.externalMove = function (percentX, percentY)
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

        const point = toCanvasPoint(numericX, numericY);

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

    window.externalUp = function ()
    {
        if (state.paused || !state.fx)
        {
            return false;
        }

        return invokeFx('externalUp', function ()
        {
            const accepted = state.fx.pointerUp(POINTER_ID);

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
        clickSpeed
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
            backendState.resolvedBloomBackend === 'software'
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

            state.fx = new window.BAClickFX.BAClickFX(
                {
                    inputSource: 'manual',
                    // WebView2 无法读取窗口后的桌面背景；使用 source-over，
                    // 再以浅色背景补偿和 Alpha 上限提高未知背景上的可见性。
                    effectBackend: 'webgl2',
                    bloomBackend: 'webgl2',
                    outputCompositing: 'browser-overlay',
                    overlayAlphaPolicy: 'visual-max',
                    overlayColorCompensation: 'bright-core',
                    overlayAlphaLimit: 0.85,
                    hostCompositing: 'source-over',
                    hostCompositingSurface: 'transparent-window',
                    isolatedCompositing: false,
                    lightBackgroundContrastAlpha: 0,
                    maxDpr: 2,
                });
            captureTrailScaleBaseline();

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
            state.fx = null;
            resetInputCache();
            return true;
        });
    });

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
