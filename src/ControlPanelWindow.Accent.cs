using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace BASpark;

public sealed partial class ControlPanelWindow
{
    private sealed class ColorCardAnimation(Border card, Expander expander, FrameworkElement body, Func<bool> isClosed)
    {
        private CompositionRoundedRectangleGeometry? _geometry;
        private Border? _contentHost;
        private bool _animating;
        private int _generation;

        internal void Loaded()
        {
            expander.ApplyTemplate();
            _contentHost = FindVisualDescendant<Border>(expander, "ExpanderContentHost");
            Visual visual = ElementCompositionPreview.GetElementVisual(card);
            _geometry ??= visual.Compositor.CreateRoundedRectangleGeometry();
            _geometry.CornerRadius = new Vector2(4);
            visual.Clip = visual.Compositor.CreateGeometricClip(_geometry);
            if (_animating || _contentHost == null) return;
            _contentHost.Height = expander.IsExpanded ? double.NaN : 0;
            _contentHost.IsHitTestVisible = expander.IsExpanded;
            card.UpdateLayout();
            SizeChanged(new Size(card.ActualWidth, card.ActualHeight));
        }

        internal void SizeChanged(Size size)
        {
            if (_geometry != null && !_animating)
                _geometry.Size = new Vector2((float)size.Width, (float)size.Height);
        }

        internal void Dispose()
        {
            _generation++;
            _geometry?.StopAnimation("Size.Y");
            _geometry = null;
            _contentHost = null;
        }

