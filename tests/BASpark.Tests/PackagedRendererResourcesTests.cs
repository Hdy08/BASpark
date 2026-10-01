using System.Reflection;

namespace BASpark.Tests;

public class PackagedRendererResourcesTests
{
    /// <summary>
    /// 渲染负载以 <c>EmbeddedResource</c> + 显式 LogicalName 打包
    /// （BASpark.csproj）。迁移到 WinUI 3 后不再有 WPF 的 <c>.g.resources</c>，
    /// 但资源内容与加载路径必须保持不变——特效渲染不允许被改动。
    /// </summary>
    private static readonly string[] ExpectedWebResources =
    {
        "Web/index.html",
        "Web/index.legacy.html",
        "Web/fx-adapter.js",
        "Web/vendor/ba-click-fx.iife.js"
    };

    private static readonly string[] ExpectedLicenseFiles =
    {
        "LICENSE",
        "THIRD_PARTY_NOTICES.md",
        "VERSION.txt"
    };

    [Fact]
    public void WebResources_AreEmbeddedAndNonEmpty()
    {
        Assembly assembly = typeof(WebRendererDocumentBuilder).Assembly;

        foreach (string expectedResource in ExpectedWebResources)
        {
            string logicalName = WebRendererResourceProvider.ToLogicalName(expectedResource);

            Assert.Contains(logicalName, assembly.GetManifestResourceNames());

            string content = WebRendererResourceProvider.ReadResourceText(expectedResource);
            Assert.False(
                string.IsNullOrWhiteSpace(content),
                $"Embedded renderer resource '{expectedResource}' is empty.");
        }
    }

    [Fact]
    public void PrimaryRenderer_KeepsTheScriptPlaceholderContract()
    {
        // 宿主通过 WebRendererDocumentBuilder 注入 vendor + adapter；
        // 占位符缺失或重复都会让渲染器初始化失败。
        string html = WebRendererResourceProvider.ReadResourceText(
            WebRendererResourceProvider.PrimaryRendererResourcePath);

        Assert.Contains(
            WebRendererDocumentBuilder.ScriptPlaceholder,
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ThirdPartyLicenseFiles_AreCopiedToOutput()
    {
        string licenseDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "licenses",
            "ba-click-fx");

        foreach (string expectedFile in ExpectedLicenseFiles)
        {
            var file = new FileInfo(Path.Combine(licenseDirectory, expectedFile));

            Assert.True(file.Exists, $"Missing third-party file: {file.FullName}");
            Assert.True(file.Length > 0, $"Empty third-party file: {file.FullName}");
        }
    }
}
