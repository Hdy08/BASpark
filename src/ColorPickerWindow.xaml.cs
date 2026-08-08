using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;
using MediaGradientStop = System.Windows.Media.GradientStop;
using MediaLinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonState = System.Windows.Input.MouseButtonState;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace BASpark
{
    public partial class ColorPickerWindow : Window
    {
        private HsvColor _hsv;
        private bool _draggingColorField;
        private bool _editingHex;
        private bool _confirmFocusTransition;
        private bool _updatingControls;

        public MediaColor SelectedColor { get; private set; }

        public ColorPickerWindow(MediaColor initialColor)
        {
            InitializeComponent();
            SelectedColor = MediaColor.FromRgb(initialColor.R, initialColor.G, initialColor.B);
            _hsv = ColorPickerColorMath.RgbToHsv(SelectedColor);

            ApplyLocalizedText();
            ThemeManager.ApplyWindow(this);
            SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
            Activated += (_, _) => ThemeManager.ApplyWindow(this);
            Loaded += ColorPickerWindow_Loaded;
        }

        private void ApplyLocalizedText()
        {
            Title = Localization.Get("ColorPicker_Title");
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
            _ = sender;
            _ = e;
            UpdateVisuals(updateText: true);
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateColorFieldMarker));
        }

        private void ColorField_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
        {
            _ = sender;
            _draggingColorField = true;
            ColorField.CaptureMouse();
            UpdateColorFieldFromPointer(e.GetPosition(ColorFieldMarkerCanvas));
            e.Handled = true;
        }

        private void ColorField_MouseMove(object sender, WpfMouseEventArgs e)
        {
            _ = sender;
            if (_draggingColorField && e.LeftButton == WpfMouseButtonState.Pressed)
            {
                UpdateColorFieldFromPointer(e.GetPosition(ColorFieldMarkerCanvas));
                e.Handled = true;
            }
        }

        private void ColorField_MouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
        {
            _ = sender;
            if (!_draggingColorField)
            {
                return;
            }

            UpdateColorFieldFromPointer(e.GetPosition(ColorFieldMarkerCanvas));
            _draggingColorField = false;
            ColorField.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void ColorField_LostMouseCapture(object sender, WpfMouseEventArgs e)
        {
            _ = sender;
            _ = e;
            _draggingColorField = false;
        }

        private void UpdateColorFieldFromPointer(WpfPoint position)
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

        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _ = sender;
            if (_updatingControls)
            {
                return;
            }

            _hsv = new HsvColor(_hsv.Hue, _hsv.Saturation, e.NewValue);
            UpdateSelectedColor(updateText: true);
        }

        private void RgbTextBox_LostKeyboardFocus(object sender, WpfKeyboardFocusChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (!_updatingControls && !_confirmFocusTransition && !TryApplyRgbText())
            {
                UpdateTextInputs();
            }
        }

        private void HexTextBox_LostKeyboardFocus(object sender, WpfKeyboardFocusChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (!_updatingControls && !_confirmFocusTransition && !TryApplyHexText())
            {
                UpdateTextInputs();
            }
        }

        private void ColorInput_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key != WpfKey.Enter)
            {
                return;
            }

            bool applied = ReferenceEquals(sender, TxtHex)
                ? TryApplyHexText()
                : TryApplyRgbText();
            if (!applied)
            {
                UpdateTextInputs();
                if (sender is WpfTextBox invalidTextBox)
                {
                    invalidTextBox.SelectAll();
                }
                e.Handled = true;
                return;
            }

            DialogResult = true;
            e.Handled = true;
        }

        private void ColorInput_GotKeyboardFocus(object sender, WpfKeyboardFocusChangedEventArgs e)
        {
            _ = e;
            if (sender is WpfTextBox textBox)
            {
                _editingHex = ReferenceEquals(textBox, TxtHex);
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(textBox.SelectAll));
            }
        }

        private void Confirm_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
        {
            _ = sender;
            _ = e;
            _confirmFocusTransition = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _confirmFocusTransition = false));
        }

        private bool TryApplyRgbText()
        {
            if (!byte.TryParse(TxtRed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte red) ||
                !byte.TryParse(TxtGreen.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte green) ||
                !byte.TryParse(TxtBlue.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte blue))
            {
                return false;
            }

            SelectedColor = MediaColor.FromRgb(red, green, blue);
            _hsv = ColorPickerColorMath.RgbToHsv(SelectedColor);
            UpdateVisuals(updateText: true);
            return true;
        }

        private bool TryApplyHexText()
        {
            if (!ColorPickerColorMath.TryParseHex(TxtHex.Text, out MediaColor color))
            {
                return false;
            }

            SelectedColor = color;
            _hsv = ColorPickerColorMath.RgbToHsv(color);
            UpdateVisuals(updateText: true);
            return true;
        }

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
                ColorPreview.Background = new MediaSolidColorBrush(SelectedColor);
                BrightnessOverlay.Opacity = 1 - _hsv.Value;
                BrightnessSlider.Value = _hsv.Value;
                BrightnessSlider.Background = CreateBrightnessBrush();
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

        private MediaLinearGradientBrush CreateBrightnessBrush()
        {
            MediaColor fullBrightness = ColorPickerColorMath.HsvToRgb(_hsv.Hue, _hsv.Saturation, 1);
            var brush = new MediaLinearGradientBrush
            {
                StartPoint = new WpfPoint(0.5, 0),
                EndPoint = new WpfPoint(0.5, 1)
            };
            brush.GradientStops.Add(new MediaGradientStop(fullBrightness, 0));
            brush.GradientStops.Add(new MediaGradientStop(MediaColors.Black, 1));
            brush.Freeze();
            return brush;
        }

        private void UpdateColorFieldMarker()
        {
            double width = ColorFieldMarkerCanvas.ActualWidth;
            double height = ColorFieldMarkerCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            WpfCanvas.SetLeft(ColorFieldMarker, (_hsv.Hue / 360 * width) - (ColorFieldMarker.Width / 2));
            WpfCanvas.SetTop(ColorFieldMarker, (_hsv.Saturation * height) - (ColorFieldMarker.Height / 2));
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

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            DialogResult = false;
        }
    }
}
