using System.ComponentModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace BASpark;

public sealed class SelectionGroup(string title, ItemsControl list, List<SelectionCardItem> items, bool first) : INotifyPropertyChanged
{
    public string Title { get; } = title;
    public ItemsControl List { get; } = list;
    public List<SelectionCardItem> Items { get; } = items;
    public bool IsFirst { get; } = first;
    public string TitleWithCount => FormatTitle(Title, Items);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshSelectionCount() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TitleWithCount)));
    internal static string FormatTitle(string title, IReadOnlyCollection<SelectionCardItem> items) =>
        $"{title} ({items.Count(item => item.IsSelected)}/{items.Count})";
}

public sealed class SelectionCardLayout : VirtualizingLayout
{
    private sealed class LayoutState
    {
        internal double Width = double.NaN;
        internal double TextScale;
        internal bool Dirty = true;
        internal double[] Offsets = [];
        internal readonly List<(double Offset, SelectionGroup Group)> Groups = [];
        internal readonly List<(int Index, UIElement Element)> Realized = [];
    }

    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = new();
    private readonly TextBlock _headingTitle = new();
    private Border? _measurementCard;
    private Grid? _measurementHeading;
    private LayoutState? _state;
    private double _measuredWidth = double.NaN;
    private double _measuredTextScale;
    private Dictionary<(string Title, string Subtitle), double> _cardHeights = [];

    protected override void InitializeForContextCore(VirtualizingLayoutContext context) => context.LayoutState = new LayoutState();

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
    {
        context.LayoutState = null;
        _state = null;
    }

    protected override void OnItemsChangedCore(VirtualizingLayoutContext context, object source, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        var state = (LayoutState)context.LayoutState;
        foreach (var entry in state.Realized) context.RecycleElement(entry.Element);
        state.Realized.Clear();
        state.Dirty = true;
        InvalidateMeasure();
    }

    internal SelectionGroup? GetActiveGroup(double offset) =>
        _state?.Groups.LastOrDefault(entry => entry.Offset <= offset + 1).Group;

    private static UIElement GetRowElement(VirtualizingLayoutContext context, int index)
    {
        UIElement element = context.GetOrCreateElementAt(index);
        if (element is FrameworkElement row)
        {
            object item = context.GetItemAt(index);
            if (!ReferenceEquals(row.DataContext, item)) row.DataContext = item;
        }
        return element;
    }

    private void InitializeMeasurementElements()
    {
        if (_measurementCard != null) return;
        _title.Style = (Style)Application.Current.Resources["BasSettingTitleStyle"];
        _subtitle.Style = (Style)Application.Current.Resources["BasCaptionStyle"];
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_title);
        text.Children.Add(_subtitle);
        var content = new Grid { ColumnSpacing = 16 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(text);
        var check = new CheckBox
        {
            Style = (Style)Application.Current.Resources["DefaultCheckBoxStyle"],
            MinWidth = 0, Padding = new Thickness(), VerticalAlignment = VerticalAlignment.Center
        };
        check.ApplyTemplate();
        Grid.SetColumn(check, 1);
        content.Children.Add(check);
        _measurementCard = new Border
        {
            Style = (Style)Application.Current.Resources["BasSettingCardStyle"], MinHeight = 0,
            Margin = new Thickness(0, 0, 16, 4), Child = content
        };
        _headingTitle.Style = (Style)Application.Current.Resources["BasSectionTitleStyle"];
        _measurementHeading = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 16, 12) };
        _measurementHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _measurementHeading.Children.Add(_headingTitle);
        for (int column = 1; column <= 2; column++)
        {
            _measurementHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var button = new Button { Width = 32, Height = 32, MinWidth = 0, Padding = new Thickness() };
            Grid.SetColumn(button, column);
            _measurementHeading.Children.Add(button);
        }
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        InitializeMeasurementElements();
        var state = (LayoutState)context.LayoutState;
        _state = state;
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : context.RealizationRect.Width;
        if (!double.IsFinite(width) || width <= 0) width = 384;
        double textScale = _uiSettings.TextScaleFactor;
        if (state.Dirty || state.Width != width || state.TextScale != textScale || state.Offsets.Length != context.ItemCount + 1)
        {
            if (_measuredWidth != width || _measuredTextScale != textScale) _cardHeights.Clear();
            var heights = new Dictionary<(string Title, string Subtitle), double>();
            state.Width = width;
            state.TextScale = textScale;
            state.Offsets = new double[context.ItemCount + 1];
            state.Groups.Clear();
            for (int index = 0; index < context.ItemCount; index++)
            {
                double height;
                if (context.GetItemAt(index) is SelectionGroup group)
                {
                    state.Groups.Add((state.Offsets[index], group));
                    _headingTitle.Text = $"{group.Title} ({group.Items.Count}/{group.Items.Count})";
                    _measurementHeading!.Measure(new Size(width, double.PositiveInfinity));
                    height = group.IsFirst ? 0 : _measurementHeading.DesiredSize.Height;
                }
                else
                {
                    var item = (SelectionCardItem)context.GetItemAt(index);
                    var key = (item.Title, item.Subtitle);
                    if (!_cardHeights.TryGetValue(key, out height))
                    {
                        _title.Text = item.Title;
                        _subtitle.Text = item.Subtitle;
                        _subtitle.Visibility = item.SubtitleVisibility;
                        _measurementCard!.Measure(new Size(width, double.PositiveInfinity));
                        height = _measurementCard.DesiredSize.Height;
                    }
                    heights[key] = height;
                }
                state.Offsets[index + 1] = state.Offsets[index] + height;
            }
            _cardHeights = heights;
            _measuredWidth = width;
            _measuredTextScale = textScale;
            state.Dirty = false;
        }
        state.Realized.Clear();
        Rect visible = context.RealizationRect;
        double top = double.IsFinite(visible.Top) ? Math.Max(0, visible.Top) : 0;
        double bottom = double.IsFinite(visible.Bottom) && visible.Height > 0 ? visible.Bottom : top + 506;
        for (int index = 0; index < context.ItemCount; index++)
        {
            if (state.Offsets[index + 1] < top) continue;
            if (state.Offsets[index] > bottom) break;
            UIElement element = GetRowElement(context, index);
            element.Measure(new Size(width, state.Offsets[index + 1] - state.Offsets[index]));
            state.Realized.Add((index, element));
        }
        int anchor = context.RecommendedAnchorIndex;
        if (anchor >= 0 && anchor < context.ItemCount && !state.Realized.Any(entry => entry.Index == anchor))
        {
            UIElement element = GetRowElement(context, anchor);
            element.Measure(new Size(width, state.Offsets[anchor + 1] - state.Offsets[anchor]));
            state.Realized.Add((anchor, element));
        }
        return new Size(width, state.Offsets[^1]);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        var state = (LayoutState)context.LayoutState;
        foreach (var entry in state.Realized)
            entry.Element.Arrange(new Rect(0, state.Offsets[entry.Index], finalSize.Width,
                state.Offsets[entry.Index + 1] - state.Offsets[entry.Index]));
        return finalSize;
    }
}

