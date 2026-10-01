using System.Reflection;

namespace BASpark;

/// <summary>
/// 读取嵌入式特效渲染资源（Web/index.html、fx-adapter.js、vendor/ba-click-fx.iife.js）。
/// 迁移到 WinUI 3 后不再使用 WPF 的 <c>pack://application:,,,/</c> URI，
/// 改为按 <see cref="Assembly.GetManifestResourceStream(string)"/> 读取。
/// 资源内容与 LogicalName 在 BASpark.csproj 中固定，渲染链路本身不做任何改动。
/// </summary>
internal static class WebRendererResourceProvider
{
    internal const string PrimaryRendererResourcePath = "Web/index.html";
    internal const string LegacyRendererResourcePath = "Web/index.legacy.html";
    internal const string RendererVendorResourcePath = "Web/vendor/ba-click-fx.iife.js";
    internal const string RendererAdapterResourcePath = "Web/fx-adapter.js";

    private static readonly Assembly ResourceAssembly = typeof(WebRendererResourceProvider).Assembly;
    private static readonly string AssemblyName =
        ResourceAssembly.GetName().Name ?? "BASpark";

    public static string ReadResourceText(string resourcePath)
    {
        string logicalName = ToLogicalName(resourcePath);
        using Stream? stream = ResourceAssembly.GetManifestResourceStream(logicalName);
        if (stream == null)
        {
            throw new InvalidOperationException(
                $"Embedded renderer resource '{resourcePath}' was not found (looked for '{logicalName}').");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>把 <c>Web/vendor/x.js</c> 映射为 <c>BASpark.Web.vendor.x.js</c>。</summary>
    internal static string ToLogicalName(string resourcePath)
    {
        string normalized = resourcePath.Replace('\\', '/').TrimStart('/');
        return AssemblyName + "." + normalized.Replace('/', '.');
    }
}
