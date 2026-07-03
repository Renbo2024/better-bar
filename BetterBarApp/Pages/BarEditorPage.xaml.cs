using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

/// <summary>
/// In-window editor for a <see cref="BarDefinition"/> (name, height, placements, items),
/// replacing the old BarConfigWindow dialog. Changes apply live: every edit saves and
/// refreshes the affected panels.
/// </summary>
public partial class BarEditorPage : Page
{
    public sealed record ScreenOption(string Label, int Number)
    {
        public override string ToString() => Label;
    }

    private readonly BarDefinition _def;

    public List<ScreenOption> MonitorOptions  { get; } = [];
    public PanelPosition[]    PositionOptions { get; } = { PanelPosition.Bottom, PanelPosition.Top };

    private readonly ObservableCollection<PanelConfig> _placements = [];
    private bool _loaded;

    public BarEditorPage()
    {
        _def = SettingsWindow.TakeContext() as BarDefinition
               ?? PanelManager.Definitions.FirstOrDefault()
               ?? PanelManager.NewDefinition();
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        foreach (var s in ScreenService.Screens)
            MonitorOptions.Add(new ScreenOption(s.Label, s.Number));

        TitleText.Text = _def.Name;
        NameBox.Text   = _def.Name;
        HeightBox.Value = _def.HeightPx;

        foreach (var p in PanelManager.PanelsFor(_def)) _placements.Add(p);
        PanelsList.ItemsSource = _placements;
        ItemsList.ItemsSource  = _def.Items;

        _loaded = true;
    }

    private void Apply() { SettingsService.Save(); PanelManager.RefreshDefinition(_def); }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarsPage));

    // ── Basics ──
    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _def.Name = NameBox.Text.Trim() is { Length: > 0 } n ? n : "Bar";
        TitleText.Text = _def.Name;
        Apply();
    }

    private void HeightBox_ValueChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _def.HeightPx = HeightBox.Value;
        Apply();
    }

    // ── Placements ──
    private void AddPanel_Click(object sender, RoutedEventArgs e)
    {
        var panel = new PanelConfig { DefinitionId = _def.Id };
        PanelManager.AddPanel(panel);
        _placements.Add(panel);
        Apply();
    }

    private void RemovePanel_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PanelConfig panel)
        {
            PanelManager.RemovePanel(panel);   // closes its window if live
            _placements.Remove(panel);
            Apply();
        }
    }

    private void EnableToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PanelConfig panel)
        {
            if (panel.IsEnabled) PanelManager.EnablePanel(panel);
            else                 PanelManager.DisablePanel(panel);
            Apply();
        }
    }

    // Monitor / position changed on an existing placement → re-place it live.
    private void Placement_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        if ((sender as FrameworkElement)?.DataContext is PanelConfig panel)
        {
            PanelManager.RefreshPanel(panel);
            Apply();
        }
    }

    // ── Items ──
    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ItemTypePicker(Window.GetWindow(this)!);
        if (picker.ShowDialog() != true || picker.CreatedItem == null) return;

        var item = picker.CreatedItem;
        _def.Items.Add(item);
        Apply();
        SettingsWindow.Navigate(ItemPages.PageTypeFor(item), new ItemEditContext(_def, item));
    }

    private void EditItem_Click(object sender, RoutedEventArgs e) => EditItem(sender);
    private void Item_DoubleClick(object sender, MouseButtonEventArgs e) => EditItem(sender);

    private void EditItem(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is BarItem item)
            SettingsWindow.Navigate(ItemPages.PageTypeFor(item), new ItemEditContext(_def, item));
    }

    private void CloneItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BarItem item) return;

        var clone = SettingsService.CloneItem(item);
        // Only one grow-to-fill item is honoured per panel, so a clone never inherits it.
        if (clone is IGrowToFillItem g) g.GrowToFill = false;

        int i = _def.Items.IndexOf(item);
        _def.Items.Insert(i + 1, clone);   // place the copy right after the original
        Apply();
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BarItem item)
        {
            _def.Items.Remove(item);
            Apply();
        }
    }

    private void MoveItemUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BarItem item)
        {
            int i = _def.Items.IndexOf(item);
            if (i > 0) { _def.Items.Move(i, i - 1); Apply(); }
        }
    }

    private void MoveItemDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BarItem item)
        {
            int i = _def.Items.IndexOf(item);
            if (i >= 0 && i < _def.Items.Count - 1) { _def.Items.Move(i, i + 1); Apply(); }
        }
    }

    // ── Drag-and-drop reordering of items (the up/down buttons still work too) ──
    private const string ItemDragFormat = "BarItemDrag";
    private Point     _itemDragStart;
    private BarItem?  _itemDragCandidate;

    private void Items_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        _itemDragStart     = e.GetPosition(null);
        _itemDragCandidate = (e.OriginalSource as FrameworkElement)?.DataContext as BarItem;
    }

    private void Items_PreviewMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _itemDragCandidate is null) return;
        var diff = e.GetPosition(null) - _itemDragStart;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _itemDragCandidate;
        _itemDragCandidate = null;
        DragDrop.DoDragDrop(ItemsList, new DataObject(ItemDragFormat, item), DragDropEffects.Move);
    }

    private void Items_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(ItemDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Items_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(ItemDragFormat) is not BarItem dragged) return;
        var target = (e.OriginalSource as FrameworkElement)?.DataContext as BarItem;

        int from = _def.Items.IndexOf(dragged);
        int to   = target != null ? _def.Items.IndexOf(target) : _def.Items.Count - 1;
        if (from < 0 || to < 0 || from == to) return;

        _def.Items.Move(from, to);
        Apply();
    }
}
