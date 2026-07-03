using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class SystemMonitorPage : Page
{
    private readonly SystemMonitorItem? _item;
    private readonly BarDefinition?     _def;
    private readonly ObservableCollection<MonitorWidget> _widgets = new();

    // Option sources bound from the widget DataTemplates (RelativeSource Page).
    public IReadOnlyList<string> Fonts { get; }
    public int[] SampleRates { get; } = { 250, 500, 1000, 1500, 2000 };
    public Array ScrollModes { get; } = Enum.GetValues(typeof(MonitorScrollMode));
    public IReadOnlyList<NetworkSampler.Nic> Nics { get; } = NetworkSampler.List();

    public SystemMonitorPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as SystemMonitorItem;
        _def  = ctx?.Definition;

        Fonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source).Distinct()
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        if (_item != null)
        {
            DataContext = _item;                       // top-level NumericBoxes bind to the item
            _item.PropertyChanged += OnAnyChanged;     // spacing/margins → save+refresh

            foreach (var w in _item.Widgets) Track(w);
            WidgetsList.ItemsSource = _widgets;
        }
    }

    private void Track(MonitorWidget w)
    {
        _widgets.Add(w);
        w.PropertyChanged += OnAnyChanged;
    }

    private void AddCpu_Click(object sender, RoutedEventArgs e)
    {
        var w = new CpuMonitorWidget { Title = "CPU", Subtitle = "%value%" };
        w.PropertyChanged += OnAnyChanged;
        _widgets.Add(w);
        CommitWidgets();
    }

    private void AddNetwork_Click(object sender, RoutedEventArgs e)
    {
        // Default to the first interface that is currently up, else the first listed.
        var nic = Nics.FirstOrDefault(n => !n.Name.Contains('(')) ?? Nics.FirstOrDefault();
        var w = new NetworkMonitorWidget { Title = "NET", Subtitle = "%total%", InterfaceId = nic?.Id ?? "" };
        w.PropertyChanged += OnAnyChanged;
        _widgets.Add(w);
        CommitWidgets();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MonitorWidget w) return;
        w.PropertyChanged -= OnAnyChanged;
        _widgets.Remove(w);
        CommitWidgets();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)   => Move(sender, -1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(sender, +1);

    private void Move(object sender, int dir)
    {
        if ((sender as FrameworkElement)?.DataContext is not MonitorWidget w) return;
        int i = _widgets.IndexOf(w);
        int j = i + dir;
        if (i < 0 || j < 0 || j >= _widgets.Count) return;
        _widgets.Move(i, j);
        CommitWidgets();
    }

    // Structural change (add/remove/reorder): mirror order into the item, persist, and rebuild
    // the live bar so the widget set matches.
    private void CommitWidgets()
    {
        if (_item == null) return;
        _item.Widgets = _widgets.ToList();
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
    }

    // Property edits on the item or a widget just persist — the live controls observe the same
    // model objects and update themselves (graph redraws from its saved samples), so no rebuild.
    private void OnAnyChanged(object? sender, PropertyChangedEventArgs e) => SettingsService.Save();

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);
}