public sealed partial class ControlPanelWindow
{
    private sealed record SectionResetRequest(ResetScope Scope, string? GroupKey);
    private sealed record CardResetButton(Button Button, TextBlock Label, ResetScope Scope, string Key, Action Restore, Func<SettingsState, bool> IsDefault, bool IsGroup);
    private sealed record StickyHeading(FrameworkElement Anchor, TextBlock Label, object? Action, Func<bool> Visible);

    private sealed class ResetButtonAnimation(Button button) : IDisposable
    {
        private Storyboard? _storyboard;
        private bool _visible = button.Visibility == Visibility.Visible;

        internal void SetVisible(bool visible, bool animate)
        {
            if (_visible == visible) return;
            double opacity = button.Visibility == Visibility.Collapsed ? 0 : button.Opacity;
            _visible = visible;
            _storyboard?.Stop();
            _storyboard = null;
            button.ReleasePointerCaptures();
            button.IsHitTestVisible = visible;
            VisualStateManager.GoToState(button, "Normal", false);
            VisualStateManager.GoToState(button, "Unfocused", false);
            animate &= button.IsLoaded && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            for (DependencyObject? parent = VisualTreeHelper.GetParent(button); animate && parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is UIElement { Visibility: Visibility.Collapsed }) animate = false;
            if (!animate)
            {
                Complete();
                return;
            }
            button.Visibility = Visibility.Visible;
            button.Opacity = 1;
            var storyboard = new Storyboard();
            var duration = TimeSpan.Parse((string)Application.Current.Resources["ControlFastAnimationDuration"], System.Globalization.CultureInfo.InvariantCulture);
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = opacity });
            fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(duration), Value = visible ? 1 : 0 });
            Storyboard.SetTarget(fade, button);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
            _storyboard = storyboard;
            storyboard.Completed += (_, _) =>
            {
                if (!ReferenceEquals(_storyboard, storyboard)) return;
                Complete();
            };
            storyboard.Begin();
        }

        internal void Complete()
        {
            _storyboard?.Stop();
            _storyboard = null;
            button.Visibility = _visible ? Visibility.Visible : Visibility.Collapsed;
            button.Opacity = 1;
        }

        public void Dispose() => Complete();
    }

    private sealed class StickySectionTracker : IDisposable
    {
        private readonly ScrollViewer _scroll;
        private readonly FrameworkElement _content;
        private readonly Action<StickyHeading?, bool> _show;
        private readonly List<StickyHeading> _headings = [];
        private StickyHeading? _active;
        private bool _updatePending;
        private bool _refreshPending;
        private bool _disposed;

        internal StickySectionTracker(ScrollViewer scroll, FrameworkElement content, Action<StickyHeading?, bool> show)
        {
            _scroll = scroll;
            _content = content;
            _show = show;
            scroll.ViewChanged += ViewChanged;
            scroll.Loaded += Loaded;
            content.SizeChanged += SizeChanged;
        }

        internal void Add(StickyHeading heading)
        {
            _headings.Add(heading);
            heading.Anchor.Loaded += Heading_Loaded;
            RequestUpdate();
        }

        internal void Remove(FrameworkElement anchor)
        {
            anchor.Loaded -= Heading_Loaded;
            _headings.RemoveAll(heading => ReferenceEquals(heading.Anchor, anchor));
            RequestUpdate();
        }

        internal void Clear()
        {
            foreach (StickyHeading heading in _headings)
            {
                heading.Anchor.Loaded -= Heading_Loaded;
                heading.Anchor.Opacity = 1;
            }
            _headings.Clear();
            _active = null;
            _show(null, false);
        }

        internal void Update(bool refresh = false, bool animateResetButtons = false)
        {
            if (!_scroll.IsLoaded) return;
            StickyHeading? active = null;
            double nearest = double.NegativeInfinity;
            foreach (StickyHeading heading in _headings)
            {
                if (!heading.Anchor.IsLoaded || !heading.Visible()) continue;
                double top = heading.Anchor.TransformToVisual(_scroll).TransformPoint(new Point()).Y;
                if (active == null) active = heading;
                if (top <= 1 && top >= nearest)
                {
                    nearest = top;
                    active = heading;
                }
            }
            foreach (StickyHeading heading in _headings)
            {
                double opacity = heading.Anchor.Height == 0 || ReferenceEquals(heading, active) ? 0 : 1;
                if (heading.Anchor.Opacity != opacity) heading.Anchor.Opacity = opacity;
                heading.Anchor.IsHitTestVisible = opacity != 0;
            }
            if (!ReferenceEquals(active, _active) || refresh)
            {
                _active = active;
                _show(active, animateResetButtons);
            }
        }

        private void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) => Update();
        private void RequestUpdate(bool refresh = false)
        {
            _refreshPending |= refresh;
            if (_disposed || _updatePending) return;
            _updatePending = true;
            if (!_scroll.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    _updatePending = false;
                    bool requestedRefresh = _refreshPending;
                    _refreshPending = false;
                    if (!_disposed) Update(requestedRefresh);
                })) _updatePending = false;
        }

        private void Loaded(object sender, RoutedEventArgs args) => RequestUpdate(true);
        private void Heading_Loaded(object sender, RoutedEventArgs args) => RequestUpdate(true);
        private void SizeChanged(object sender, SizeChangedEventArgs args) => RequestUpdate();

        public void Dispose()
        {
            _disposed = true;
            _scroll.ViewChanged -= ViewChanged;
            _scroll.Loaded -= Loaded;
            _content.SizeChanged -= SizeChanged;
            foreach (StickyHeading heading in _headings) heading.Anchor.Loaded -= Heading_Loaded;
            _headings.Clear();
        }
    }

    private readonly List<StickySectionTracker> _stickyTrackers = [];
    private readonly Dictionary<ItemsControl, ScrollViewer> _selectionScrollViewers = new();
    private readonly Dictionary<Grid, (SelectionGroup Group, SelectionToolbar Toolbar)> _groupSelectionToolbars = new();
    private readonly Dictionary<ItemsControl, SelectionToolbar> _pinnedSelectionToolbars = new();
    private StickySectionTracker? _settingsHeaders;
    private StickySectionTracker? _aboutHeaders;
    private readonly List<CardResetButton> _cardResetButtons = [];
    private readonly Dictionary<Button, ResetButtonAnimation> _resetButtonAnimations = [];
    private readonly Dictionary<TextBlock, TranslateTransform> _rowTextTransforms = [];
    private readonly Dictionary<(string Family, double Size, ushort Weight, string Language, double Scale, bool Cjk), double> _rowTextOffsets = [];
    private readonly Windows.UI.ViewManagement.UISettings _rowTextUiSettings = new();
    private readonly List<(Button Button, TextBlock Label, SectionResetRequest Request)> _sectionResetButtons = [];
    private SettingsState? _resetValueState;
    private readonly Dictionary<string, JsonElement> _resetValues = new(StringComparer.Ordinal);

    private SettingsSections CurrentSettingsPage => _selectedSettingsItem == SubTabVisual ? SettingsSections.Visual
        : _selectedSettingsItem == SubTabFilter ? SettingsSections.Filter
        : _selectedSettingsItem == SubTabMultiScreen ? SettingsSections.Screens
        : _selectedSettingsItem == SubTabBackup ? 0 : SettingsSections.Basic;

    private void SetupPageHeaders()
    {
        _settingsHeaders = new(SettingsContentScrollViewer, SettingsContent, (heading, animateResetButtons) =>
        {
            TxtActiveSettingsSection.Text = heading?.Label.Text ?? string.Empty;
            if (!Equals(BtnResetSection.Tag, heading?.Action) && _resetButtonAnimations.TryGetValue(BtnResetSection, out var animation)) animation.Complete();
            BtnResetSection.Tag = heading?.Action;
            SetResetButtonVisibility(BtnResetSection, heading?.Action is SectionResetRequest request && HasNonDefaultSettings(request), animateResetButtons);
        });
        _stickyTrackers.Add(_settingsHeaders);
        foreach (var section in new[]
                 {
                     (TxtBasicTitle, SectionBasic, ResetScope.Basic, "Basic_Interface", true),
                     (TxtBasicTriggerTitle, SectionBasic, ResetScope.Basic, "Basic_Trigger", false),
                     (TxtBasicRuntimeTitle, SectionBasic, ResetScope.Basic, "Basic_Runtime", false),
                     (TxtVisualTitle, SectionVisual, ResetScope.Visual, "Visual_ScaleSection", true),
                     (TxtVisualSpeedTitle, SectionVisual, ResetScope.Visual, "Visual_SpeedSection", false),
                     (TxtVisualSamplingTitle, SectionVisual, ResetScope.Visual, "Visual_SamplingSection", false),
                     (TxtVisualOtherTitle, SectionVisual, ResetScope.Visual, "Visual_OtherSection", false),
                     (TxtVisualAdvancedTitle, SectionVisual, ResetScope.Visual, "Visual_AdvancedSection", false),
                     (TxtFilterTitle, SectionFilter, ResetScope.Filter, "Filter_BasicSection", true),
                     (TxtFilterAdvancedTitle, SectionFilter, ResetScope.Filter, "Filter_AdvancedSection", false),
                     (TxtScreensTitle, SectionMultiScreen, ResetScope.Screens, (string?)null, true),
                     (TxtBackupTitle, SectionBackup, ResetScope.All, "Backup_BackupSection", true),
                     (TxtBackupRestoreTitle, SectionBackup, ResetScope.All, "Backup_RestoreSection", false)
                 })
        {
            var request = section.Item2 == SectionBackup ? null : new SectionResetRequest(section.Item3, section.Item4);
            StackPanel parent = section.Item1 == TxtFilterAdvancedTitle ? PanelEnvironmentFilter : section.Item2;
            FrameworkElement anchor = WrapPageHeading(section.Item1, parent, request, section.Item5);
            StackPanel page = section.Item2;
            _settingsHeaders.Add(new(anchor, section.Item1, request, () => page.Visibility == Visibility.Visible &&
                (section.Item1 != TxtFilterAdvancedTitle || EnvironmentFilterList.IsExpanded)));
        }
        _aboutHeaders = new(AboutScrollViewer, AboutContent,
            (heading, _) => TxtActiveAboutSection.Text = heading?.Label.Text ?? string.Empty);
        _stickyTrackers.Add(_aboutHeaders);
        foreach (TextBlock label in new[] { TxtBuildInfoTitle, TxtRepositoryLinks, TxtAuthorsTitle })
            _aboutHeaders.Add(new(WrapPageHeading(label, AboutContent, null, label == TxtBuildInfoTitle), label, null,
                () => PageAbout.Visibility == Visibility.Visible));
        SetupSelectionHeader(ListVisualResetItems, ResetItemsScrollViewer, BtnResetGroupInvert, BtnResetGroupSelectAll);
        SetupSelectionHeader(ListBackupItems, BackupItemsScrollViewer, BtnBackupGroupInvert, BtnBackupGroupSelectAll);
        RefreshPageHeaders();
    }

    private FrameworkElement WrapPageHeading(TextBlock label, StackPanel parent, SectionResetRequest? request, bool first)
    {
        int index = parent.Children.IndexOf(label);
        Thickness margin = label.Margin;
        parent.Children.RemoveAt(index);
        label.Margin = new Thickness();
        label.VerticalAlignment = VerticalAlignment.Center;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(label);
        if (request != null)
        {
            var button = new Button { Tag = request,
                Content = new FontIcon { Glyph = "\uE777", FontSize = 16 } };
            ConfigureResetButton(button, label);
            button.Click += ResetSection_Click;
            content.Children.Add(button);
            _sectionResetButtons.Add((button, label, request));
        }
        var anchor = new Grid { Margin = first ? new Thickness() : margin, HorizontalAlignment = HorizontalAlignment.Left,
            Opacity = first ? 0 : 1, IsHitTestVisible = !first };
        anchor.Children.Add(content);
        if (first) anchor.Height = 0;
        parent.Children.Insert(index, anchor);
        return anchor;
    }

    private void RefreshPageHeaders(bool animateResetButtons = false)
    {
        if (_settingsHeaders == null) return;
        SettingsSections page = CurrentSettingsPage;
        SetResetButtonVisibility(BtnBasicReset, page == SettingsSections.Basic && HasNonDefaultSettings(new(ResetScope.Basic, null)), animateResetButtons);
        SetResetButtonVisibility(BtnVisualReset, page == SettingsSections.Visual && HasNonDefaultSettings(new(ResetScope.Visual, null)), animateResetButtons);
        SetResetButtonVisibility(BtnFilterReset, page == SettingsSections.Filter && HasNonDefaultSettings(new(ResetScope.Filter, null)), animateResetButtons);
        BtnRefreshScreens.Visibility = page == SettingsSections.Screens ? Visibility.Visible : Visibility.Collapsed;
        foreach (Button button in new[] { BtnResetSection }.Concat(_sectionResetButtons.Select(entry => entry.Button)))
        {
            string label = Localization.Get("Settings_ResetSection");
            SetResetButtonLabel(button, label);
        }
        foreach (Button button in new[] { BtnBasicReset, BtnVisualReset, BtnFilterReset })
        {
            string label = Localization.Get("Settings_ResetPage");
            SetResetButtonLabel(button, label);
        }
        _settingsHeaders.Update(true, animateResetButtons);
        _aboutHeaders?.Update(true);
    }

    private void ConfigureResetButton(Button button, TextBlock label, bool standardSize = false)
    {
        AlignRowText(label);
        button.Style = (Style)Application.Current.Resources["BasInlineResetButtonStyle"];
        var reference = new TextBlock { Style = (Style)Application.Current.Resources["BasSettingsPageTitleStyle"],
            IsTextScaleFactorEnabled = false };
        void UpdateSize()
        {
            double scale = standardSize ? 1 : label.FontSize / reference.FontSize;
            button.Height = button.Width = 32 * scale;
            button.Padding = new Thickness(4 * scale);
            if (button.Content is FontIcon icon)
            {
                icon.IsTextScaleFactorEnabled = false;
                icon.FontSize = 16 * scale;
            }
        }
        UpdateSize();
        label.SizeChanged += (_, _) => UpdateSize();
        button.VerticalAlignment = VerticalAlignment.Center;
        ConfigureResetButtonAnimations(button);
    }

    private void SetupRowTextAlignment()
    {
        foreach (TextBlock label in SettingElements((UIElement)Content).OfType<TextBlock>())
            if (label.Style != null && new[] { "BasSettingsPageTitleStyle", "BasSectionTitleStyle", "BasSettingsSectionTitleStyle",
                    "BasSettingTitleStyle", "BasCaptionStyle", "BasAdvancedGroupTitleStyle", "BasAdvancedSectionTitleStyle", "BasPageSubtitleStyle" }
                .Any(key => label.Style == (Style)Application.Current.Resources[key])) AlignRowText(label);
    }

    private void AlignRowText(TextBlock label)
    {
        if (!_rowTextTransforms.TryGetValue(label, out TranslateTransform? transform))
        {
            transform = new TranslateTransform();
            label.RenderTransform = transform;
            label.VerticalAlignment = VerticalAlignment.Center;
            _rowTextTransforms.Add(label, transform);
            label.SizeChanged += RowText_SizeChanged;
        }
        bool cjk = label.Text.Any(character => character is >= '\u2E80' and <= '\u9FFF');
        double scale = label.IsTextScaleFactorEnabled ? _rowTextUiSettings.TextScaleFactor : 1;
        var key = (label.FontFamily.Source, label.FontSize, label.FontWeight.Weight, label.Language, scale, cjk);
        if (!_rowTextOffsets.TryGetValue(key, out double offset))
        {
            var measurement = new TextBlock { Text = cjk ? "设" : "Ag", FontFamily = label.FontFamily, FontSize = label.FontSize,
                FontWeight = label.FontWeight, FontStyle = label.FontStyle, FontStretch = label.FontStretch,
                Language = label.Language, IsTextScaleFactorEnabled = label.IsTextScaleFactorEnabled,
                TextLineBounds = TextLineBounds.TrimToCapHeight };
            measurement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double capHeight = measurement.DesiredSize.Height;
            measurement.TextLineBounds = TextLineBounds.TrimToBaseline;
            measurement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            offset = (capHeight - measurement.DesiredSize.Height) / 2;
            _rowTextOffsets[key] = offset;
        }
        transform.Y = offset;
    }

    private void RowText_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!_isClosed && sender is TextBlock label) AlignRowText(label);
    }

    private void ConfigureResetButtonAnimations(Button button)
    {
        _resetButtonAnimations.TryAdd(button, new(button));
    }

    private void SetResetButtonVisibility(Button button, bool visible, bool animate = false)
    {
        if (_resetButtonAnimations.TryGetValue(button, out ResetButtonAnimation? animation)) animation.SetVisible(visible, animate);
        else button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetResetButtonLabel(Button button, string label, string? name = null)
    {
        if (ToolTipService.GetToolTip(button) as string != label) ToolTipService.SetToolTip(button, label);
        name ??= label;
        if (AutomationProperties.GetName(button) != name) AutomationProperties.SetName(button, name);
    }

    private List<VisualResetItem> CreateCardResetItems(ResetScope scope)
    {
        var items = new List<VisualResetItem>();
        ResetScope previousScope = _resetScope;
        ICollection<VisualResetItem>? previousTarget = _resetItemTarget;
        _resetScope = scope;
        _resetItemTarget = items;
        try
        {
            if (scope == ResetScope.Basic) RebuildBasicResetItems();
            if (scope == ResetScope.Visual) RebuildVisualResetItems();
            if (scope == ResetScope.Filter) RebuildFilterResetItems();
            return items;
        }
        finally
        {
            _resetScope = previousScope;
            _resetItemTarget = previousTarget;
        }
    }

    private void SetupCardResetButtons()
    {
        foreach (ResetScope scope in new[] { ResetScope.Basic, ResetScope.Visual, ResetScope.Filter })
        {
            Dictionary<string, VisualResetItem> items = CreateCardResetItems(scope).ToDictionary(item => item.SettingKey!);
            foreach (var entry in new (FrameworkElement Control, string Key)[]
            {
                (ComboLanguage, "UiLanguage"), (RadioDarkMode, "DarkMode"), (CheckAlwaysTrailEffectSwitch, "EnableAlwaysTrailEffect"),
                (CheckMasterSwitch, "IsEffectEnabled"), (RadioClickType, "ClickTriggerType"), (CheckMiddleClickTrigger, "EnableMiddleClickTrigger"),
                (CheckTouchscreenMode, "IsTouchscreenMode"), (SliderMaxTouchEffects, "MaxTouchEffects"),
                (CheckScreenshotCompatibilityMode, "ScreenshotCompatibilityMode"), (CheckAutoStart, "AutoStart"),
                (CheckStartSilent, "StartSilent"), (CheckHideTrayIcon, "HideTrayIcon"), (CheckRunAsAdmin, "RunAsAdmin"),
                (CheckLinkedEffectScale, "UseLinkedEffectScale"), (SliderScale, "EffectScale"), (SliderTrailScale, "TrailEffectScale"),
                (SliderClickScale, "ClickEffectScale"), (CheckLinkedAnimationSpeed, "UseLinkedAnimationSpeed"),
                (SliderSpeed, "EffectSpeed"), (SliderTrailAnimSpeed, "TrailAnimationSpeed"), (SliderClickAnimSpeed, "ClickAnimationSpeed"),
                (CheckApplyCurveDraw, "ApplyCurveDraw"), (SliderTrailSamplingRate, "InputSamplingRate"), (SliderGlow, "GlowIntensity"),
                (CheckBaClickAdvancedSettings, "EnableBaClickAdvancedSettings"), (CheckEnvironmentFilter, "EnableEnvironmentFilter"),
                (CheckHideInFullscreen, "HideInFullscreen"), (CheckShowEffectOnDesktop, "ShowEffectOnDesktop"),
                (CheckShowEffectOnApp, "ShowEffectOnApp"), (ComboProfiles, "Profiles")
            })
            {
                if (!items.TryGetValue(entry.Key, out VisualResetItem? item)) continue;
                var heading = FindSettingCardHeading(entry.Control);
                if (heading != null) AddCardResetButton(heading.Value.Label, heading.Value.Parent, scope, entry.Key, item.Restore!,
                    state => entry.Key == "Profiles" ? AreProfilesDefault(state) :
                        item.DefaultValue is JsonElement defaultValue && IsDefaultSettingValue(state, entry.Key, defaultValue));
            }
            if (scope == ResetScope.Visual)
            {
                VisualResetItem color = items["ParticleColor"];
                AddCardResetButton(TxtEffectColor, (Panel)EffectColorExpander.Header, scope, "ParticleColor", color.Restore!,
                    state => IsDefaultSettingValue(state, "ParticleColor", color.DefaultValue!.Value));
            }
        }
        if (FindSettingCardHeading(ComboProcessFilterMode) is { } modeHeading)
            AddCardResetButton(modeHeading.Label, modeHeading.Parent, ResetScope.Filter, "Profile.Mode", () => SelectProcessFilterMode(ProcessFilterModeOption.Blacklist),
                state => GetPendingProfile(state)?.GetProperty("Mode").GetInt32() == (int)ProcessFilterModeOption.Blacklist);
        var processHeading = FindSettingCardHeading(ListConfiguredProcesses)!.Value;
        AddCardResetButton(TxtProcessList, processHeading.Parent, ResetScope.Filter, "Profile.Processes", () =>
        {
            if (ComboProfiles.SelectedItem is not FilterProfile profile) return;
            profile.Processes.Clear();
            RefreshCurrentProfileProcesses(profile);
        }, state => GetPendingProfile(state)?.GetProperty("Processes").GetArrayLength() == 0);
        foreach (Button button in new[] { BtnBasicReset, BtnVisualReset, BtnFilterReset }) ConfigureResetButton(button, TxtSettingsTitle, standardSize: true);
        ConfigureResetButton(BtnResetSection, TxtActiveSettingsSection);
        RefreshResetButtonVisibility();
    }

    private (TextBlock Label, Panel Parent)? FindSettingCardHeading(FrameworkElement control)
    {
        foreach (Border card in SettingElements(SettingsContent).OfType<Border>())
            if (card.Style == (Style)Application.Current.Resources["BasSettingCardStyle"] && SettingElements(card).Contains(control))
                return FindCardHeading(card.Child);
        return null;
    }

    private static IEnumerable<UIElement> SettingElements(UIElement content)
    {
        yield return content;
        IEnumerable<UIElement> children = content switch
        {
            Panel panel => panel.Children,
            Border { Child: not null } border => [border.Child],
            Expander expander => new[] { expander.Header, expander.Content }.OfType<UIElement>(),
            ContentControl { Content: UIElement child } => [child],
            _ => []
        };
        foreach (UIElement child in children)
            foreach (UIElement element in SettingElements(child)) yield return element;
    }

    private static (TextBlock Label, Panel Parent)? FindCardHeading(UIElement content)
    {
        if (content is Border border) return FindCardHeading(border.Child);
        if (content is Panel panel)
            foreach (UIElement child in panel.Children)
            {
                if (child is TextBlock title) return (title, panel);
                if (FindCardHeading(child) is { } heading) return heading;
            }
        return null;
    }

    private void AddCardResetButton(TextBlock label, Panel panel, ResetScope scope, string key, Action restore, Func<SettingsState, bool> isDefault, bool isGroup = false)
    {
        int index = panel.Children.IndexOf(label);
        int column = Grid.GetColumn(label);
        int row = Grid.GetRow(label);
        Thickness margin = label.Margin;
        panel.Children.RemoveAt(index);
        label.Margin = new Thickness();
        var heading = new Grid { ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = margin };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(label);
        var button = new Button { Visibility = Visibility.Collapsed, Content = new FontIcon { Glyph = "\uE777", FontSize = 16 } };
        ConfigureResetButton(button, label);
        var request = new CardResetButton(button, label, scope, key, restore, isDefault, isGroup);
        button.Tag = request;
        button.Click += CardReset_Click;
        Grid.SetColumn(button, 1);
        heading.Children.Add(button);
        Grid.SetColumn(heading, column);
        Grid.SetRow(heading, row);
        panel.Children.Insert(index, heading);
        _cardResetButtons.Add(request);
    }

    private void CardReset_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: CardResetButton request } || _isApplyingSettings) return;
        Focus(FocusState.Programmatic);
        RestorePendingSettings(new[] { request.Restore });
    }

    private Dictionary<string, JsonElement> GetResetValues(SettingsState state)
    {
        if (ReferenceEquals(_resetValueState, state)) return _resetValues;
        _resetValues.Clear();
        foreach (string content in new[] { state.Basic, state.Visual, state.Filter })
        {
            using JsonDocument document = JsonDocument.Parse(content);
            foreach (JsonProperty property in document.RootElement.EnumerateObject()) _resetValues[property.Name] = property.Value.Clone();
        }
        _resetValueState = state;
        return _resetValues;
    }

    private bool IsDefaultSettingValue(SettingsState state, string key, JsonElement defaultValue)
    {
        Dictionary<string, JsonElement> values = GetResetValues(state);
        if (!values.TryGetValue(key, out JsonElement value)) return false;
        if (key == "ParticleColor")
            return string.Equals(ColorPickerColorMath.CombineThemeColor(value.GetString()!, values["EffectOpacity"].GetDouble()),
                defaultValue.GetString(), StringComparison.OrdinalIgnoreCase);
        return SettingValuesEqual(value, defaultValue);
    }

    internal static bool SettingValuesEqual(JsonElement value, JsonElement defaultValue) =>
        value.ValueKind == defaultValue.ValueKind && (value.ValueKind == JsonValueKind.Number
            ? Math.Abs(value.GetDouble() - defaultValue.GetDouble()) < 0.000000001
            : value.ValueKind == JsonValueKind.String ? value.GetString() == defaultValue.GetString() : value.GetRawText() == defaultValue.GetRawText());

    private JsonElement? GetPendingProfile(SettingsState state)
    {
        Dictionary<string, JsonElement> values = GetResetValues(state);
        if (!values.TryGetValue("Profiles", out JsonElement profiles)) return null;
        string? activeId = values["ActiveProfileId"].GetString();
        foreach (JsonElement profile in profiles.EnumerateArray())
            if (profile.GetProperty("Id").GetString() == activeId) return profile;
        return null;
    }

    private bool AreProfilesDefault(SettingsState state)
    {
        Dictionary<string, JsonElement> values = GetResetValues(state);
        if (!values.TryGetValue("Profiles", out JsonElement profiles) || profiles.GetArrayLength() != 1) return false;
        JsonElement profile = profiles[0];
        return profile.GetProperty("Name").GetString() == Localization.Get("Profile_Default") &&
            profile.GetProperty("Mode").GetInt32() == (int)ProcessFilterModeOption.Blacklist && profile.GetProperty("Processes").GetArrayLength() == 0;
    }

    private static string GetResetSectionKey(CardResetButton entry) => entry.Scope switch
    {
        ResetScope.Basic => entry.Key is "UiLanguage" or "DarkMode" ? "Basic_Interface"
            : entry.Key is "AutoStart" or "StartSilent" or "HideTrayIcon" or "RunAsAdmin" ? "Basic_Runtime" : "Basic_Trigger",
        ResetScope.Filter => entry.Key is "EnableEnvironmentFilter" or "HideInFullscreen" or "ShowEffectOnDesktop" or "ShowEffectOnApp" ? "Filter_BasicSection" : "Filter_AdvancedSection",
        ResetScope.Visual => entry.Key.StartsWith("BaClickFx/", StringComparison.Ordinal) || entry.Key is "EnableBaClickAdvancedSettings" or "HdrPreset" ? "Visual_AdvancedSection"
            : entry.Key.Contains("Scale", StringComparison.Ordinal) ? "Visual_ScaleSection"
            : entry.Key.Contains("Speed", StringComparison.Ordinal) ? "Visual_SpeedSection"
            : entry.Key is "ApplyCurveDraw" or "InputSamplingRate" ? "Visual_SamplingSection" : "Visual_OtherSection",
        _ => "MultiScreen_Displays"
    };

    private bool HasNonDefaultSettings(SectionResetRequest request) => _currentSettingsState != null && _cardResetButtons.Any(entry => entry.Scope == request.Scope &&
        (request.GroupKey == null || GetResetSectionKey(entry) == request.GroupKey) && !entry.IsDefault(_currentSettingsState));

    private void RefreshResetButtonVisibility(bool animate = false)
    {
        if (_currentSettingsState == null) return;
        foreach (CardResetButton entry in _cardResetButtons)
        {
            SetResetButtonVisibility(entry.Button, !entry.IsDefault(_currentSettingsState), animate);
            entry.Button.IsEnabled = !_isApplyingSettings;
            string label = Localization.Get(entry.IsGroup ? "Settings_ResetSection" : "Settings_ResetItem");
            SetResetButtonLabel(entry.Button, label, $"{label}: {entry.Label.Text}");
        }
        foreach (var entry in _sectionResetButtons)
            SetResetButtonVisibility(entry.Button, HasNonDefaultSettings(entry.Request), animate);
        RefreshPageHeaders(animate);
    }

    private void ResetSection_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: SectionResetRequest request } || _isApplyingSettings) return;
        Focus(FocusState.Programmatic);
        _resetScope = request.Scope;
        VisualResetItems.Clear();
        if (request.Scope == ResetScope.Basic) RebuildBasicResetItems();
        if (request.Scope == ResetScope.Visual) RebuildVisualResetItems();
        if (request.Scope == ResetScope.Filter) RebuildFilterResetItems();
        if (request.Scope == ResetScope.Screens) RebuildScreenResetItems();
        string? group = request.GroupKey == null ? null : Localization.Get(request.GroupKey);
        RestorePendingSettings(VisualResetItems.Where(item => group == null || item.Group == group));
    }

    private void RestorePendingSettings(IEnumerable<VisualResetItem> items) => RestorePendingSettings(items.Select(item => item.Restore).OfType<Action>());

    private void RestorePendingSettings(IEnumerable<Action> restores)
    {
        _isLoading = _suppressValueSync = true;
        try
        {
            foreach (Action restore in restores) restore();
            SyncSliderAndBoxValues();
        }
        finally { _isLoading = _suppressValueSync = false; }
        UpdateDependentSettingsVisibility(animate: true);
        UpdateAdvancedSettingsVisibility();
        UpdateAdvancedHdrControls();
        UpdateApplySettingsState(SettingsSections.All, animateResetButtons: true);
    }

    private void DiscardSettings_Click(object sender, RoutedEventArgs args)
    {
        if (_savedSettingsState == null || _isApplyingSettings) return;
        Focus(FocusState.Programmatic);
        ApplyBackupValuesToPanel(_savedSettingsState.GetValues(CurrentSettingsPage), animate: true);
        UpdateAdvancedSettingsVisibility();
        UpdateAdvancedHdrControls();
        UpdateApplySettingsState(SettingsSections.All, animateResetButtons: true);
    }

    private void ApplyPageRuntimeSettings(SettingsSections page, bool screensChanged)
    {
        if (page == SettingsSections.Basic)
        {
            App.Tray?.SetHidden(ConfigManager.HideTrayIcon);
            App.Overlay?.UpdateTouchMode(ConfigManager.IsTouchscreenMode);
            App.Overlay?.UpdateScreenshotCompatibilityMode(ConfigManager.ScreenshotCompatibilityMode);
            App.Overlay?.RefreshEnvironmentFilterState();
            ApplyAutoStartSettings(useSavedSettings: true);
            ApplyDarkMode();
        }
        if (page == SettingsSections.Visual)
        {
            ConfigManager.GetEffectScalesForOverlay(out double trailScale, out double clickScale);
            ConfigManager.GetAnimationSpeedsForOverlay(out double trailSpeed, out double clickSpeed);
            App.Overlay?.UpdateColor(ConfigManager.ParticleColor);
            App.Overlay?.UpdateEffectSettings(trailScale, clickScale, ConfigManager.EffectOpacity, trailSpeed, clickSpeed, ConfigManager.GlowIntensity);
            App.Overlay?.UpdateInputSamplingRate(ConfigManager.InputSamplingRate);
            App.Overlay?.SetCurveDraw(ConfigManager.ApplyCurveDraw);
        }
        if (page == SettingsSections.Filter) App.Overlay?.RefreshEnvironmentFilterState();
        if (page == SettingsSections.Screens && screensChanged) App.Overlay?.RefreshScreenSelection();
    }

    private void ReloadScreenLabels()
    {
        Dictionary<string, object> pending = CaptureSettingsState().GetValues(SettingsSections.Screens);
        LoadScreenOptions();
        ApplyBackupValuesToPanel(pending);
    }

    private void SetupSelectionHeader(ItemsControl list, ScrollViewer scroll, Button invert, Button selectAll)
    {
        SelectionToolbar global = _selectionToolbars[list];
        var toolbar = new SelectionToolbar(invert, selectAll, global.Confirm, global.EmptyMessage, []);
        InitializeSelectionToolbar(toolbar);
        _pinnedSelectionToolbars[list] = toolbar;
        _selectionScrollViewers[list] = scroll;
        scroll.ViewChanged += (_, _) => UpdateSelectionHeader(list);
        list.SizeChanged += (_, _) => UpdateSelectionHeader(list);
    }

    private void UpdateSelectionHeader(ItemsControl list)
    {
        if (FindVisualDescendant<ItemsRepeater>(list)?.Layout is SelectionCardLayout layout)
            ShowSelectionGroup(list, layout.GetActiveGroup(_selectionScrollViewers[list].VerticalOffset));
    }

    private void SetGroupedSelectionRows(ItemsControl list, IEnumerable<SelectionCardItem> items, Func<SelectionCardItem, string> groupName)
    {
        var rows = new List<object>();
        foreach (var group in items.GroupBy(groupName))
        {
            var heading = new SelectionGroup(group.Key, list, group.ToList(), rows.Count == 0);
            rows.Add(heading);
            rows.AddRange(heading.Items);
        }
        list.ItemsSource = rows;
        ShowSelectionGroup(list, rows.OfType<SelectionGroup>().FirstOrDefault());
        RefreshSelectionToolbar(list);
    }

    private void SelectionElement_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not FrameworkElement element) return;
        element.DataContext = sender.ItemsSourceView.GetAt(args.Index);
        UpdateSelectionCardBinding(element);
        bool first = element.DataContext is SelectionGroup { IsFirst: true };
        element.Margin = element.DataContext is SelectionGroup
            ? first ? new Thickness() : new Thickness(0, 8, 16, 12)
            : new Thickness(0, 0, 16, 4);
        element.Height = first ? 0 : double.NaN;
        element.Opacity = first ? 0 : 1;
        element.IsHitTestVisible = !first;
    }

    private void SelectionCard_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement card)
        {
            UpdateSelectionCardBinding(card);
            foreach (TextBlock label in SettingElements(card).OfType<TextBlock>()) AlignRowText(label);
        }
    }

    private void SelectionCard_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => UpdateSelectionCardBinding(sender);

    private static void UpdateSelectionCardBinding(FrameworkElement card)
    {
        if (card.FindName("SelectionCardCheck") is not CheckBox check) return;
        if (card.DataContext is SelectionCardItem item)
        {
            if (ReferenceEquals(check.GetBindingExpression(CheckBox.IsCheckedProperty)?.ParentBinding.Source, item)) return;
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding
            {
                Source = item,
                Path = new PropertyPath(nameof(SelectionCardItem.IsSelected)),
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        }
        else check.ClearValue(CheckBox.IsCheckedProperty);
    }

    private void SelectionHeading_Loaded(object sender, RoutedEventArgs args)
    {
        InitializeSelectionHeading(sender);
        if (sender is Grid header)
            foreach (TextBlock label in header.Children.OfType<TextBlock>()) AlignRowText(label);
    }
    private void SelectionHeading_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => InitializeSelectionHeading(sender);

    private void InitializeSelectionHeading(object sender)
    {
        if (sender is not Grid { DataContext: SelectionGroup group } header) return;
        header.Height = group.IsFirst ? 0 : double.NaN;
        header.Margin = group.IsFirst ? new Thickness() : new Thickness(0, 8, 16, 12);
        if (_groupSelectionToolbars.TryGetValue(header, out var existing) && ReferenceEquals(existing.Group, group)) return;
        Button[] buttons = header.Children.OfType<Button>().ToArray();
        SelectionToolbar global = _selectionToolbars[group.List];
        var toolbar = new SelectionToolbar(buttons[0], buttons[1], global.Confirm, global.EmptyMessage, group.Items);
        toolbar.Invert.Tag = toolbar.SelectAll.Tag = group;
        InitializeSelectionToolbar(toolbar);
        _groupSelectionToolbars[header] = (group, toolbar);
        UpdateSelectionButtons(toolbar, group.List != ListBackupItems || !_backupBusy);
        UpdateSelectionHeadingVisibility(group.List);
    }

    private void SelectionElement_Clearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        Grid? header = args.Element as Grid ?? FindVisualDescendant<Grid>(args.Element, "SelectionHeading");
        if (header != null) _groupSelectionToolbars.Remove(header);
        if (args.Element is FrameworkElement element)
        {
            if (element.FindName("SelectionCardCheck") is CheckBox check) check.ClearValue(CheckBox.IsCheckedProperty);
            element.DataContext = null;
        }
    }

    private void SelectionHeading_Unloaded(object sender, RoutedEventArgs args)
    {
        if (sender is Grid header) _groupSelectionToolbars.Remove(header);
    }

    private void UpdateSelectionHeadingVisibility(ItemsControl list)
    {
        SelectionGroup? active = _pinnedSelectionToolbars[list].Invert.Tag as SelectionGroup;
        foreach (var entry in _groupSelectionToolbars)
        {
            if (entry.Value.Group.List != list) continue;
            bool visible = !entry.Value.Group.IsFirst && !ReferenceEquals(active, entry.Value.Group);
            entry.Key.Opacity = visible ? 1 : 0;
            entry.Key.IsHitTestVisible = visible;
        }
    }

    private void ShowSelectionGroup(ItemsControl list, SelectionGroup? group)
    {
        Grid header = list == ListVisualResetItems ? ResetGroupHeader : BackupGroupHeader;
        TextBlock title = list == ListVisualResetItems ? TxtResetGroup : TxtBackupGroup;
        SelectionToolbar toolbar = _pinnedSelectionToolbars[list];
        title.Text = group?.TitleWithCount ?? string.Empty;
        if (ReferenceEquals(toolbar.Invert.Tag, group)) return;
        header.Visibility = group == null ? Visibility.Collapsed : Visibility.Visible;
        toolbar.Items.Clear();
        if (group != null) toolbar.Items.AddRange(group.Items);
        toolbar.Invert.Tag = toolbar.SelectAll.Tag = group;
        UpdateSelectionButtons(toolbar, list != ListBackupItems || !_backupBusy);
        UpdateSelectionHeadingVisibility(list);
    }

    private void UpdateSectionSelectionToolbars(ItemsControl list)
    {
        bool available = list != ListBackupItems || !_backupBusy;
        foreach (SelectionGroup group in list.Items.OfType<SelectionGroup>()) group.RefreshSelectionCount();
        foreach (var entry in _groupSelectionToolbars.Values)
            if (entry.Group.List == list) UpdateSelectionButtons(entry.Toolbar, available);
        if (_pinnedSelectionToolbars.TryGetValue(list, out SelectionToolbar? toolbar))
        {
            UpdateSelectionButtons(toolbar, available);
            TextBlock title = list == ListVisualResetItems ? TxtResetGroup : TxtBackupGroup;
            title.Text = (toolbar.Invert.Tag as SelectionGroup)?.TitleWithCount ?? string.Empty;
        }
    }
}