        internal void Animate(bool expanding)
        {
            if (_contentHost == null || _geometry == null || !card.IsLoaded) return;
            int generation = ++_generation;
            body.Measure(new Size(Math.Max(0, card.ActualWidth - 34), double.PositiveInfinity));
            double contentHeight = body.DesiredSize.Height + _contentHost.Padding.Top + _contentHost.Padding.Bottom;
            if (!_animating)
                _geometry.Size = new Vector2((float)card.ActualWidth, expanding ? (float)card.MinHeight : (float)card.ActualHeight);
            _animating = true;
            _contentHost.Height = contentHeight;
            _contentHost.IsHitTestVisible = expanding;
            card.UpdateLayout();
            var compositor = _geometry.Compositor;
            ElementCompositionPreview.GetElementVisual(card).Clip = compositor.CreateGeometricClip(_geometry);
            float targetHeight = expanding ? (float)card.ActualHeight : (float)card.MinHeight;
            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.InsertExpressionKeyFrame(0, "this.StartingValue");
            animation.InsertKeyFrame(1, targetHeight,
                compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.7f), new Vector2(0.2f, 1)));
            animation.Duration = TimeSpan.FromMilliseconds(expanding ? 220 : 180);
            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            _geometry.StartAnimation("Size.Y", animation);
            batch.Completed += (_, _) =>
            {
                batch.Dispose();
                App.DispatcherQueue.TryEnqueue(() =>
                {
                    if (isClosed() || generation != _generation) return;
                    _geometry.StopAnimation("Size.Y");
                    _contentHost.Height = expanding ? double.NaN : 0;
                    card.UpdateLayout();
                    _animating = false;
                    SizeChanged(new Size(card.ActualWidth, card.ActualHeight));
                });
            };
            batch.End();
        }
    }

    private ColorCardAnimation _effectColorCardAnimation = null!;
    private ColorCardAnimation _accentColorCardAnimation = null!;
    private readonly SolidColorBrush _accentColorPreviewBrush = new();
    private string _customAccentColor = ConfigManager.CustomAccentColor;
    private bool _syncingAccentColor;
    private bool _isAccentDragging;
    private bool _accentPreviewPending;
    private long _lastAccentLabelUpdate;
    private ColorSpectrum? _configuredAccentSpectrum;

    internal void RefreshAccentColor() => App.ApplyAccentColor(RootGrid);

    private void SetupAccentColorControls()
    {
        _effectColorCardAnimation = new(EffectColorCard, EffectColorExpander, ColorPickerBody, () => _isClosed);
        _accentColorCardAnimation = new(AccentColorCard, AccentColorExpander, AccentColorPickerBody, () => _isClosed);
        AccentColorPreview.Background = ExpandedAccentColorPreview.Background = _accentColorPreviewBrush;
        for (int index = 0; index < 16; index++)
        {
            Color color = ColorPickerColorMath.HsvToRgb(index * 20, 1, 1);
            string hex = ColorPickerColorMath.ToHex(color);
            var swatch = new RadioButton
            {
                Tag = hex, Background = new SolidColorBrush(color),
                Style = (Style)Application.Current.Resources["BasColorSwatchStyle"]
            };
            ToolTipService.SetToolTip(swatch, hex);
            AutomationProperties.SetName(swatch, hex);
            AccentColorPresets.Items.Add(swatch);
        }
        AccentColorPicker.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(AccentColorPicker_PointerPressed), true);
        RootGrid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(AccentColorPicker_PointerFinished), true);
        RootGrid.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(AccentColorPicker_PointerFinished), true);
        AccentColorPicker.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(AccentColorPicker_PointerFinished), true);
    }

    private void DisposeAccentColorControls()
    {
        _isAccentDragging = _accentPreviewPending = false;
        _configuredAccentSpectrum = null;
        AccentColorHexInput.Loaded -= InputControl_Loaded;
        AccentColorPicker.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(AccentColorPicker_PointerPressed));
        RootGrid.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(AccentColorPicker_PointerFinished));
        RootGrid.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(AccentColorPicker_PointerFinished));
        AccentColorPicker.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(AccentColorPicker_PointerFinished));
    }

    private void RadioAccentColor_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!IsUiReady || _isLoading) return;
        SetSettingsVisibility(SectionBasic, true, (AccentColorCard, RadioAccentColor.SelectedIndex == 0));
    }

    private string GetCustomAccentColor() => AccentColorHexInput.FocusState != FocusState.Unfocused &&
        ColorPickerColorMath.TryParseHex(AccentColorHexInput.Text, out Color color) ? ColorPickerColorMath.ToHex(color) : _customAccentColor;

    private void AccentColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (!IsUiReady || _syncingAccentColor) return;
        _customAccentColor = ColorPickerColorMath.ToHex(args.NewColor);
        _accentColorPreviewBrush.Color = Color.FromArgb(255, args.NewColor.R, args.NewColor.G, args.NewColor.B);
        _accentPreviewPending = true;
        EnsureColorPreviewRendering();
    }

    private void AccentColorPicker_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(AccentColorPicker).IsInContact) return;
        DependencyObject? source = args.OriginalSource as DependencyObject;
        while (source != null && source != AccentColorPicker && source is not ColorSpectrum and not ColorPickerSlider)
            source = VisualTreeHelper.GetParent(source);
        if (source is not ColorSpectrum and not ColorPickerSlider) return;
        _isAccentDragging = true;
        _lastAccentLabelUpdate = 0;
        EnsureColorPreviewRendering();
    }

    private void AccentColorPicker_PointerFinished(object sender, PointerRoutedEventArgs args)
    {
        if (!_isAccentDragging) return;
        _isAccentDragging = false;
        _accentPreviewPending = true;
        EnsureColorPreviewRendering();
        QueueSettingsChangeCheck(SettingsSections.Basic);
    }

    private void AccentColorPresets_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingAccentColor || AccentColorPresets.SelectedItem is not RadioButton { Tag: string hex }) return;
        _customAccentColor = hex;
        UpdateAccentColorPreview();
        QueueSettingsChangeCheck(SettingsSections.Basic);
    }

    private void AccentColorHexInput_LostFocus(object sender, RoutedEventArgs args) => CommitAccentColorHex();

    private void AccentColorHexInput_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter) return;
        CommitAccentColorHex();
        Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    private void CommitAccentColorHex()
    {
        if (!IsUiReady || _syncingAccentColor) return;
        _customAccentColor = GetCustomAccentColorFromInput();
        UpdateAccentColorPreview();
        QueueSettingsChangeCheck(SettingsSections.Basic);
    }

    private string GetCustomAccentColorFromInput() => ColorPickerColorMath.TryParseHex(AccentColorHexInput.Text, out Color color)
        ? ColorPickerColorMath.ToHex(color) : _customAccentColor;

    private void UpdateAccentColorPreview(bool updatePicker = true)
    {
        long now = Stopwatch.GetTimestamp();
        if (_isAccentDragging && now - _lastAccentLabelUpdate < Stopwatch.Frequency / 20) return;
        _lastAccentLabelUpdate = now;
        _syncingAccentColor = true;
        try
        {
            ColorPickerColorMath.TryParseHex(_customAccentColor, out Color color);
            _accentColorPreviewBrush.Color = color;
            if (AccentColorHexInput.Text != _customAccentColor) AccentColorHexInput.Text = _customAccentColor;
            if (ExpandedAccentColorHex.Text != _customAccentColor) ExpandedAccentColorHex.Text = _customAccentColor;
            if (updatePicker && AccentColorPicker.Color != color) AccentColorPicker.Color = color;
            int selected = -1;
            for (int index = 0; index < AccentColorPresets.Items.Count; index++)
                if (AccentColorPresets.Items[index] is RadioButton { Tag: string hex } && hex == _customAccentColor) selected = index;
            if (AccentColorPresets.SelectedIndex != selected) AccentColorPresets.SelectedIndex = selected;
        }
        finally { _syncingAccentColor = false; }
    }

    private void AccentColorExpander_Loaded(object sender, RoutedEventArgs args) => _accentColorCardAnimation.Loaded();
    private void AccentColorCard_SizeChanged(object sender, SizeChangedEventArgs args) => _accentColorCardAnimation?.SizeChanged(args.NewSize);
    private void AccentColorExpander_Expanding(Expander sender, object args) => _accentColorCardAnimation.Animate(true);
    private void AccentColorExpander_Collapsed(Expander sender, object args) => _accentColorCardAnimation.Animate(false);

    private void AccentColorPicker_Loaded(object sender, RoutedEventArgs args)
    {
        AccentColorPicker.ApplyTemplate();
        if (FindVisualDescendant<Grid>(AccentColorPicker, "LayoutRoot") is { } layout && layout.ColumnDefinitions.Count == 3)
        {
            if (FindVisualDescendant<ColorPickerSlider>(AccentColorPicker, "AlphaSlider") is { } alpha &&
                VisualTreeHelper.GetParent(alpha) is UIElement container) container.Visibility = Visibility.Collapsed;
            layout.ColumnDefinitions.RemoveAt(2);
            layout.Width = 192;
        }
        if (FindVisualDescendant<ColorSpectrum>(AccentColorPicker, "ColorSpectrum") is { } spectrum && spectrum != _configuredAccentSpectrum)
        {
            _configuredAccentSpectrum = spectrum;
            void UpdateClip()
            {
                if (FindVisualDescendant<Grid>(spectrum, "SizingGrid") is { } sizing)
                    sizing.Clip = new RectangleGeometry { Rect = new Rect(-8, -8, spectrum.ActualWidth + 16, spectrum.ActualHeight + 16) };
            }
            spectrum.SizeChanged += (_, _) => UpdateClip();
            UpdateClip();
        }
    }
}
