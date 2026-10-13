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
            Update();
        }

        internal void Remove(FrameworkElement anchor)
        {
            _headings.RemoveAll(heading => ReferenceEquals(heading.Anchor, anchor));
            Update();
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
        private void Loaded(object sender, RoutedEventArgs args) => Update(true);
        private void SizeChanged(object sender, SizeChangedEventArgs args) => Update();

        public void Dispose()
        {
            _scroll.ViewChanged -= ViewChanged;
            _scroll.Loaded -= Loaded;
            _content.SizeChanged -= SizeChanged;
            _headings.Clear();
        }
    }

    private readonly List<StickySectionTracker> _stickyTrackers = [];
    private readonly Dictionary<ItemsControl, StickySectionTracker> _selectionHeaders = new();
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
            FrameworkElement anchor = WrapPageHeading(section.Item1, section.Item2, request, section.Item5);
            StackPanel page = section.Item2;
            _settingsHeaders.Add(new(anchor, section.Item1, request, () => page.Visibility == Visibility.Visible));
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
        foreach (Button button in new[] { BtnResetSection }.Concat(SettingsContent.Children.OfType<StackPanel>()
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
        UpdateClickEffectPanelVisibility();
        UpdateEnvironmentFilterInterlock();
        UpdateMultiTouchPanelVisibility();
        UpdateEffectScalePanelVisibility();
        UpdateAnimationSpeedPanelVisibility();
        UpdateAdvancedSettingsVisibility();
        UpdateAdvancedHdrControls();
        UpdateApplySettingsState(SettingsSections.All);
        RefreshPageHeaders();
    }

    private void DiscardSettings_Click(object sender, RoutedEventArgs args)
    {
        if (_savedSettingsState == null || _isApplyingSettings) return;
        Focus(FocusState.Programmatic);
        ApplyBackupValuesToPanel(_savedSettingsState.GetValues(CurrentSettingsPage));
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
        var tracker = new StickySectionTracker(scroll, list, heading => ShowSelectionGroup(list, heading?.Action as SelectionGroup));
        _selectionHeaders[list] = tracker;
        _stickyTrackers.Add(tracker);
    }

    private void SetGroupedSelectionRows(ItemsControl list, IEnumerable<SelectionCardItem> items, Func<SelectionCardItem, string> groupName)
    {
        _selectionHeaders[list].Clear();
        var rows = new List<object>();
        foreach (var group in items.GroupBy(groupName))
        {
            var heading = new SelectionGroup(group.Key, list, group.ToList(), rows.Count == 0);
            rows.Add(heading);
            rows.AddRange(heading.Items);
        }
        list.ItemsSource = rows;
        RefreshSelectionToolbar(list);
    }

    private void SelectionHeading_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Grid { DataContext: SelectionGroup group } header) return;
        if (group.IsFirst)
        {
            header.Height = 0;
            header.Opacity = 0;
            header.IsHitTestVisible = false;
            header.Margin = new Thickness();
            if (header.Parent is ContentPresenter container) container.Margin = new Thickness(0, 0, 16, 0);
        }
        Button[] buttons = header.Children.OfType<Button>().ToArray();
        SelectionToolbar global = _selectionToolbars[group.List];
        var toolbar = new SelectionToolbar(buttons[0], buttons[1], global.Confirm, global.EmptyMessage, group.Items);
        InitializeSelectionToolbar(toolbar);
        _groupSelectionToolbars[header] = (group, toolbar);
        UpdateSelectionButtons(toolbar, group.List != ListBackupItems || !_backupBusy);
        _selectionHeaders[group.List].Add(new(header, header.Children.OfType<TextBlock>().First(), group, () => true));
    }

    private void SelectionHeading_Unloaded(object sender, RoutedEventArgs args)
    {
        if (sender is Grid header && _groupSelectionToolbars.Remove(header, out var entry))
            _selectionHeaders[entry.Group.List].Remove(header);
    }

    private void ShowSelectionGroup(ItemsControl list, SelectionGroup? group)
    {
        Grid header = list == ListVisualResetItems ? ResetGroupHeader : BackupGroupHeader;
        TextBlock title = list == ListVisualResetItems ? TxtResetGroup : TxtBackupGroup;
        header.Visibility = group == null ? Visibility.Collapsed : Visibility.Visible;
        title.Text = group?.Title ?? string.Empty;
        SelectionToolbar toolbar = _pinnedSelectionToolbars[list];
        toolbar.Items.Clear();
        if (group != null) toolbar.Items.AddRange(group.Items);
        toolbar.Invert.Tag = toolbar.SelectAll.Tag = group;
        UpdateSelectionButtons(toolbar, list != ListBackupItems || !_backupBusy);
    }

    private void UpdateSectionSelectionToolbars(ItemsControl list)
    {
        bool available = list != ListBackupItems || !_backupBusy;
        foreach (var entry in _groupSelectionToolbars.Values)
            if (entry.Group.List == list) UpdateSelectionButtons(entry.Toolbar, available);
        if (_pinnedSelectionToolbars.TryGetValue(list, out SelectionToolbar? toolbar)) UpdateSelectionButtons(toolbar, available);
    }
}
