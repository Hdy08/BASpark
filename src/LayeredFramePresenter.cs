using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace BASpark;

internal sealed class LayeredFramePresenter : IDisposable
{
    private readonly NativeMethods.RECT _bounds;
    private readonly LayeredWindowHost _host;
    private CoreWebView2SharedBuffer? _buffer;
    private Windows.Foundation.IMemoryBufferReference? _reference;
    private IntPtr _sharedPixels;
    private IntPtr _bitmapPixels;
    private IntPtr _deviceContext;
    private IntPtr _bitmap;
    private IntPtr _originalBitmap;

    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal IntPtr Handle => _host.Handle;

    internal LayeredFramePresenter(CoreWebView2Environment environment, NativeMethods.RECT bounds)
    {
        _bounds = bounds;
        _host = new LayeredWindowHost(bitmapBacked: true);
        try
        {
            int byteCount = checked(bounds.Width * bounds.Height * 4);
            _buffer = environment.CreateSharedBuffer((ulong)byteCount);
            _reference = _buffer.Buffer;
            WinRT.CastExtensions.As<IMemoryBufferByteAccess>(_reference).GetBuffer(out _sharedPixels, out uint capacity);
            if (_sharedPixels == IntPtr.Zero || capacity < byteCount)
            {
                throw new InvalidOperationException("The overlay frame buffer has insufficient capacity.");
            }

            _host.SetBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            InitializeBitmap();
            UpdateWindow();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Share(CoreWebView2 browser, string? generation)
    {
        browser.PostSharedBufferToScript(_buffer!, CoreWebView2SharedBufferAccess.ReadWrite,
            JsonSerializer.Serialize(new { type = "baspark-frame-buffer", generation, id = Id,
                width = _bounds.Width, height = _bounds.Height }));
    }

    internal void SetTopmost()
    {
        if (_host.IsVisible) _host.SetTopmost();
    }

    private unsafe void InitializeBitmap()
    {
        _deviceContext = CreateCompatibleDC(IntPtr.Zero);
        if (_deviceContext == IntPtr.Zero) throw new Win32Exception();

        var header = new BitmapHeader(40, _bounds.Width, _bounds.Height, 1, 32, 0, 0, 0, 0, 0, 0);
        _bitmap = CreateDIBSection(_deviceContext, in header, 0, out _bitmapPixels, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero) throw new Win32Exception();

        _originalBitmap = SelectObject(_deviceContext, _bitmap);
        if (_originalBitmap == IntPtr.Zero || _originalBitmap == new IntPtr(-1)) throw new Win32Exception();

        new Span<byte>((void*)_bitmapPixels, checked(_bounds.Width * _bounds.Height * 4)).Clear();
    }

    internal void Present(JsonElement rectangle)
    {
        if (rectangle.ValueKind != JsonValueKind.Array || rectangle.GetArrayLength() != 4 ||
            !rectangle[0].TryGetInt32(out int left) || !rectangle[1].TryGetInt32(out int bottom) ||
            !rectangle[2].TryGetInt32(out int width) || !rectangle[3].TryGetInt32(out int height) ||
            left < 0 || bottom < 0 || width <= 0 || height <= 0 ||
            left > _bounds.Width - width || bottom > _bounds.Height - height)
        {
            throw new ArgumentException("Invalid overlay frame rectangle.", nameof(rectangle));
        }

        int rowBytes = width * 4;
        int stride = _bounds.Width * 4;
        IntPtr destinationRow = _bitmapPixels + bottom * stride + left * 4;
        IntPtr sourceRow = _sharedPixels;
        for (int row = 0; row < height; row++)
        {
            CopyMemory(destinationRow, sourceRow, (nuint)rowBytes);
            destinationRow += stride;
            sourceRow += rowBytes;
        }

        var dirty = new NativeMethods.RECT
        {
            Left = left,
            Right = left + width,
            Top = _bounds.Height - bottom - height,
            Bottom = _bounds.Height - bottom
        };
        UpdateWindow(dirty);
        if (!_host.IsVisible) _host.Show();
    }

    private unsafe void UpdateWindow(NativeMethods.RECT? area = null)
    {
        var dirty = area.GetValueOrDefault();
        var destination = new NativeMethods.POINT { x = _bounds.Left, y = _bounds.Top };
        var source = new NativeMethods.POINT();
        var size = new BitmapSize(_bounds.Width, _bounds.Height);
        var blend = new BlendFunction(0, 0, byte.MaxValue, 1);
        const uint usePerPixelAlpha = 2;
        var update = new LayeredWindowUpdate((uint)sizeof(LayeredWindowUpdate), IntPtr.Zero,
            (IntPtr)(&destination), (IntPtr)(&size), _deviceContext, (IntPtr)(&source),
            0, (IntPtr)(&blend), usePerPixelAlpha, area.HasValue ? (IntPtr)(&dirty) : IntPtr.Zero);

        if (!UpdateLayeredWindowIndirect(Handle, in update)) throw new Win32Exception();
    }

    public void Dispose()
    {
        _host.Dispose();
        if (_deviceContext != IntPtr.Zero)
        {
            if (_originalBitmap != IntPtr.Zero && _originalBitmap != new IntPtr(-1))
                SelectObject(_deviceContext, _originalBitmap);
            DeleteDC(_deviceContext);
            _deviceContext = IntPtr.Zero;
        }
        if (_bitmap != IntPtr.Zero)
        {
            DeleteObject(_bitmap);
            _bitmap = IntPtr.Zero;
        }
        _reference?.Dispose();
        _reference = null;
        _buffer?.Dispose();
        _buffer = null;
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr data, out uint capacity);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct BitmapHeader(uint Size, int Width, int Height, ushort Planes, ushort BitsPerPixel,
        uint Compression, uint ImageLength, int PixelsPerMeterX, int PixelsPerMeterY, uint Colors, uint ImportantColors);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct BitmapSize(int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct BlendFunction(byte Operation, byte Flags, byte Opacity, byte AlphaFormat);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct LayeredWindowUpdate(uint Size, IntPtr DestinationDc, IntPtr Destination,
        IntPtr Dimensions, IntPtr SourceDc, IntPtr Source, uint ColorKey, IntPtr Blend, uint Flags, IntPtr Dirty);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindowIndirect(IntPtr window, in LayeredWindowUpdate update);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, in BitmapHeader header,
        uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr bitmap);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr bitmap);

    [DllImport("ntdll.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, nuint length);
}
