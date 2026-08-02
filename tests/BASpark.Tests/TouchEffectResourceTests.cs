using System.Diagnostics;
using System.Resources;
using System.Security.Cryptography;
using System.Text;

namespace BASpark.Tests;

public class TouchEffectResourceTests
{
    [Theory]
    [InlineData("web/assets/fx_tex_circle_01.png", "96B61811C46B4576D0C08544BD8454A9776167B87FB231FACD4FE086AB506712")]
    [InlineData("web/assets/fx_tex_grad_ring3.png", "F4210B5ADB76F9D950D091FC21E585B01BA0E96B8FA3096DAD6338D1C3CEC12A")]
    [InlineData("web/assets/fx_tex_trail_03.png", "17D80FF5AC522E882E916047D9F250EDF9A5C684C1AC994C0EF1333EF331DB07")]
    [InlineData("web/assets/fx_tex_triangle_02_1.png", "86869978232433CBEFB2387F9FF93E7529D5DEDEDC00A60DF812792357D5870F")]
    public void EmbeddedTouchTexture_MatchesExtractedGameResource(string resourceName, string expectedSha256)
    {
        byte[] content = ReadWpfResource(resourceName);
        string actualSha256 = Convert.ToHexString(SHA256.HashData(content));

        Assert.Equal(expectedSha256, actualSha256);
    }

