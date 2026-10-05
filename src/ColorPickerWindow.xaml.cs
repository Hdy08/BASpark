using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using VirtualKey = Windows.System.VirtualKey;

namespace BASpark;

/// <summary>
/// 取色器窗口。WinUI 3 没有 WPF 的 <c>ShowDialog</c>/<c>DialogResult</c>，
/// 因此以 <see cref="ShowDialogAsync"/> 返回任务：确认得到颜色，取消或关闭得到 null。
/// </summary>
public partial class ColorPickerWindow : Window
{
    /// <summary>设计尺寸（有效像素），实际尺寸按目标显示器 DPI 缩放。</summary>
    private const int DesignWidth = 420;
    private const int DesignHeight = 568;

    private readonly TaskCompletionSource<Color?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private HsvColor _hsv;
    private bool _draggingColorField;
    private uint _dragPointerId;
    private bool _editingHex;
    private bool _confirmFocusTransition;
    private bool _updatingControls;
    private bool _closed;
    private bool _titleBarApplied;

    public Color SelectedColor { get; private set; }

    public ColorPickerWindow(Color initialColor)
    {
        InitializeComponent();

        // 去掉系统标题栏、改用原生 TitleBar 控件；必须在视觉树加载后执行。
        RootGrid.Loaded += (_, _) => ApplyCustomTitleBar();

        SelectedColor = Color.FromArgb(255, initialColor.R, initialColor.G, initialColor.B);
        _hsv = ColorPickerColorMath.RgbToHsv(SelectedColor);

        ApplyLocalizedText();

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.ResolveElementTheme();
            root.Loaded += ColorPickerWindow_Loaded;
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        AppWindow.IsShownInSwitchers = false;

        WindowChrome.ApplyAppIcon(this);
        ApplyTitleBarTheme(App.ResolveElementTheme());

        // 等价于 WPF 的 PreviewMouseLeftButtonDown：Button 会把 PointerPressed
        // 标记为已处理，所以必须 handledEventsToo 才能在失焦前抢到这次点击。
        BtnConfirm.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(Confirm_PointerPressed),
            handledEventsToo: true);
    }

    /// <summary>返回用户确认后的颜色；取消时返回 null。</summary>
    public Task<Color?> ShowDialogAsync(XamlRoot xamlRoot)
    {
        if (_closed)
        {
            return Task.FromResult<Color?>(null);
        }

        WindowChrome.SetInitialSize(this, DesignWidth, DesignHeight);

        Closed += ColorPickerWindow_Closed;
        Activate();
        return _completion.Task;
    }

    // ------------------------------------------------------------------
    // 窗口生命周期
    // ------------------------------------------------------------------

    private void ApplyLocalizedText()
    {
        Title = Localization.Get("ColorPicker_Title");
        AppTitleBar.Title = Localization.Get("ColorPicker_Title");
        LblHueSaturation.Text = Localization.Get("ColorPicker_HueSaturation");
        LblBrightness.Text = Localization.Get("ColorPicker_Brightness");
        LblPreview.Text = Localization.Get("ColorPicker_Preview");
        LblRed.Text = Localization.Get("ColorPicker_Red");
        LblGreen.Text = Localization.Get("ColorPicker_Green");
        LblBlue.Text = Localization.Get("ColorPicker_Blue");
        LblHex.Text = Localization.Get("ColorPicker_Hex");
        BtnConfirm.Content = Localization.Get("ColorPicker_Confirm");
        BtnCancel.Content = Localization.Get("ColorPicker_Cancel");
    }

    private void ColorPickerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateVisuals(updateText: true);

        if (Content is FrameworkElement root)
        {
            ApplyTitleBarTheme(root.ActualTheme);
        }

        // 方法组直接当 WinRT 委托传会在 CsWinRT 封送时抛 InvalidCastException，
        // 因此统一用 lambda 包装。
        App.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => UpdateColorFieldMarker());
    }

    /// <summary>
    /// 去掉系统标题栏、改用原生 <c>TitleBar</c> 控件。
    ///
    /// 两个必须遵守的约束（已在 ControlPanelWindow 上实测）：
    ///   1. 只设置 <c>ExtendsContentIntoTitleBar</c>，**不要**再调用
    ///      <c>SetTitleBar(AppTitleBar)</c>。SetTitleBar 只适用于普通 UIElement
    ///      拖拽区域；对 TitleBar 控件调用会抛 E_BOUNDS（0x800f1000），异常在
    ///      Microsoft.UI.Xaml.dll 内未被捕获，进程直接崩溃。
    ///   2. 必须在视觉树加载后调用；构造函数里执行会抛 E_INVALIDARG 并导致
    ///      窗口构造失败、界面完全不出现。
    /// </summary>
    private void ApplyCustomTitleBar()
    {
        if (_titleBarApplied)
        {
            return;
        }

        _titleBarApplied = true;

        try
        {
            ExtendsContentIntoTitleBar = true;
        }
        catch (Exception)
        {
            // 失败时回退到系统标题栏，功能不受影响。
        }
    }

    /// <summary>
    /// WinUI 只负责内容区主题，非客户区标题栏要自己跟随；
    /// <see cref="ElementTheme.Default"/> 时留给 <c>Loaded</c> 用实际主题校准。
    /// </summary>
    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        if (theme == ElementTheme.Default)
        {
            return;
        }

        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            AppWindowTitleBar? titleBar = AppWindow?.TitleBar;
            if (titleBar == null)
            {
                return;
            }

            titleBar.PreferredTheme = theme == ElementTheme.Dark
                ? TitleBarTheme.Dark
                : TitleBarTheme.Light;
        }
        catch (Exception)
        {
        }
    }

    private void ColorPickerWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _completion.TrySetResult(null);
    }

    private void Complete(Color? result)
    {
        _completion.TrySetResult(result);
        Close();
    }

    // ------------------------------------------------------------------
    // 二维取色区
    // ------------------------------------------------------------------

    private void ColorField_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _draggingColorField = true;
        _dragPointerId = e.Pointer.PointerId;
        ColorField.CapturePointer(e.Pointer);
        UpdateColorFieldFromPointer(e.GetCurrentPoint(ColorFieldMarkerCanvas).Position);
        e.Handled = true;
    }

    private void ColorField_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingColorField || e.Pointer.PointerId != _dragPointerId)
        {
            return;
        }

        UpdateColorFieldFromPointer(e.GetCurrentPoint(ColorFieldMarkerCanvas).Position);
        e.Handled = true;
    }

    private void ColorField_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingColorField || e.Pointer.PointerId != _dragPointerId)
        {
            return;
        }

        UpdateColorFieldFromPointer(e.GetCurrentPoint(ColorFieldMarkerCanvas).Position);
        _draggingColorField = false;
        _dragPointerId = 0;
        ColorField.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ColorField_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _draggingColorField = false;
        _dragPointerId = 0;
    }

    private void ColorFieldSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // WinUI 没有 ClipToBounds，用矩形裁剪代替 WPF 的 ClipToBounds="True"。
        ColorFieldSurface.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
        };

        UpdateColorFieldMarker();
    }

    private void UpdateColorFieldFromPointer(Point position)
    {
        double width = ColorFieldMarkerCanvas.ActualWidth;
        double height = ColorFieldMarkerCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double hueRatio = Math.Clamp(position.X / width, 0, 1);
        double saturation = Math.Clamp(position.Y / height, 0, 1);
        double hue = Math.Min(hueRatio * 360, 359.999999);
        _hsv = new HsvColor(hue, saturation, _hsv.Value);
        UpdateSelectedColor(updateText: true);
    }

    private void UpdateColorFieldMarker()
    {
        double width = ColorFieldMarkerCanvas.ActualWidth;
        double height = ColorFieldMarkerCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Canvas.SetLeft(ColorFieldMarker, (_hsv.Hue / 360 * width) - (ColorFieldMarker.Width / 2));
        Canvas.SetTop(ColorFieldMarker, (_hsv.Saturation * height) - (ColorFieldMarker.Height / 2));
    }

    // ------------------------------------------------------------------
    // 明度
    // ------------------------------------------------------------------

    private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _ = sender;
        if (_updatingControls)
        {
            return;
        }

        _hsv = new HsvColor(_hsv.Hue, _hsv.Saturation, e.NewValue);
        UpdateSelectedColor(updateText: true);
    }

    // ------------------------------------------------------------------
    // 文本输入
    // ------------------------------------------------------------------

    private void RgbTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!_updatingControls && !_confirmFocusTransition && !TryApplyRgbText())
        {
            UpdateTextInputs();
        }
    }

    private void HexTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!_updatingControls && !_confirmFocusTransition && !TryApplyHexText())
        {
            UpdateTextInputs();
        }
    }

    private void ColorInput_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        bool applied = ReferenceEquals(sender, TxtHex)
            ? TryApplyHexText()
            : TryApplyRgbText();
        if (!applied)
        {
            UpdateTextInputs();
            if (sender is TextBox invalidTextBox)
            {
                invalidTextBox.SelectAll();
            }

            e.Handled = true;
            return;
        }

        e.Handled = true;
        Complete(SelectedColor);
    }

    private void ColorInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        _editingHex = ReferenceEquals(textBox, TxtHex);
        App.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => textBox.SelectAll());
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        _ = sender;
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        Complete(null);
    }

    private bool TryApplyRgbText()
    {
        if (!byte.TryParse(TxtRed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte red) ||
            !byte.TryParse(TxtGreen.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte green) ||
            !byte.TryParse(TxtBlue.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte blue))
        {
            return false;
        }

        SelectedColor = Color.FromArgb(255, red, green, blue);
        _hsv = ColorPickerColorMath.RgbToHsv(SelectedColor);
        UpdateVisuals(updateText: true);
        return true;
    }

    private bool TryApplyHexText()
    {
        if (!ColorPickerColorMath.TryParseHex(TxtHex.Text, out Color color))
        {
            return false;
        }

        SelectedColor = color;
        _hsv = ColorPickerColorMath.RgbToHsv(color);
        UpdateVisuals(updateText: true);
        return true;
    }

    // ------------------------------------------------------------------
    // 呈现
    // ------------------------------------------------------------------

    private void UpdateSelectedColor(bool updateText)
    {
        SelectedColor = ColorPickerColorMath.HsvToRgb(_hsv);
        UpdateVisuals(updateText);
    }

    private void UpdateVisuals(bool updateText)
    {
        _updatingControls = true;
        try
        {
            ColorPreview.Background = new SolidColorBrush(SelectedColor);
            BrightnessOverlay.Opacity = 1 - _hsv.Value;
            BrightnessSlider.Value = _hsv.Value;
            BrightnessTrackFill.Fill = CreateBrightnessBrush();
            UpdateColorFieldMarker();

            if (updateText)
            {
                UpdateTextInputsCore();
            }
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private LinearGradientBrush CreateBrightnessBrush()
    {
        Color fullBrightness = ColorPickerColorMath.HsvToRgb(_hsv.Hue, _hsv.Saturation, 1);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };

        brush.GradientStops.Add(new GradientStop { Color = fullBrightness, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 0, 0, 0), Offset = 1 });
        return brush;
    }

    private void UpdateTextInputs()
    {
        _updatingControls = true;
        try
        {
            UpdateTextInputsCore();
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void UpdateTextInputsCore()
    {
        TxtRed.Text = SelectedColor.R.ToString(CultureInfo.InvariantCulture);
        TxtGreen.Text = SelectedColor.G.ToString(CultureInfo.InvariantCulture);
        TxtBlue.Text = SelectedColor.B.ToString(CultureInfo.InvariantCulture);
        TxtHex.Text = ColorPickerColorMath.ToHex(SelectedColor);
    }

    // ------------------------------------------------------------------
    // 底部按钮
    // ------------------------------------------------------------------

    private void Confirm_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _confirmFocusTransition = true;
        App.DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () => _confirmFocusTransition = false);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        bool applied = _editingHex
            ? TryApplyHexText()
            : TryApplyRgbText();
        if (!applied)
        {
            UpdateTextInputs();
            return;
        }

        Complete(SelectedColor);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Complete(null);
    }
}
