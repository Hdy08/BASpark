using System.Globalization;
using System.Text.Json;

namespace BASpark;

internal sealed record AdvancedFxOption(string Value, string TitleKey);

internal sealed record AdvancedFxSetting(string Path, string GroupKey, string SectionKey, string TitleKey,
    JsonElement DefaultValue, double Minimum, double Maximum, double Step, bool IsEffectParameter,
    IReadOnlyList<AdvancedFxOption>? Options = null)
{
    internal string Key => AdvancedFxSettings.KeyPrefix + Path;
    internal int Precision => Step >= 1 ? 0 : Math.Max(2, (int)Math.Ceiling(-Math.Log10(Step)));
}

internal static class AdvancedFxSettings
{
    internal const string KeyPrefix = "BaClickFx/";

    internal static IReadOnlyList<AdvancedFxSetting> Definitions { get; } =
    [
Choice("themeColorMode", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelThemeColorMode", "hue-only", [new("relative-oklch", "Advanced_themeColorModeRelativeOklch"), new("hue-only", "Advanced_themeColorModeHueOnly")]),
        Number("maxDpr", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelDpr", 2, 1, 3, 0.1, false),
        Choice("renderMode", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelRenderMode", "full-webgl2", [new("full-webgpu-sdr", "Advanced_renderFullWebGPUStandard"), new("full-webgpu", "Advanced_renderFullWebGPU"), new("full-webgl2", "Advanced_renderFullWebGL2"), new("webgl2-bloom", "Advanced_renderWebGL2Bloom"), new("software-bloom", "Advanced_renderSoftwareBloom"), new("native-bloom", "Advanced_renderNativeBloom")]),
        Choice("overlayAlphaPolicy", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelOverlayAlphaPolicy", "visual-max", [new("coverage", "Advanced_overlayAlphaPolicyCoverage"), new("visual-max", "Advanced_overlayAlphaPolicyVisualMax")]),
        Choice("overlayColorCompensation", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelOverlayColorCompensation", "none", [new("none", "Advanced_overlayColorCompensationNone"), new("bright-core", "Advanced_overlayColorCompensationBrightCore")]),
        Number("overlayAlphaLimit", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelOverlayAlphaLimit", 0.85, 0, 1, 0.00392156862745098, false),
        Boolean("isolatedCompositing", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelIsolatedCompositing", false),
        Number("lightBackgroundContrastAlpha", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelLightBackgroundContrastAlpha", 0, 0, 1, 0.01, false),
        Number("webgpuHdrPeak", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelWebGPUHdrPeak", 3, 2, 4, 0.01, false),
        Number("webgpuHdrBrightness", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelWebGPUHdrBrightness", 1, 0, 32, 0.1, false),
        Number("webgpuHdrColorPreservation", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelWebGPUHdrColorPreservation", 0, 0, 1, 0.01, false),
        Number("webgpuHdrWhiteCore", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelWebGPUHdrWhiteCore", 0.6, 0, 1, 0.01, false),
        Number("webgpuHdrWhiteStart", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelWebGPUHdrWhiteStart", 1, 0, 15.99, 0.01, false),
        Number("webgpuHdrWhiteEnd", "Advanced_sectionBasic", "Advanced_displaySummary", "Advanced_labelWebGPUHdrWhiteEnd", 5, 0.01, 16, 0.01, false),
        Number("rings.count", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRingCount", 2, 0, 6, 1, true),
        Number("rings.hdrIntensity", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRingHdr", 5.992157, 0, 8, 0.01, true),
        Number("rings.radiusMin", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRingRadMin", 68.92571232, 20, 120, 0.01, true),
        Number("rings.radiusMax", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRingRadMax", 80.41333104000002, 20, 120, 0.01, true),
        Number("rings.bandToOuterRadius", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRingBandRatio", 0.0598573766034603, 0.01, 0.2, 0.0001, true),
        Number("rings.widthStart", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRingWStart", 1, 0.25, 2, 0.01, true),
        Number("rings.widthEnd", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRingWEnd", 1, 0.25, 2, 0.01, true),
        Number("rings.lifetimeMs", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRingLife", 600, 50, 2000, 1, true),
        Number("rings.angularVelocityMultiplier", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelAngVelMul", 11.170107, 1, 30, 0.01, true),
        Number("rings.arcSamples", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelArcSamples", 64, 24, 192, 1, true),
        Number("rings.radialSamples", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRadialSamples", 1, 1, 16, 1, true),
        Number("rings.rotationDirection", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelRingDir", -1, -1, 1, 2, true),
        Number("rings.dissolveDirection", "Advanced_sectionClick", "Advanced_ringSummary", "Advanced_labelDissolveDir", 1, -1, 1, 2, true),
        Number("disk.radius", "Advanced_sectionClick", "Advanced_diskSummary", "Advanced_labelDiskRadius", 64.8, 20, 120, 0.01, true),
        Number("disk.lifetimeMs", "Advanced_sectionClick", "Advanced_diskSummary", "Advanced_labelDiskLife", 200, 50, 500, 1, true),
        Number("shards.hdrIntensity", "Advanced_sectionShards", "Advanced_sharedShardsSummary", "Advanced_labelShardHdr", 5.992157, 0, 8, 0.01, true),
        Number("shards.roundness", "Advanced_sectionShards", "Advanced_sharedShardsSummary", "Advanced_labelShardRoundness", 0, 0, 1, 0.01, true),
        Number("shards.sizeMin", "Advanced_sectionShards", "Advanced_sharedShardsSummary", "Advanced_labelShardSizeMin", 16.6256496, 0, 100, 0.01, true),
        Number("shards.sizeMax", "Advanced_sectionShards", "Advanced_sharedShardsSummary", "Advanced_labelShardSizeMax", 33.2512992, 0, 100, 0.01, true),
        Number("shards.clickCount", "Advanced_sectionShards", "Advanced_clickShardsSummary", "Advanced_labelClickShards", 4, 0, 12, 1, true),
        Number("shards.clickLifetimeMinMs", "Advanced_sectionShards", "Advanced_clickShardsSummary", "Advanced_labelClickShardLifeMin", 600, 100, 1000, 1, true),
        Number("shards.clickLifetimeMaxMs", "Advanced_sectionShards", "Advanced_clickShardsSummary", "Advanced_labelClickShardLifeMax", 700, 100, 1000, 1, true),
        Number("shards.clickRadius", "Advanced_sectionShards", "Advanced_clickShardsSummary", "Advanced_labelClickShardRadius", 49.8769488, 0, 200, 0.01, true),
        Number("shards.clickSpeedMin", "Advanced_sectionShards", "Advanced_clickShardsSummary", "Advanced_labelClickShardSpeedMin", 49.8769488, 0, 200, 0.01, true),
        Number("shards.clickSpeedMax", "Advanced_sectionShards", "Advanced_clickShardsSummary", "Advanced_labelClickShardSpeedMax", 66.5025984, 0, 200, 0.01, true),
        Number("shards.trailSpacing", "Advanced_sectionShards", "Advanced_trailShardsSummary", "Advanced_labelShardSpacing", 108, 10, 500, 0.01, true),
        Number("shards.maxCount", "Advanced_sectionShards", "Advanced_trailShardsSummary", "Advanced_labelMaxShards", 50, 0, 500, 1, true),
        Number("shards.trailLifetimeMinMs", "Advanced_sectionShards", "Advanced_trailShardsSummary", "Advanced_labelTrailShardLifeMin", 200, 50, 500, 1, true),
        Number("shards.trailLifetimeMaxMs", "Advanced_sectionShards", "Advanced_trailShardsSummary", "Advanced_labelTrailShardLifeMax", 400, 50, 500, 1, true),
        Number("shards.trailRadius", "Advanced_sectionShards", "Advanced_trailShardsSummary", "Advanced_labelTrailShardRadius", 24.9384744, 0, 100, 0.01, true),
        Number("shards.trailSpeedMin", "Advanced_sectionShards", "Advanced_trailShardsSummary", "Advanced_labelTrailShardSpeedMin", 33.2512992, 0, 150, 0.01, true),
        Number("shards.trailSpeedMax", "Advanced_sectionShards", "Advanced_trailShardsSummary", "Advanced_labelTrailShardSpeedMax", 49.8769488, 0, 150, 0.01, true),
        Number("trail.width", "Advanced_sectionTrail", "Advanced_trailLayerSummary", "Advanced_labelTrailW", 2.7, 1, 25, 0.01, true),
        Number("trail.outerGlowWidth", "Advanced_sectionTrail", "Advanced_trailLayerSummary", "Advanced_labelTrailGlowW", 9, 1, 40, 0.1, true),
        Number("trail.lifetimeMs", "Advanced_sectionTrail", "Advanced_trailLayerSummary", "Advanced_labelTrailLife", 300, 50, 2000, 1, true),
        Number("trail.trailOpacity", "Advanced_sectionTrail", "Advanced_trailLayerSummary", "Advanced_labelTrailOpacity", 1, 0, 1, 0.01, true),
        Number("trail.geometryWidth", "Advanced_sectionTrail", "Advanced_trailLayerSummary", "Advanced_labelGeomWidth", 2.7, 1, 8, 0.01, true),
        Number("trail.minVertexDistance", "Advanced_sectionTrail", "Advanced_trailLayerSummary", "Advanced_labelMinVertDist", 5.4, 1, 20, 0.01, true),
        Number("trail.numCornerVertices", "Advanced_sectionTrail", "Advanced_trailLayerSummary", "Advanced_labelCornerVerts", 4, 0, 12, 1, true),
        Number("trail.numCapVertices", "Advanced_sectionTrail", "Advanced_trailLayerSummary", "Advanced_labelCapVerts", 1, 0, 6, 1, true),
        Number("bloom.threshold", "Advanced_sectionBloom", "Advanced_bloomPipelineSummary", "Advanced_labelBloomThreshold", 1, 0, 5, 0.01, true),
        Number("bloom.softKnee", "Advanced_sectionBloom", "Advanced_bloomPipelineSummary", "Advanced_labelBloomSoftKnee", 0, 0, 1, 0.01, true),
        Number("bloom.clamp", "Advanced_sectionBloom", "Advanced_bloomPipelineSummary", "Advanced_labelBloomClamp", 65472, 1, 65504, 1, true),
        Number("bloom.diffusion", "Advanced_sectionBloom", "Advanced_bloomPipelineSummary", "Advanced_labelBloomDiffusion", 7, 0, 10, 0.01, true),
        Number("bloom.resolutionScale", "Advanced_sectionBloom", "Advanced_bloomPipelineSummary", "Advanced_labelBloomResolution", 0.5, 0.1, 0.75, 0.01, true),
        Number("bloom.emissionRange", "Advanced_sectionBloom", "Advanced_bloomPipelineSummary", "Advanced_labelBloomEmission", 23.968628, 1, 64, 0.01, true),
        Number("bloom.clickEmissionScale", "Advanced_sectionBloom", "Advanced_bloomClickSummary", "Advanced_labelClickGlow", 1, 0, 4, 0.01, true),
        Number("bloom.ringBlur", "Advanced_sectionBloom", "Advanced_bloomClickSummary", "Advanced_labelBloomRing", 80, 0, 200, 0.1, true),
        Number("bloom.diskBlur", "Advanced_sectionBloom", "Advanced_bloomClickSummary", "Advanced_labelBloomDisk", 65, 0, 200, 0.1, true),
        Number("bloom.diskEmission", "Advanced_sectionBloom", "Advanced_bloomClickSummary", "Advanced_labelBloomDiskEmission", 2, 0, 8, 0.01, true),
        Number("bloom.ringEmissionAlpha", "Advanced_sectionBloom", "Advanced_bloomClickSummary", "Advanced_labelBloomRingCoreAlpha", 1, 0, 1, 0.01, true),
        Number("bloom.diskEmissionAlpha", "Advanced_sectionBloom", "Advanced_bloomClickSummary", "Advanced_labelBloomDiskCoreAlpha", 1, 0, 1, 0.01, true),
        Number("bloom.ringAlpha", "Advanced_sectionBloom", "Advanced_bloomClickSummary", "Advanced_labelBloomRingAlpha", 0.35, 0, 1, 0.01, true),
        Number("bloom.diskAlpha", "Advanced_sectionBloom", "Advanced_bloomClickSummary", "Advanced_labelBloomDiskAlpha", 0.65, 0, 1, 0.01, true),
        Number("bloom.trailEmissionAlpha", "Advanced_sectionBloom", "Advanced_bloomTrailSummary", "Advanced_labelBloomTrail", 1, 0, 1, 0.01, true),
        Number("bloom.trailAlpha", "Advanced_sectionBloom", "Advanced_bloomTrailSummary", "Advanced_labelBloomTrailAlpha", 0.18, 0, 1, 0.01, true),
        Number("bloom.trailEmission", "Advanced_sectionBloom", "Advanced_bloomTrailSummary", "Advanced_labelBloomTrailEmission", 23.968628, 0, 64, 0.01, true),
        Number("bloom.trailCoverageScale", "Advanced_sectionBloom", "Advanced_bloomTrailSummary", "Advanced_labelBloomTrailCoverage", 1, 0, 4, 0.01, true),
    ];

    private static readonly IReadOnlyDictionary<string, AdvancedFxSetting> ByPath =
        Definitions.ToDictionary(item => item.Path, StringComparer.Ordinal);

    internal static AdvancedFxSetting? Find(string key) =>
        key.StartsWith(KeyPrefix, StringComparison.Ordinal) &&
        ByPath.TryGetValue(key[KeyPrefix.Length..], out AdvancedFxSetting? definition) ? definition : null;

    internal static Dictionary<string, JsonElement> Parse(string? content, bool strict = false)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(content)) return values;
        try
        {
            if (content.Length > 65536) throw new InvalidDataException("Advanced effect settings are too large.");
            using JsonDocument document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Advanced effect settings must be an object.");
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!ByPath.TryGetValue(property.Name, out AdvancedFxSetting? definition))
                {
                    if (strict) throw new InvalidDataException("Unknown advanced effect setting: " + property.Name);
                    continue;
                }
                try
                {
                    JsonElement value = Validate(definition, property.Value);
                    if (!EqualsDefault(definition, value)) values[definition.Path] = value;
                }
                catch (InvalidDataException) when (!strict) { }
            }
        }
        catch (Exception exception) when (!strict && exception is JsonException or InvalidDataException)
        {
            values.Clear();
        }
        return values;
    }

    internal static JsonElement Validate(AdvancedFxSetting definition, JsonElement value)
    {
        bool valid;
        if (definition.Options != null)
            valid = value.ValueKind == JsonValueKind.String &&
                definition.Options.Any(option => option.Value == value.GetString());
        else if (definition.DefaultValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
            valid = value.ValueKind is JsonValueKind.True or JsonValueKind.False;
        else
            valid = value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) &&
                double.IsFinite(number) && number >= definition.Minimum && number <= definition.Maximum &&
                (definition.Step < 1 || number == Math.Truncate(number)) &&
                (definition.Step != 2 || number is -1 or 1);
        if (!valid) throw new InvalidDataException("Invalid advanced effect setting: " + definition.Path);
        return value.Clone();
    }

    internal static bool EqualsDefault(AdvancedFxSetting definition, JsonElement value) =>
        definition.DefaultValue.ValueKind == JsonValueKind.Number
            ? value.ValueKind == JsonValueKind.Number &&
                Math.Abs(value.GetDouble() - definition.DefaultValue.GetDouble()) < 0.000000001
            : definition.DefaultValue.ValueKind == JsonValueKind.String
                ? value.ValueKind == JsonValueKind.String && value.GetString() == definition.DefaultValue.GetString()
                : value.ValueKind == definition.DefaultValue.ValueKind;

    internal static JsonElement GetValue(IReadOnlyDictionary<string, JsonElement> values, AdvancedFxSetting definition) =>
        values.TryGetValue(definition.Path, out JsonElement value) ? value : definition.DefaultValue;

    internal static string Serialize(IReadOnlyDictionary<string, JsonElement> values) =>
        JsonSerializer.Serialize(Definitions.Where(item => values.ContainsKey(item.Path))
            .Select(item => (Definition: item, Value: Validate(item, values[item.Path])))
            .Where(item => !EqualsDefault(item.Definition, item.Value))
            .ToDictionary(item => item.Definition.Path, item => item.Value, StringComparer.Ordinal));

    internal static string Normalize(string? content) => Serialize(Parse(content));

    internal static string RendererPayload(string? content)
    {
        Dictionary<string, JsonElement> values = Parse(content);
        return JsonSerializer.Serialize(new
        {
            config = Definitions.Where(item => !item.IsEffectParameter && values.ContainsKey(item.Path))
                .ToDictionary(item => item.Path, item => values[item.Path], StringComparer.Ordinal),
            parameters = Definitions.Where(item => item.IsEffectParameter && values.ContainsKey(item.Path))
                .ToDictionary(item => item.Path, item => values[item.Path], StringComparer.Ordinal)
        });
    }

    internal static string Describe(AdvancedFxSetting definition, JsonElement value)
    {
        if (definition.Options != null)
            return Localization.Get(definition.Options.First(option => option.Value == value.GetString()).TitleKey);
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return Localization.Get(value.GetBoolean() ? "Basic_DarkModeOn" : "Basic_DarkModeOff");
        if (definition.Path is "rings.rotationDirection" or "rings.dissolveDirection")
            return Localization.Get(definition.Path == "rings.rotationDirection"
                ? value.GetDouble() < 0 ? "Advanced_Counterclockwise" : "Advanced_Clockwise"
                : value.GetDouble() < 0 ? "Advanced_Reverse" : "Advanced_Forward");
        return value.GetDouble().ToString("F" + definition.Precision, CultureInfo.CurrentCulture);
    }

    private static AdvancedFxSetting Number(string path, string group, string section, string title,
        double value, double minimum, double maximum, double step, bool isEffectParameter) =>
        new(path, group, section, title, JsonSerializer.SerializeToElement(value), minimum, maximum, step, isEffectParameter);

    private static AdvancedFxSetting Boolean(string path, string group, string section, string title, bool value) =>
        new(path, group, section, title, JsonSerializer.SerializeToElement(value), 0, 1, 1, false);

    private static AdvancedFxSetting Choice(string path, string group, string section, string title,
        string value, IReadOnlyList<AdvancedFxOption> options) =>
        new(path, group, section, title, JsonSerializer.SerializeToElement(value), 0, 1, 1, false, options);
}