    [Fact]
    public void EmbeddedRenderer_PreservesHostApiAndIndependentTrailScale()
    {
        string html = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));

        Assert.Contains("getContext(\"webgl2\"", html, StringComparison.Ordinal);
        Assert.Contains("window.externalBoom", html, StringComparison.Ordinal);
        Assert.Contains("window.externalMove", html, StringComparison.Ordinal);
        Assert.Contains("window.externalTrailStart", html, StringComparison.Ordinal);
        Assert.Contains("window.externalUp", html, StringComparison.Ordinal);
        Assert.Contains("window.updateColor", html, StringComparison.Ordinal);
        Assert.Contains("window.updateEffectSettings", html, StringComparison.Ordinal);
        Assert.Contains("window.updateTrailRefreshRate", html, StringComparison.Ordinal);
        Assert.Contains("window.setCurveDraw", html, StringComparison.Ordinal);
        Assert.Contains("window.setRenderingPaused", html, StringComparison.Ordinal);
        Assert.Contains("const EFFECT_RENDER_SCALE = 0.5", html, StringComparison.Ordinal);
        Assert.Contains("const TRAIL_RENDER_WIDTH_SCALE = 0.5", html, StringComparison.Ordinal);
        Assert.Contains(
            "(particle.shapeScale + travelled) * effectiveScale",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "this.engine.trailThickness * TRAIL_RENDER_WIDTH_SCALE * 0.5",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TRAIL_WIDTH_WORLD * this.engine.worldToPx * this.engine.scale",
            html,
            StringComparison.Ordinal);
        Assert.Contains("this.trailTip = { x, y, born: time, strokeId: this.strokeId }", html, StringComparison.Ordinal);
        Assert.Contains("const count = Math.floor(distance / spacing)", html, StringComparison.Ordinal);
        Assert.Contains("Math.log2(Math.max(levelWidth, levelHeight)) + BLOOM_DIFFUSION - 10", html, StringComparison.Ordinal);
        Assert.Contains("const BLOOM_RESOURCE_INTENSITY = 1.7000000476837158", html, StringComparison.Ordinal);
        Assert.Contains("const BLOOM_BASE_STRENGTH = 0.05", html, StringComparison.Ordinal);
        Assert.Contains("const BLOOM_RADIUS_CALIBRATION = 0.25", html, StringComparison.Ordinal);
        Assert.Contains(
            "const radiusScale = BLOOM_RADIUS_CALIBRATION * Math.sqrt(glowControl)",
            html,
            StringComparison.Ordinal);
        Assert.Contains("this.bloomLogs + Math.log2(radiusScale)", html, StringComparison.Ordinal);
        Assert.Contains("renderBloom(sourceTarget, levelCount, sampleScale)", html, StringComparison.Ordinal);
        Assert.Contains(
            "BLOOM_BASE_STRENGTH * glowControl",
            html,
            StringComparison.Ordinal);
        Assert.Contains("vec3 hdr = max(source.rgb + bloom, vec3(0.0))", html, StringComparison.Ordinal);
        Assert.Contains("vec3 srgb = linearToSrgb(hdr)", html, StringComparison.Ordinal);
        Assert.Contains("vec3 sampleBox4", html, StringComparison.Ordinal);
        Assert.Contains("outColor = vec4(color * contribution, 0.0)", html, StringComparison.Ordinal);
        Assert.Contains("vec3 sampleTent4", html, StringComparison.Ordinal);
        Assert.Contains("sampleTent4(uLow, vUv, uLowTexel), 0.0)", html, StringComparison.Ordinal);
        Assert.Contains("vec3 bloom = sampleBloom(vUv) * uBloomStrength", html, StringComparison.Ordinal);
        Assert.Contains(
            "float effectCoverage = clamp(max(max(srgb.r, srgb.g), srgb.b), 0.0, 1.0)",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "float effectAlpha = 1.0 - (1.0 - sourceCoverage) * (1.0 - effectCoverage)",
            html,
            StringComparison.Ordinal);
        Assert.Contains("float sourceCoverage = clamp(source.a, 0.0, 1.0)", html, StringComparison.Ordinal);
        Assert.Contains(
            "float discCoverage = texture(uDiscMask, vUv).r",
            html,
            StringComparison.Ordinal);
        Assert.Contains("float discFootprint = texture(uDiscFootprint, vUv).r", html, StringComparison.Ordinal);
        Assert.Contains("float nonDiscAlpha = texture(uNonDiscAlpha, vUv).r", html, StringComparison.Ordinal);
        Assert.Contains(
            "float discCompositeAlpha = 1.0 - (1.0 - discCoverage) * (1.0 - nonDiscAlpha)",
            html,
            StringComparison.Ordinal);
        Assert.Contains("float discMask = step(0.0001, discFootprint)", html, StringComparison.Ordinal);
        Assert.Contains("float alpha = mix(effectAlpha, discCompositeAlpha, discMask)", html, StringComparison.Ordinal);
        Assert.Contains(
            "outColor = vec4(min(srgb, vec3(alpha)), alpha)",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BLOOM_RGBA8_FALLBACK_THRESHOLD", html, StringComparison.Ordinal);
        Assert.DoesNotContain("vec4 bloomSample = sampleBloom(vUv)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("bloomOpacity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("clamp(sampled.a", html, StringComparison.Ordinal);
        Assert.DoesNotContain("vec3 mapped =", html, StringComparison.Ordinal);
        Assert.DoesNotContain("vec3 mapped = hdr / (1.0 + peak)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("vec3 unassociated = hdr /", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mapThemeRgb", html, StringComparison.Ordinal);
        Assert.DoesNotContain("gl.generateMipmap(gl.TEXTURE_2D)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("gl.LINEAR_MIPMAP_LINEAR", html, StringComparison.Ordinal);
        Assert.Contains("gl.TEXTURE_MIN_FILTER, gl.LINEAR", html, StringComparison.Ordinal);
        Assert.Contains("const TRAIL_SMOOTHING_PASSES = 3", html, StringComparison.Ordinal);
        Assert.Contains("const TRAIL_RENDER_SEGMENT_PX = 0.5", html, StringComparison.Ordinal);
        Assert.Contains("const TRAIL_JITTER_TOLERANCE_RATIO = 0.25", html, StringComparison.Ordinal);
        Assert.Contains("const TRAIL_MSAA_SAMPLES = 4", html, StringComparison.Ordinal);
        Assert.Contains("function smoothTrailPath(source, renderSegmentPx, createPoint = null)", html, StringComparison.Ordinal);
        Assert.Contains("function curveTrailPath(source, renderSegmentPx, createPoint = null)", html, StringComparison.Ordinal);
        Assert.Contains("function simplifyTrailPath(source, tolerancePx)", html, StringComparison.Ordinal);
        Assert.Contains("pass < TRAIL_SMOOTHING_PASSES", html, StringComparison.Ordinal);
        Assert.Contains("refined.push(makePoint(", html, StringComparison.Ordinal);
        Assert.Contains("lerp(a.x, b.x, 0.25)", html, StringComparison.Ordinal);
        Assert.Contains("lerp(a.x, b.x, 0.75)", html, StringComparison.Ordinal);
        Assert.Contains(
            "Math.ceil(distance / renderSegmentPx)",
            html,
            StringComparison.Ordinal);
        Assert.Contains("TRAIL_RENDER_SEGMENT_PX / Math.max(1, this.dpr)", html, StringComparison.Ordinal);
        Assert.Contains("const stabilizedPoints = simplifyTrailPath(sourcePoints, jitterTolerancePx)", html, StringComparison.Ordinal);
        Assert.Contains("createMultisampleTarget(width, height)", html, StringComparison.Ordinal);
        Assert.Contains("gl.renderbufferStorageMultisample", html, StringComparison.Ordinal);
        Assert.Contains("this.resolveSourceMultisample(target);", html, StringComparison.Ordinal);
        Assert.Contains("gl.blitFramebuffer", html, StringComparison.Ordinal);
        Assert.Contains("this.nonDiscTarget = this.createTarget(pixelWidth, pixelHeight)", html, StringComparison.Ordinal);
        Assert.Contains("this.discMaskTarget = this.createCoverageTarget(pixelWidth, pixelHeight)", html, StringComparison.Ordinal);
        Assert.Contains("this.discFootprintTarget = this.createCoverageTarget(pixelWidth, pixelHeight)", html, StringComparison.Ordinal);
        Assert.Contains("this.nonDiscAlphaTarget = this.createCoverageTarget(pixelWidth, pixelHeight)", html, StringComparison.Ordinal);
        Assert.Contains("this.renderSource(now, this.nonDiscTarget, false)", html, StringComparison.Ordinal);
        Assert.Contains("this.renderBloom(this.nonDiscTarget, bloomParameters.levels, bloomParameters.sampleScale)", html, StringComparison.Ordinal);
        Assert.Contains("this.renderNonDiscAlpha(nonDiscBloom, bloomParameters.sampleScale, glowControl)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("uNonDiscMask", html, StringComparison.Ordinal);
        Assert.DoesNotContain("renderNonDiscMasks", html, StringComparison.Ordinal);
        Assert.Contains("window.truncateTrail", html, StringComparison.Ordinal);
        Assert.Contains("engine.truncateTrail();", html, StringComparison.Ordinal);

        int pauseHandler = html.IndexOf("window.setRenderingPaused = paused =>", StringComparison.Ordinal);
        int pauseClear = html.IndexOf("window.clearEffects();", pauseHandler, StringComparison.Ordinal);
        int resumeBranch = html.IndexOf("if (!window.renderingPaused)", pauseHandler, StringComparison.Ordinal);
        int resumeSchedule = html.IndexOf("window.scheduleNextAnimationFrame();", resumeBranch, StringComparison.Ordinal);
        Assert.True(pauseHandler >= 0, "Rendering pause handler is missing.");
        Assert.True(pauseClear > pauseHandler, "Rendering pause handler must clear active effects.");
        Assert.True(
            resumeBranch > pauseClear,
            "Effect clearing must run before the resume-only scheduling branch.");
        Assert.True(resumeSchedule > resumeBranch, "Rendering resume must schedule a fresh frame.");
        Assert.Contains("engine.clear();", html, StringComparison.Ordinal);
        Assert.Contains("renderer?.clear();", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(html, "<!-- BASPARK_ASSET_BOOTSTRAP -->"));
    }

    [Fact]
    public void TrailRetractionDelayAndSpeed_UseIndependentGameMatchedDefaults()
    {
        const double originalGameSimulationSpeed = 1.0;
        Assert.Equal(originalGameSimulationSpeed, ConfigManager.DefaultTrailDelayMultiplier);

        string html = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));
        Assert.Contains("const TRAIL_LIFETIME_MS = 300.00001192092896", html, StringComparison.Ordinal);
        Assert.Contains("const fallbackAssetBase = \"Assets/\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("../../apk/", html, StringComparison.Ordinal);
        Assert.Contains("this.trailRetractionDelayMultiplier = 1", html, StringComparison.Ordinal);
        Assert.Contains("return now - this.trailRetentionMs();", html, StringComparison.Ordinal);
        Assert.Contains("cutoffAtRelease: time - retentionMs", html, StringComparison.Ordinal);
        Assert.Contains(
            "Math.max(0, now - retraction.releasedAt) * this.trailSpeed",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("lifetime / this.trailSpeed", html, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayCoordinates_KeepSubpixelTrailPrecision()
    {
        string sourcePath = Path.Combine(FindWorkspaceRoot(), "src", "MainWindow.xaml.cs");
        string source = File.ReadAllText(sourcePath, Encoding.UTF8);
        int methodStart = source.IndexOf("private static string FormatCoordinate(double value)", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("private bool TryConvertScreenToOverlayPoint", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0, "Coordinate formatter is missing.");
        Assert.True(methodEnd > methodStart, "Coordinate formatter is incomplete.");
        string method = source[methodStart..methodEnd];
        Assert.Contains("value.ToString(\"F6\", CultureInfo.InvariantCulture)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("\"F3\"", method, StringComparison.Ordinal);
    }

    [Fact]
    public void InputBridgeAndTopmostMonitor_AvoidPerSampleScriptCompilation()
    {
        string root = FindWorkspaceRoot();
        string mainWindowSource = File.ReadAllText(
            Path.Combine(root, "src", "MainWindow.xaml.cs"),
            Encoding.UTF8);
        string html = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));

        Assert.Contains("coreWebView.Settings.IsWebMessageEnabled = true;", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("coreWebView.PostWebMessageAsJson(message);", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("PostInputMessage(\"move\", inputMode, clientPoint);", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("PostHostInputState(", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("AdvanceInputGeneration()", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("\\\"kind\\\":\\\"hostState\\\"", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteWithInputContext", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("private sealed class InputBoundsSnapshot", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("System.Threading.Volatile.Read(ref _inputBounds)", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("CacheInputBounds(bounds);", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _topmostRefreshQueued, 1)", mainWindowSource, StringComparison.Ordinal);

        Assert.Contains("function installHostInputBridge()", html, StringComparison.Ordinal);
        Assert.Contains("webView.addEventListener(\"message\", event =>", html, StringComparison.Ordinal);
        Assert.Contains("let hostInputGeneration = 0;", html, StringComparison.Ordinal);
        Assert.Contains("if (kind === \"hostState\")", html, StringComparison.Ordinal);
        Assert.Contains("if (!hostInputEnabled || generation !== hostInputGeneration) return;", html, StringComparison.Ordinal);
        Assert.Contains("window.externalMove(x, y);", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Renderer_UsesNoDiscFastPathWithoutReducingActiveDiscCoverage()
    {
        string html = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));
        int renderStart = html.IndexOf("render(now)", StringComparison.Ordinal);
        int renderEnd = html.IndexOf("clear()", renderStart, StringComparison.Ordinal);

        Assert.True(renderStart >= 0 && renderEnd > renderStart, "Renderer entry point is missing.");
        string render = html[renderStart..renderEnd];
        Assert.Contains("this.engine.prune(now);", render, StringComparison.Ordinal);
        Assert.Contains("if (this.viewportNeedsResize()) this.resize();", render, StringComparison.Ordinal);
        Assert.Contains("const hasActiveDisc = this.hasActiveDisc(now);", render, StringComparison.Ordinal);
        Assert.Contains("if (hasActiveDisc) {\n                        this.renderDiscMasks(now);", render, StringComparison.Ordinal);
        Assert.Contains("this.renderFinal(bloom, bloomParameters.sampleScale, glowControl, hasActiveDisc);", render, StringComparison.Ordinal);
        Assert.Contains("viewportNeedsResize()", html, StringComparison.Ordinal);
        Assert.Contains("createUniformCache(program, names)", html, StringComparison.Ordinal);
        Assert.Contains("this.geometryUploadScratch = new Float32Array(0);", html, StringComparison.Ordinal);

        int livenessStart = html.IndexOf("hasActiveEffects()", StringComparison.Ordinal);
        int livenessEnd = html.IndexOf("trailStrokes(now)", livenessStart, StringComparison.Ordinal);
        Assert.True(livenessStart >= 0 && livenessEnd > livenessStart, "Effect liveness check is missing.");
        string liveness = html[livenessStart..livenessEnd];
        Assert.DoesNotContain("this.prune", liveness, StringComparison.Ordinal);
    }

    [Fact]
    public void ClickDisc_UsesExtractedLinearGradientFadeTiming()
    {
        string html = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));

        Assert.Contains("{ t: 7903 / 65535, c: [0.24056601524353027, 0.39061814546585083, 1] }", html, StringComparison.Ordinal);
        Assert.Contains("lifetimeMs: 200.00000298023224", html, StringComparison.Ordinal);
        Assert.Contains("{ t: 7132 / 65535, v: 1 }", html, StringComparison.Ordinal);
        Assert.Contains("function evalGradientAlpha(stops, t)", html, StringComparison.Ordinal);
        Assert.Contains("const alpha = evalGradientAlpha(PROFILE.disc.alpha, p);", html, StringComparison.Ordinal);
        Assert.Contains("if (p < 0 || p >= 1) return null", html, StringComparison.Ordinal);
        Assert.Contains("this.discMaskTarget = this.createCoverageTarget(pixelWidth, pixelHeight)", html, StringComparison.Ordinal);
        Assert.Contains("this.discFootprintTarget = this.createCoverageTarget(pixelWidth, pixelHeight)", html, StringComparison.Ordinal);
        Assert.Contains("this.nonDiscAlphaTarget = this.createCoverageTarget(pixelWidth, pixelHeight)", html, StringComparison.Ordinal);
        Assert.Contains("for (const click of this.engine.clicks) this.renderDiscMask(click, now)", html, StringComparison.Ordinal);
        Assert.Contains("for (const click of this.engine.clicks) this.renderDiscFootprint(click, now)", html, StringComparison.Ordinal);
        Assert.Contains("const FINAL_NO_DISC_FRAGMENT", html, StringComparison.Ordinal);
        Assert.Contains("this.finalNoDiscProgram = createProgram", html, StringComparison.Ordinal);
        Assert.Contains("this.bindFullscreenTexture(uniforms, \"uDiscMask\", 2, this.discMaskTarget.texture)", html, StringComparison.Ordinal);
        Assert.Contains("this.bindFullscreenTexture(uniforms, \"uDiscFootprint\", 3, this.discFootprintTarget.texture)", html, StringComparison.Ordinal);
        Assert.Contains("this.bindFullscreenTexture(uniforms, \"uNonDiscAlpha\", 4, this.nonDiscAlphaTarget.texture)", html, StringComparison.Ordinal);
        Assert.Contains("const formats = this.floatTargets", html, StringComparison.Ordinal);
        Assert.Contains("{ internalFormat: gl.R16F, type: gl.HALF_FLOAT }", html, StringComparison.Ordinal);

        int maskRenderStart = html.IndexOf("renderDiscMask(click, now)", StringComparison.Ordinal);
        int maskRenderEnd = html.IndexOf("renderDiscFootprint(click, now)", maskRenderStart, StringComparison.Ordinal);
        Assert.True(maskRenderStart >= 0 && maskRenderEnd > maskRenderStart, "Disc mask renderer is missing.");
        Assert.Contains("disc.alpha,", html[maskRenderStart..maskRenderEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void ClickDisc_FadesByTheFourth120FpsSample()
    {
        const double frameTimeSeconds = 4.0 / 120.0;
        const double lifetimeSeconds = 0.20000000298023224;
        const double fadeStart = 7132.0 / 65535.0;
        double normalizedAge = frameTimeSeconds / lifetimeSeconds;
        double alpha = 1.0 - (normalizedAge - fadeStart) / (1.0 - fadeStart);
        double midpointAge = (fadeStart + 1.0) * 0.5;
        double midpointAlpha = 1.0 - (midpointAge - fadeStart) / (1.0 - fadeStart);
        double twentyThirdSampleAge = (22.0 / 120.0) / lifetimeSeconds;
        double twentyThirdSampleAlpha = 1.0 - (twentyThirdSampleAge - fadeStart) / (1.0 - fadeStart);

        Assert.InRange(alpha, 0.93, 0.94);
        Assert.Equal(1.0, 1.0 - (fadeStart - fadeStart) / (1.0 - fadeStart), 12);
        Assert.Equal(0.5, midpointAlpha, 12);
        Assert.Equal(0.0, 1.0 - (1.0 - fadeStart) / (1.0 - fadeStart), 12);
        Assert.InRange(twentyThirdSampleAlpha, 0.09, 0.10);

        string html = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));
        Assert.Contains("if (p < 0 || p >= 1) return null", html, StringComparison.Ordinal);
        Assert.Contains("float alpha = mix(effectAlpha, discCompositeAlpha, discMask)", html, StringComparison.Ordinal);
        Assert.Contains(
            "float discMask = step(0.0001, discFootprint)",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentFiltering_ReleasesInputWithoutHidingExistingEffects()
    {
        string root = FindWorkspaceRoot();
        string mainWindowSource = File.ReadAllText(
            Path.Combine(root, "src", "MainWindow.xaml.cs"),
            Encoding.UTF8);
        int methodStart = mainWindowSource.IndexOf("public void SetEnvironmentSuppressed(bool suppressed)", StringComparison.Ordinal);
        int methodEnd = mainWindowSource.IndexOf("private bool ShouldOverlayBeVisible", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0, "Environment suppression handler is missing.");
        Assert.True(methodEnd > methodStart, "Environment suppression handler is incomplete.");
        string method = mainWindowSource[methodStart..methodEnd];
        Assert.Contains("PostHostInputState(", method, StringComparison.Ordinal);
        Assert.Contains("inputEnabled: false", method, StringComparison.Ordinal);
        Assert.Contains("truncateTrail: true", method, StringComparison.Ordinal);
        Assert.Contains("AdvanceInputGeneration()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("PauseOverlayRuntime", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncOverlayPresentationState", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Hide()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("clearEffects", method, StringComparison.Ordinal);
        Assert.DoesNotContain("_hiddenByEnvironmentSuppression", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains(
            "private bool ShouldOverlayBeVisible =>\n            !_hiddenForExternalScreenshotCapture;",
            mainWindowSource,
            StringComparison.Ordinal);

        string html = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));
        int truncateStart = html.IndexOf("truncateTrail(time = this.now())", StringComparison.Ordinal);
        int truncateEnd = html.IndexOf("pointerDown(x, y, time = this.now())", truncateStart, StringComparison.Ordinal);
        Assert.True(truncateStart >= 0 && truncateEnd > truncateStart, "Trail truncation implementation is missing.");
        string truncateTrail = html[truncateStart..truncateEnd];
        Assert.Contains("this.endTrailStroke(time);", truncateTrail, StringComparison.Ordinal);
        Assert.DoesNotContain("this.clicks", truncateTrail, StringComparison.Ordinal);
        Assert.DoesNotContain("this.distanceParticles", truncateTrail, StringComparison.Ordinal);
        Assert.Contains("if (Boolean(message.truncateTrail))", html, StringComparison.Ordinal);
        Assert.Contains("window.truncateTrail();", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("previewScale", "min=\"0.50\" max=\"3.00\" step=\"0.10\" value=\"1.00\"")]
    [InlineData("previewThickness", "min=\"0.50\" max=\"3.00\" step=\"0.10\" value=\"1.00\"")]
    [InlineData("previewGlow", "min=\"0.00\" max=\"3.00\" step=\"0.10\" value=\"1.00\"")]
    [InlineData("previewOpacity", "min=\"0.10\" max=\"1.00\" step=\"0.10\" value=\"1.00\"")]
    [InlineData("previewSpeed", "min=\"0.20\" max=\"3.00\" step=\"0.10\" value=\"1.00\"")]
    [InlineData("previewTrailSpeed", "min=\"0.20\" max=\"3.00\" step=\"0.10\" value=\"1.00\"")]
    [InlineData("previewClickSpeed", "min=\"0.20\" max=\"3.00\" step=\"0.10\" value=\"1.00\"")]
    [InlineData("previewLifetime", "min=\"0.00\" max=\"2.00\" step=\"0.10\" value=\"1.00\"")]
    [InlineData("previewRefresh", "min=\"30\" max=\"360\" step=\"10\" value=\"60\"")]
    public void Preview_ExposesProductionVisualSettingRange(string id, string expectedAttributes)
    {
        string html = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));

        Assert.Contains($"id=\"{id}\" type=\"range\" {expectedAttributes}", html, StringComparison.Ordinal);
        Assert.Contains("id=\"previewColor\" type=\"color\" value=\"#5fc5ff\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"previewBackgroundColor\" type=\"color\" value=\"#000000\"", html, StringComparison.Ordinal);
        Assert.Contains("background: var(--preview-background, #000000)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("previewBackground\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("background-image:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("runDemo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval(", html, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout(", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CurveTrail_IsOptionalAndDisabledByDefault()
    {
        string html = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));
        int renderTrailStart = html.IndexOf("renderTrail(now) {", StringComparison.Ordinal);
        int renderTrailEnd = html.IndexOf("renderSource(now, target", renderTrailStart, StringComparison.Ordinal);

        Assert.Contains("window.ApplyCurveDraw = false", html, StringComparison.Ordinal);
        Assert.Contains("this.applyCurveDraw = false", html, StringComparison.Ordinal);
        Assert.True(renderTrailStart >= 0 && renderTrailEnd > renderTrailStart, "Trail renderer is missing.");
        string renderTrail = html[renderTrailStart..renderTrailEnd].Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "const points = this.engine.applyCurveDraw\n" +
            "                            ? curveTrailPath(stabilizedPoints, renderSegmentPx, this.trailPointFactory)\n" +
            "                            : smoothTrailPath(stabilizedPoints, renderSegmentPx, this.trailPointFactory);",
            renderTrail,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(renderTrail, "curveTrailPath("));
        Assert.Equal(1, CountOccurrences(renderTrail, "smoothTrailPath("));
        Assert.Contains("id=\"previewCurveDraw\"", html, StringComparison.Ordinal);
        Assert.Contains("controls.curveDraw.checked = false", html, StringComparison.Ordinal);
        Assert.Contains("window.setCurveDraw(controls.curveDraw.checked)", html, StringComparison.Ordinal);

        string root = FindWorkspaceRoot();
        string configSource = File.ReadAllText(Path.Combine(root, "src", "ConfigManager.cs"), Encoding.UTF8);
        string mainWindowSource = File.ReadAllText(Path.Combine(root, "src", "MainWindow.xaml.cs"), Encoding.UTF8);
        string controlPanelSource = File.ReadAllText(Path.Combine(root, "src", "ControlPanelWindow.xaml.cs"), Encoding.UTF8);
        Assert.Contains("public static bool ApplyCurveDraw { get; set; } = false;", configSource, StringComparison.Ordinal);
        Assert.Contains("ApplyCurveDraw = ReadBool(key, \"ApplyCurveDraw\", false);", configSource, StringComparison.Ordinal);
        Assert.Contains("CurveDraw = 1 << 10", configSource, StringComparison.Ordinal);
        Assert.Contains("Save(\"ApplyCurveDraw\", false);", configSource, StringComparison.Ordinal);
        Assert.Contains("SetCurveDraw(ConfigManager.ApplyCurveDraw);", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("settingsSaved &= ConfigManager.Save(\"ApplyCurveDraw\", curveDrawEnabled);", controlPanelSource, StringComparison.Ordinal);
        Assert.Contains("App.Overlay?.SetCurveDraw(curveDrawEnabled);", controlPanelSource, StringComparison.Ordinal);
        Assert.Contains("VisualAppearanceResetFlags.CurveDraw", controlPanelSource, StringComparison.Ordinal);
        Assert.Contains("App.Overlay?.SetCurveDraw(ConfigManager.ApplyCurveDraw);", controlPanelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void StandalonePreviewBootstrap_PreservesRendererContract()
    {
        string embeddedRenderer = Encoding.UTF8.GetString(ReadWpfResource("web/index.html"));
        const string bootstrapPrefix =
            "<script>window.__BASPARK_STANDALONE_PREVIEW__=true;window.__BASPARK_ASSETS=";
        const string bootstrapSuffix = ";</script>";
        var previewAssets = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["circle"] = "data:image/png;base64," + Convert.ToBase64String(ReadWpfResource("web/assets/fx_tex_circle_01.png")),
            ["ring"] = "data:image/png;base64," + Convert.ToBase64String(ReadWpfResource("web/assets/fx_tex_grad_ring3.png")),
            ["trail"] = "data:image/png;base64," + Convert.ToBase64String(ReadWpfResource("web/assets/fx_tex_trail_03.png")),
            ["triangle"] = "data:image/png;base64," + Convert.ToBase64String(ReadWpfResource("web/assets/fx_tex_triangle_02_1.png"))
        };
        string bootstrap = bootstrapPrefix +
            System.Text.Json.JsonSerializer.Serialize(previewAssets) +
            bootstrapSuffix;
        string html = embeddedRenderer.Replace("<!-- BASPARK_ASSET_BOOTSTRAP -->", bootstrap, StringComparison.Ordinal);
        int bootstrapStart = html.IndexOf(bootstrapPrefix, StringComparison.Ordinal);
        Assert.True(bootstrapStart >= 0, "Standalone preview bootstrap is missing.");
        int jsonStart = bootstrapStart + bootstrapPrefix.Length;
        int bootstrapEnd = html.IndexOf(bootstrapSuffix, jsonStart, StringComparison.Ordinal);
        Assert.True(bootstrapEnd >= jsonStart, "Standalone preview bootstrap is incomplete.");
        string json = html[jsonStart..bootstrapEnd];

        using var assets = System.Text.Json.JsonDocument.Parse(json);
        var expectedResources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["circle"] = "web/assets/fx_tex_circle_01.png",
            ["ring"] = "web/assets/fx_tex_grad_ring3.png",
            ["trail"] = "web/assets/fx_tex_trail_03.png",
            ["triangle"] = "web/assets/fx_tex_triangle_02_1.png"
        };
        Assert.Equal(expectedResources.Count, assets.RootElement.EnumerateObject().Count());
        foreach ((string key, string resourceName) in expectedResources)
        {
            string dataUrl = assets.RootElement.GetProperty(key).GetString()!;
            const string dataUrlPrefix = "data:image/png;base64,";
            Assert.StartsWith(dataUrlPrefix, dataUrl, StringComparison.Ordinal);
            Assert.Equal(ReadWpfResource(resourceName), Convert.FromBase64String(dataUrl[dataUrlPrefix.Length..]));
        }

        int bootstrapLength = bootstrapEnd + bootstrapSuffix.Length - bootstrapStart;
        string normalized = html.Remove(bootstrapStart, bootstrapLength)
            .Insert(bootstrapStart, "<!-- BASPARK_ASSET_BOOTSTRAP -->");
        Assert.Contains("window.__BASPARK_STANDALONE_PREVIEW__=true", html, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(html, "data:image/png;base64,"));
        Assert.Equal(embeddedRenderer, normalized);
        Assert.DoesNotContain("<!-- BASPARK_ASSET_BOOTSTRAP -->", html, StringComparison.Ordinal);
        Assert.DoesNotContain("previewScene", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<select", html, StringComparison.Ordinal);
        Assert.DoesNotContain("linear-gradient(", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" href=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runPreviewDemo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval(", html, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout(", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandalonePreviewGenerator_ProducesSelfContainedIdlePreview()
    {
        string root = FindWorkspaceRoot();
        string sourcePath = Path.Combine(root, "src", "Web", "index.html");
        string scriptPath = Path.Combine(root, "scripts", "Generate-StandalonePreview.ps1");
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "BASpark.Tests",
            Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(temporaryDirectory, "preview.html");

        try
        {
            string powershellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            Assert.True(File.Exists(powershellPath), $"Windows PowerShell was not found at '{powershellPath}'.");

            var startInfo = new ProcessStartInfo
            {
                FileName = powershellPath,
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-SourcePath");
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add("-OutputPath");
            startInfo.ArgumentList.Add(outputPath);

            using Process process = Assert.IsType<Process>(Process.Start(startInfo));
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            bool exited = false;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                    exited = true;
                }
                catch (OperationCanceledException)
                {
                    exited = process.HasExited;
                    if (!exited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                }
            }

            string output = await standardOutput;
            string error = await standardError;
            Assert.True(exited, $"Standalone preview generation timed out.\n{output}\n{error}");
            Assert.True(process.ExitCode == 0, $"Standalone preview generation failed.\n{output}\n{error}");
            Assert.True(File.Exists(outputPath), "Standalone preview generator did not create its requested output file.");

            string source = File.ReadAllText(sourcePath, Encoding.UTF8);
            string html = File.ReadAllText(outputPath, Encoding.UTF8);
            Assert.Contains("window.__BASPARK_STANDALONE_PREVIEW__=true", html, StringComparison.Ordinal);
            Assert.Equal(4, CountOccurrences(html, "data:image/png;base64,"));
            Assert.DoesNotContain("<!-- BASPARK_ASSET_BOOTSTRAP -->", html, StringComparison.Ordinal);
            Assert.DoesNotContain(" src=", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" href=", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("runPreviewDemo", html, StringComparison.Ordinal);
            Assert.DoesNotContain("setInterval(", html, StringComparison.Ordinal);
            Assert.DoesNotContain("setTimeout(", html, StringComparison.Ordinal);

            const string bootstrapPrefix =
                "<script>window.__BASPARK_STANDALONE_PREVIEW__=true;window.__BASPARK_ASSETS=";
            const string bootstrapSuffix = ";</script>";
            int bootstrapStart = html.IndexOf(bootstrapPrefix, StringComparison.Ordinal);
            Assert.True(bootstrapStart >= 0, "Generated asset bootstrap is missing.");
            int jsonStart = bootstrapStart + bootstrapPrefix.Length;
            int bootstrapEnd = html.IndexOf(bootstrapSuffix, jsonStart, StringComparison.Ordinal);
            Assert.True(bootstrapEnd > jsonStart, "Generated asset bootstrap is incomplete.");

            using var generatedAssets = System.Text.Json.JsonDocument.Parse(html[jsonStart..bootstrapEnd]);
            var expectedResources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["circle"] = "web/assets/fx_tex_circle_01.png",
                ["ring"] = "web/assets/fx_tex_grad_ring3.png",
                ["trail"] = "web/assets/fx_tex_trail_03.png",
                ["triangle"] = "web/assets/fx_tex_triangle_02_1.png"
            };
            Assert.Equal(expectedResources.Count, generatedAssets.RootElement.EnumerateObject().Count());
            foreach ((string key, string resourceName) in expectedResources)
            {
                string dataUrl = generatedAssets.RootElement.GetProperty(key).GetString()!;
                const string dataUrlPrefix = "data:image/png;base64,";
                Assert.StartsWith(dataUrlPrefix, dataUrl, StringComparison.Ordinal);
                Assert.Equal(ReadWpfResource(resourceName), Convert.FromBase64String(dataUrl[dataUrlPrefix.Length..]));
            }

            string normalized = html.Remove(bootstrapStart, bootstrapEnd + bootstrapSuffix.Length - bootstrapStart)
                .Insert(bootstrapStart, "<!-- BASPARK_ASSET_BOOTSTRAP -->");
            Assert.Equal(source, normalized);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static byte[] ReadWpfResource(string resourceName)
    {
        var assembly = typeof(MainWindow).Assembly;
        string generatedResourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(".g.resources", StringComparison.Ordinal));
        using Stream generatedResources = Assert.IsAssignableFrom<Stream>(
            assembly.GetManifestResourceStream(generatedResourceName));
        using var reader = new ResourceReader(generatedResources);
        var entries = reader.GetEnumerator();
        while (entries.MoveNext())
        {
            if (!string.Equals(entries.Key as string, resourceName, StringComparison.Ordinal))
            {
                continue;
            }

            using Stream content = Assert.IsAssignableFrom<Stream>(entries.Value);
            using var memory = new MemoryStream();
            content.CopyTo(memory);
            return memory.ToArray();
        }

        throw new Xunit.Sdk.XunitException($"Embedded resource '{resourceName}' was not found.");
    }

    private static int CountOccurrences(string value, string expected)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += expected.Length;
        }
        return count;
    }

    private static string FindWorkspaceRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BASpark.sln")))
            {
                return directory.FullName;
            }
        }

        throw new Xunit.Sdk.XunitException("Could not locate the BASpark workspace root.");
    }
}
