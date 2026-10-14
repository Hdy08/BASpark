using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace BASpark;

public sealed class SelectionGroup(string title, ItemsControl list, List<SelectionCardItem> items, bool first)
{
    public string Title { get; } = title;
    public ItemsControl List { get; } = list;
    public List<SelectionCardItem> Items { get; } = items;
    public bool IsFirst { get; } = first;
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
        ((LayoutState)context.LayoutState).Dirty = true;
        InvalidateMeasure();
    }

    internal SelectionGroup? GetActiveGroup(double offset) =>
        _state?.Groups.LastOrDefault(entry => entry.Offset <= offset + 1).Group;

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
        var check = new CheckBox { MinWidth = 0, Padding = new Thickness(), VerticalAlignment = VerticalAlignment.Center };
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
                    _headingTitle.Text = group.Title;
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
            UIElement element = context.GetOrCreateElementAt(index);
            element.Measure(new Size(width, state.Offsets[index + 1] - state.Offsets[index]));
            state.Realized.Add((index, element));
        }
        int anchor = context.RecommendedAnchorIndex;
        if (anchor >= 0 && anchor < context.ItemCount && !state.Realized.Any(entry => entry.Index == anchor))
        {
            UIElement element = context.GetOrCreateElementAt(anchor);
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
    private sealed record StickyHeading(FrameworkElement Anchor, TextBlock Label, object? Action, Func<bool> Visible);

    private sealed class StickySectionTracker : IDisposable
    {
        private readonly ScrollViewer _scroll;
        private readonly FrameworkElement _content;
        private readonly Action<StickyHeading?> _show;
        private readonly List<StickyHeading> _headings = [];
        private StickyHeading? _active;
        private bool _updatePending;
        private bool _refreshPending;
        private bool _disposed;

        internal StickySectionTracker(ScrollViewer scroll, FrameworkElement content, Action<StickyHeading?> show)
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
            RequestUpdate();
        }

        internal void Remove(FrameworkElement anchor)
        {
            _headings.RemoveAll(heading => ReferenceEquals(heading.Anchor, anchor));
            RequestUpdate();
        }

        internal void Clear()
        {
            foreach (StickyHeading heading in _headings) heading.Anchor.Opacity = 1;
            _headings.Clear();
            _active = null;
            _show(null);
        }

        internal void Update(bool refresh = false)
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
                _show(active);
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
        private void SizeChanged(object sender, SizeChangedEventArgs args) => RequestUpdate();

        public void Dispose()
        {
            _disposed = true;
            _scroll.ViewChanged -= ViewChanged;
            _scroll.Loaded -= Loaded;
            _content.SizeChanged -= SizeChanged;
            _headings.Clear();
        }
    }

    private readonly List<StickySectionTracker> _stickyTrackers = [];
    private readonly Dictionary<ItemsControl, ScrollViewer> _selectionScrollViewers = new();
    private readonly Dictionary<Grid, (SelectionGroup Group, SelectionToolbar Toolbar)> _groupSelectionToolbars = new();
    private readonly Dictionary<ItemsControl, SelectionToolbar> _pinnedSelectionToolbars = new();
    private StickySectionTracker? _settingsHeaders;
    private StickySectionTracker? _aboutHeaders;

    private SettingsSections CurrentSettingsPage => _selectedSettingsItem == SubTabVisual ? SettingsSections.Visual
        : _selectedSettingsItem == SubTabFilter ? SettingsSections.Filter
        : _selectedSettingsItem == SubTabMultiScreen ? SettingsSections.Screens
        : _selectedSettingsItem == SubTabBackup ? 0 : SettingsSections.Basic;

    private void SetupPageHeaders()
    {
        _settingsHeaders = new(SettingsContentScrollViewer, SettingsContent, heading =>
        {
            TxtActiveSettingsSection.Text = heading?.Label.Text ?? string.Empty;
            BtnResetSection.Tag = heading?.Action;
            BtnResetSection.Visibility = heading?.Action is SectionResetRequest ? Visibility.Visible : Visibility.Collapsed;
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
            heading => TxtActiveAboutSection.Text = heading?.Label.Text ?? string.Empty);
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
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(label);
        if (request != null)
        {
            var button = new Button { Width = 32, Height = 32, MinWidth = 0, Padding = new Thickness(), Tag = request,
                Content = new FontIcon { Glyph = "\uE777", FontSize = 16 } };
            button.Click += ResetSection_Click;
            content.Children.Add(button);
        }
        var anchor = new Grid { Margin = first ? new Thickness() : margin, HorizontalAlignment = HorizontalAlignment.Left,
            Opacity = first ? 0 : 1, IsHitTestVisible = !first };
        anchor.Children.Add(content);
        if (first) anchor.Height = 0;
        parent.Children.Insert(index, anchor);
        return anchor;
    }

    private void RefreshPageHeaders()
    {
        if (_settingsHeaders == null) return;
        SettingsSections page = CurrentSettingsPage;
        BtnBasicReset.Visibility = page == SettingsSections.Basic ? Visibility.Visible : Visibility.Collapsed;
        BtnVisualReset.Visibility = page == SettingsSections.Visual ? Visibility.Visible : Visibility.Collapsed;
        BtnFilterReset.Visibility = page == SettingsSections.Filter ? Visibility.Visible : Visibility.Collapsed;
        BtnRefreshScreens.Visibility = page == SettingsSections.Screens ? Visibility.Visible : Visibility.Collapsed;
        foreach (Button button in new[] { BtnResetSection }.Concat(SettingsContent.Children.OfType<StackPanel>().Append(PanelEnvironmentFilter)
                     .SelectMany(section => section.Children.OfType<Grid>()).SelectMany(header => header.Children.OfType<StackPanel>())
                     .SelectMany(header => header.Children.OfType<Button>())))
        {
            string label = Localization.Get("Settings_ResetSection");
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, label);
        }
        _settingsHeaders.Update(true);
        _aboutHeaders?.Update(true);
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

    private void RestorePendingSettings(IEnumerable<VisualResetItem> items)
    {
        _isLoading = _suppressValueSync = true;
        try
        {
            foreach (VisualResetItem item in items) item.Restore?.Invoke();
            SyncSliderAndBoxValues();
        }
        finally { _isLoading = _suppressValueSync = false; }
        UpdateDependentSettingsVisibility(animate: true);
        UpdateAdvancedSettingsVisibility();
        UpdateAdvancedHdrControls();
        UpdateApplySettingsState(SettingsSections.All);
        RefreshPageHeaders();
    }

    private void DiscardSettings_Click(object sender, RoutedEventArgs args)
    {
        if (_savedSettingsState == null || _isApplyingSettings) return;
        Focus(FocusState.Programmatic);
        ApplyBackupValuesToPanel(_savedSettingsState.GetValues(CurrentSettingsPage), animate: true);
        UpdateAdvancedSettingsVisibility();
        UpdateAdvancedHdrControls();
        UpdateApplySettingsState(SettingsSections.All);
        RefreshPageHeaders();
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
        bool first = element.DataContext is SelectionGroup { IsFirst: true };
        element.Margin = new Thickness(0, 0, 16, first ? 0 : 4);
        element.Height = first ? 0 : double.NaN;
        element.Opacity = first ? 0 : 1;
        element.IsHitTestVisible = !first;
    }

    private void SelectionHeading_Loaded(object sender, RoutedEventArgs args) => InitializeSelectionHeading(sender);
    private void SelectionHeading_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => InitializeSelectionHeading(sender);

    private void InitializeSelectionHeading(object sender)
    {
        if (sender is not Grid { DataContext: SelectionGroup group } header) return;
        header.Height = group.IsFirst ? 0 : double.NaN;
        header.Margin = group.IsFirst ? new Thickness() : new Thickness(0, 8, 0, 8);
        if (_groupSelectionToolbars.TryGetValue(header, out var existing) && ReferenceEquals(existing.Group, group)) return;
        Button[] buttons = header.Children.OfType<Button>().ToArray();
        SelectionToolbar global = _selectionToolbars[group.List];
        var toolbar = new SelectionToolbar(buttons[0], buttons[1], global.Confirm, global.EmptyMessage, group.Items);
        InitializeSelectionToolbar(toolbar);
        _groupSelectionToolbars[header] = (group, toolbar);
        UpdateSelectionButtons(toolbar, group.List != ListBackupItems || !_backupBusy);
        UpdateSelectionHeadingVisibility(group.List);
    }

    private void SelectionElement_Clearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        Grid? header = args.Element as Grid ?? FindVisualDescendant<Grid>(args.Element, "SelectionHeading");
        if (header != null) _groupSelectionToolbars.Remove(header);
        if (args.Element is FrameworkElement element) element.DataContext = null;
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
        if (ReferenceEquals(toolbar.Invert.Tag, group) && title.Text == (group?.Title ?? string.Empty)) return;
        header.Visibility = group == null ? Visibility.Collapsed : Visibility.Visible;
        title.Text = group?.Title ?? string.Empty;
        toolbar.Items.Clear();
        if (group != null) toolbar.Items.AddRange(group.Items);
        toolbar.Invert.Tag = toolbar.SelectAll.Tag = group;
        UpdateSelectionButtons(toolbar, list != ListBackupItems || !_backupBusy);
        UpdateSelectionHeadingVisibility(list);
    }

    private void UpdateSectionSelectionToolbars(ItemsControl list)
    {
        bool available = list != ListBackupItems || !_backupBusy;
        foreach (var entry in _groupSelectionToolbars.Values)
            if (entry.Group.List == list) UpdateSelectionButtons(entry.Toolbar, available);
        if (_pinnedSelectionToolbars.TryGetValue(list, out SelectionToolbar? toolbar)) UpdateSelectionButtons(toolbar, available);
    }
}
