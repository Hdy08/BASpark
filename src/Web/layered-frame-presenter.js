(function () {
    'use strict';

    const tileSize = 16;
    const vertexSource = `#version 300 es
        out vec2 uv;
        void main() {
            vec2 corner = vec2((gl_VertexID & 1) * 2, (gl_VertexID >> 1) * 2);
            uv = corner;
            gl_Position = vec4(corner * 2.0 - 1.0, 0, 1);
        }`;
    const bitmapSource = `#version 300 es
        precision highp float;
        in vec2 uv;
        out vec4 color;
        uniform sampler2D image;
        void main() { color = texture(image, uv).bgra; }`;
    const maskSource = `#version 300 es
        precision highp float;
        out vec4 color;
        uniform sampler2D image;
        void main() {
            ivec2 start = ivec2(gl_FragCoord.xy) * 4;
            ivec2 last = textureSize(image, 0) - 1;
            vec4 maximum = vec4(0);
            for (int row = 0; row < 4; ++row) {
                for (int col = 0; col < 4; ++col) {
                    maximum = max(maximum, texelFetch(image, min(start + ivec2(col, row), last), 0));
                }
            }
            color = vec4(any(greaterThan(maximum, vec4(0))) ? 1.0 : 0.0);
        }`;

    function getDamage(mask, columns, rows, sourceWidth, sourceHeight, width, height, previous) {
        let left = columns, bottom = rows, right = 0, top = 0;
        for (let row = 0; row < rows; ++row) {
            for (let column = 0; column < columns; ++column) {
                if (!mask[(row * columns + column) * 4]) continue;
                left = Math.min(left, column);
                bottom = Math.min(bottom, row);
                right = Math.max(right, column + 1);
                top = Math.max(top, row + 1);
            }
        }
        const current = left >= right ? null : [
            Math.max(0, Math.floor((left * tileSize - 1) * width / sourceWidth)),
            Math.max(0, Math.floor((bottom * tileSize - 1) * height / sourceHeight)),
            Math.min(width, Math.ceil((right * tileSize + 1) * width / sourceWidth)),
            Math.min(height, Math.ceil((top * tileSize + 1) * height / sourceHeight)),
        ];
        const dirty = current && previous ? [
            Math.min(current[0], previous[0]), Math.min(current[1], previous[1]),
            Math.max(current[2], previous[2]), Math.max(current[3], previous[3]),
        ] : current || previous;
        return { current, dirty };
    }

    function makeProgram(gl, fragmentSource) {
        const program = gl.createProgram();
        try {
            for (const [type, source] of [[gl.VERTEX_SHADER, vertexSource], [gl.FRAGMENT_SHADER, fragmentSource]]) {
                const shader = gl.createShader(type);
                gl.shaderSource(shader, source);
                gl.compileShader(shader);
                gl.attachShader(program, shader);
                gl.deleteShader(shader);
            }
            gl.linkProgram(program);
            if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
            return program;
        } catch (error) {
            gl.deleteProgram(program);
            throw error;
        }
    }

    class FrameTransfer {
        constructor(gl, width, height) {
            this.gl = gl;
            this.width = width;
            this.height = height;
            this.sourceWidth = gl.drawingBufferWidth;
            this.sourceHeight = gl.drawingBufferHeight;
            this.textures = [];
            this.framebuffers = [];
            try {
                this.bitmapProgram = makeProgram(gl, bitmapSource);
                this.maskProgram = makeProgram(gl, maskSource);
                this.vertexArray = gl.createVertexArray();
                this.pack = gl.createBuffer();
                this.source = this.makeTexture(this.sourceWidth, this.sourceHeight);
                this.bitmap = this.makeTarget(width, height);
                this.masks = [4, tileSize].map(divisor => this.makeTarget(
                    Math.ceil(this.sourceWidth / divisor), Math.ceil(this.sourceHeight / divisor)));
                const finalMask = this.masks[1];
                this.maskBytes = new Uint8Array(finalMask.width * finalMask.height * 4);
            } catch (error) {
                this.dispose();
                throw error;
            } finally {
                gl.bindFramebuffer(gl.FRAMEBUFFER, null);
                gl.bindTexture(gl.TEXTURE_2D, null);
            }
        }

        makeTexture(width, height) {
            const gl = this.gl;
            const texture = gl.createTexture();
            this.textures.push(texture);
            gl.bindTexture(gl.TEXTURE_2D, texture);
            gl.texStorage2D(gl.TEXTURE_2D, 1, gl.RGBA8, width, height);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
            return texture;
        }

        makeTarget(width, height) {
            const gl = this.gl;
            const texture = this.makeTexture(width, height);
            const framebuffer = gl.createFramebuffer();
            this.framebuffers.push(framebuffer);
            gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
            gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0);
            if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) {
                throw new Error('The overlay frame framebuffer is incomplete.');
            }
            return { texture, framebuffer, width, height };
        }

        queue(pixels, prepare = false) {
            const gl = this.gl;
            try {
                gl.bindFramebuffer(gl.FRAMEBUFFER, null);
                gl.activeTexture(gl.TEXTURE0);
                gl.bindTexture(gl.TEXTURE_2D, this.source);
                gl.copyTexSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 0, 0, this.sourceWidth, this.sourceHeight);
                gl.disable(gl.BLEND);
                gl.disable(gl.SCISSOR_TEST);
                gl.bindVertexArray(this.vertexArray);
                gl.useProgram(this.maskProgram);
                for (const target of this.masks) {
                    gl.bindFramebuffer(gl.FRAMEBUFFER, target.framebuffer);
                    gl.viewport(0, 0, target.width, target.height);
                    gl.drawArrays(gl.TRIANGLES, 0, 3);
                    gl.bindTexture(gl.TEXTURE_2D, target.texture);
                }
                const { width: columns, height: rows } = this.masks[1];
                gl.readPixels(0, 0, columns, rows, gl.RGBA, gl.UNSIGNED_BYTE, this.maskBytes);
                const { current, dirty } = getDamage(this.maskBytes, columns, rows, this.sourceWidth,
                    this.sourceHeight, this.width, this.height, this.previous);
                if (!prepare) this.previous = current;
                if (!prepare && !dirty) return false;

                const [left, bottom, right, top] = prepare ? [0, 0, 1, 1] : dirty;
                this.rectangle = [left, bottom, right - left, top - bottom];
                gl.bindFramebuffer(gl.FRAMEBUFFER, this.bitmap.framebuffer);
                gl.viewport(0, 0, this.width, this.height);
                gl.enable(gl.SCISSOR_TEST);
                gl.scissor(...this.rectangle);
                gl.bindTexture(gl.TEXTURE_2D, this.source);
                gl.useProgram(this.bitmapProgram);
                gl.drawArrays(gl.TRIANGLES, 0, 3);

                if (prepare) {
                    gl.readPixels(...this.rectangle, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array(4));
                    return false;
                }
                this.length = (right - left) * (top - bottom) * 4;
                if (pixels && this.length <= 2 * 1024 * 1024) {
                    gl.readPixels(...this.rectangle, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
                    return true;
                }
                gl.bindBuffer(gl.PIXEL_PACK_BUFFER, this.pack);
                if (!this.capacity || this.capacity < this.length) {
                    this.capacity = this.length;
                    gl.bufferData(gl.PIXEL_PACK_BUFFER, this.capacity, gl.STREAM_READ);
                }
                gl.readPixels(...this.rectangle, gl.RGBA, gl.UNSIGNED_BYTE, 0);
                this.fence = gl.fenceSync(gl.SYNC_GPU_COMMANDS_COMPLETE, 0);
                if (!this.fence) throw new Error('The overlay frame fence could not be created.');
                gl.flush();
                return false;
            } finally {
                gl.disable(gl.SCISSOR_TEST);
                gl.bindBuffer(gl.PIXEL_PACK_BUFFER, null);
                gl.bindFramebuffer(gl.FRAMEBUFFER, null);
                gl.bindVertexArray(null);
                gl.bindTexture(gl.TEXTURE_2D, null);
            }
        }

        read(pixels) {
            const gl = this.gl;
            const result = gl.clientWaitSync(this.fence, 0, 0);
            if (result === gl.TIMEOUT_EXPIRED) return false;
            if (result === gl.WAIT_FAILED) throw new Error('The overlay frame transfer failed.');
            gl.bindBuffer(gl.PIXEL_PACK_BUFFER, this.pack);
            try {
                gl.getBufferSubData(gl.PIXEL_PACK_BUFFER, 0, pixels, 0, this.length);
            } finally {
                gl.bindBuffer(gl.PIXEL_PACK_BUFFER, null);
                gl.deleteSync(this.fence);
                this.fence = null;
            }
            return true;
        }

        dispose() {
            const gl = this.gl;
            if (this.fence) gl.deleteSync(this.fence);
            if (this.pack) gl.deleteBuffer(this.pack);
            if (this.vertexArray) gl.deleteVertexArray(this.vertexArray);
            if (this.bitmapProgram) gl.deleteProgram(this.bitmapProgram);
            if (this.maskProgram) gl.deleteProgram(this.maskProgram);
            for (const framebuffer of this.framebuffers) gl.deleteFramebuffer(framebuffer);
            for (const texture of this.textures) gl.deleteTexture(texture);
        }
    }

    function attach(fx, send, generation) {
        const webview = window.chrome?.webview;
        if (!webview?.releaseBuffer || !fx._onFrame) return;
        const draw = fx._onFrame;
        const body = document.body;
        const visibility = body.style.visibility;
        let verifiedContext, supported, lease, transfer, disabled, requestTime, submitTime, fenceTime;
        let transferTimer, waitingForTransfer;

        function reset(notify) {
            if (notify && lease) send('frame-release', { id: lease.id });
            if (lease) webview.releaseBuffer(lease.pixels.buffer);
            transfer?.dispose();
            body.style.visibility = visibility;
            lease = transfer = null;
            requestTime = submitTime = fenceTime = 0;
            clearTimeout(transferTimer);
            transferTimer = 0;
            waitingForTransfer = false;
        }

        function supportsNativeFrame(gl) {
            const config = fx.config;
            if (!gl || gl.isContextLost() || config.effectBackend !== 'webgl2' ||
                config.outputCompositing !== 'browser-overlay' || config.hostCompositing !== 'source-over' ||
                config.hostCompositingSurface !== 'transparent-window' ||
                config.isolatedCompositing || config.lightBackgroundContrastAlpha) return false;

            if (verifiedContext !== gl) {
                verifiedContext = gl;
                const extension = gl.getExtension('WEBGL_debug_renderer_info');
                const renderer = extension && gl.getParameter(extension.UNMASKED_RENDERER_WEBGL);
                supported = /ANGLE \(Intel, Intel\(R\) (?:UHD |HD |Iris[^,]* |)Graphics .*Direct3D11/.test(renderer);
            }
            return supported;
        }

        webview.addEventListener('sharedbufferreceived', event => {
            const data = event.additionalData;
            if (data?.type !== 'baspark-frame-buffer') return;
            const buffer = event.getBuffer();
            if (disabled || data.generation !== generation || !requestTime || lease) {
                webview.releaseBuffer(buffer);
                return;
            }
            lease = { ...data, pixels: new Uint8Array(buffer) };
            requestTime = 0;
            if (fx.webglEffectVisible) body.style.visibility = 'hidden';
            fx._requestRender();
        });
        webview.addEventListener('message', event => {
            const message = event.data;
            if (message?.generation !== generation || (lease && message.id !== lease.id)) return;
            if (message.type === 'baspark-frame-presented') {
                submitTime = 0;
                if (transfer?.fence) finishTransfer();
            } else if (message.type === 'baspark-frame-reset' || message.type === 'baspark-frame-disabled') {
                disabled = message.type === 'baspark-frame-disabled';
                reset(false);
                fx._requestRender();
            }
        });

        function submit() {
            submitTime = performance.now();
            send('frame-submit', { id: lease.id, rect: transfer.rectangle });
        }

        function fail(error) {
            console.warn('[BASpark FX] Native frame presentation:', error);
            disabled = true;
            reset(true);
            fx._requestRender();
        }

        function finishTransfer() {
            clearTimeout(transferTimer);
            transferTimer = 0;
            if (!lease || !transfer?.fence || fx.paused || fx.destroyed) return;

            try {
                if (performance.now() - fenceTime > 500) throw new Error('The overlay frame transfer timed out.');
                if (submitTime || !transfer.read(lease.pixels)) {
                    transferTimer = setTimeout(finishTransfer, submitTime ? 16 : 3);
                    return;
                }
                fenceTime = 0;
                submit();
                if (waitingForTransfer) {
                    waitingForTransfer = false;
                    fx._requestRender();
                }
            } catch (error) {
                fail(error);
            }
        }

        function frame(time) {
            fx.animationFrame = null;
            if (fx.paused || fx.destroyed) return draw(time);

            try {
                const gl = fx.webglEffectRenderer?.gl;
                const now = performance.now();
                if (disabled || !supportsNativeFrame(gl) || (transfer && gl !== transfer.gl)) {
                    if (lease || requestTime) reset(true);
                    return draw(time);
                }
                if ([requestTime, submitTime, fenceTime].some(start => start && now - start > 500)) {
                    throw new Error('The overlay frame presenter did not respond.');
                }
                if (transfer?.fence) {
                    waitingForTransfer = true;
                    if (!transferTimer) finishTransfer();
                    return;
                }
                if (!lease && !requestTime) {
                    requestTime = now;
                    send('frame-request');
                }
                const needsPixels = fx._hasVisibleEffects() || transfer?.previous;
                draw(time);
                if (!lease) return;
                if (!fx.webglEffectVisible || gl !== fx.webglEffectRenderer?.gl || gl.isContextLost()) {
                    reset(true);
                    return;
                }

                if (!transfer || transfer.sourceWidth !== gl.drawingBufferWidth || transfer.sourceHeight !== gl.drawingBufferHeight) {
                    const previous = transfer?.previous;
                    transfer?.dispose();
                    transfer = new FrameTransfer(gl, lease.width, lease.height);
                    transfer.previous = previous;
                    if (!needsPixels) transfer.queue(null, true);
                }
                if (!needsPixels) return;
                body.style.visibility = 'hidden';
                if (transfer.queue(submitTime ? null : lease.pixels)) submit();
                if (transfer.fence) {
                    fenceTime = now;
                    finishTransfer();
                }
            } catch (error) {
                fail(error);
            }
        }

        fx._onFrame = frame;
        window.addEventListener('beforeunload', () => reset(false), { once: true });
    }

    if (typeof module !== 'undefined') module.exports = { getDamage };
    else window.attachLayeredFramePresenter = attach;
})();
