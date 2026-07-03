using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BetterBarApp.Controls;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Pages;

public partial class ThemeEditorPage : Page
{
    private readonly Dictionary<string, FrameworkElement> _editors = [];
    private ThemeService.ThemeInfo? _current;
    private Dictionary<string, object> _values = [];
    private bool _loading;

    public ThemeEditorPage()
    {
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);
        BuildRows();
        RefreshThemeList();
        SelectInfo(ThemeService.Current);
    }

    // ── Build the per-key editor rows once (values are filled per theme in LoadTheme) ──
    private void BuildRows()
    {
        string? group = null;
        foreach (var key in ThemeSchema.Keys)
        {
            if (key.Group != group)
            {
                group = key.Group;
                RowsHost.Children.Add(new TextBlock
                {
                    Text = group, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(2, group == ThemeSchema.Keys[0].Group ? 0 : 14, 0, 6),
                });
            }

            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = key.Display, Width = 200, VerticalAlignment = VerticalAlignment.Center });

            FrameworkElement editor;
            if (key.Kind == ThemeKeyKind.CornerRadius)
            {
                var box = new NumericBox { Minimum = 0, Maximum = 24, Step = 1, Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Tag = key };
                box.ValueChanged += CornerChanged;
                editor = box;
            }
            else
            {
                var picker = new ColorPickerButton { ShowAlpha = true, AllowDefault = false, HorizontalAlignment = HorizontalAlignment.Left, Tag = key };
                picker.ValueChanged += ColorChanged;
                editor = picker;
            }

            _editors[key.Key] = editor;
            row.Children.Add(editor);
            RowsHost.Children.Add(row);
        }
    }

    // ── Theme list / selection ─────────────────────────────────────────────────────
    private void RefreshThemeList()
    {
        _loading = true;
        ThemeCombo.ItemsSource = ThemeService.Available.ToList();
        _loading = false;
    }

    private void SelectInfo(ThemeService.ThemeInfo info)
    {
        // Match by reference within the freshly-bound list.
        foreach (var item in (IEnumerable<ThemeService.ThemeInfo>)ThemeCombo.ItemsSource)
            if (item == info) { ThemeCombo.SelectedItem = item; return; }
        if (ThemeCombo.Items.Count > 0) ThemeCombo.SelectedIndex = 0;
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedItem is not ThemeService.ThemeInfo info) return;
        LoadTheme(info);
    }

    private void LoadTheme(ThemeService.ThemeInfo info)
    {
        _current = info;
        ThemeService.Apply(info.Name);          // live preview
        _values = ThemeService.ReadValues(info);

        _loading = true;
        NameBox.Text = info.Name;
        NameBox.IsEnabled    = !info.BuiltIn;
        DeleteBtn.IsEnabled  = !info.BuiltIn;
        BuiltInNote.Visibility = info.BuiltIn ? Visibility.Visible : Visibility.Collapsed;

        foreach (var key in ThemeSchema.Keys)
        {
            if (!_editors.TryGetValue(key.Key, out var editor)) continue;
            editor.IsEnabled = !info.BuiltIn;
            if (!_values.TryGetValue(key.Key, out var v)) continue;
            if (editor is NumericBox box) box.Value = Convert.ToInt32(v);
            else if (editor is ColorPickerButton picker) picker.Value = HexOf((Color)v);
        }
        _loading = false;
    }

    // ── Edits (user themes only) → live preview + autosave ──────────────────────────
    private void ColorChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is not { BuiltIn: false } || sender is not ColorPickerButton p || p.Tag is not ThemeKey key) return;
        if (ParseColor(p.Value) is not { } c) return;
        _values[key.Key] = c;
        ThemeService.SetLiveValue(key.Key, ThemeKeyKind.Color, c);
        ThemeService.Save(_current, _values);
    }

    private void CornerChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _current is not { BuiltIn: false } || sender is not NumericBox box || box.Tag is not ThemeKey key) return;
        _values[key.Key] = box.Value;
        ThemeService.SetLiveValue(key.Key, ThemeKeyKind.CornerRadius, box.Value);
        ThemeService.Save(_current, _values);
    }

    // ── Name ────────────────────────────────────────────────────────────────────────
    private void NameBox_Commit(object sender, RoutedEventArgs e) => CommitName();
    private void NameBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) CommitName(); }

    private void CommitName()
    {
        if (_current is not { BuiltIn: false } info) return;
        var name = NameBox.Text.Trim();
        if (name.Length == 0 || name == info.Name) { NameBox.Text = info.Name; return; }
        ThemeService.Rename(info, name);
        RefreshThemeList();
        SelectInfo(info);
    }

    // ── Actions ───────────────────────────────────────────────────────────────────
    private void Clone_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var clone = ThemeService.CloneTheme(_current);
        RefreshThemeList();
        SelectInfo(clone);
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var values = ThemeService.ReadValues(_current ?? ThemeService.Current);
        var created = ThemeService.CreateUserTheme("Custom theme", values);
        RefreshThemeList();
        SelectInfo(created);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "BetterBar theme (*.json)|*.json", Title = "Import theme" };
        if (dlg.ShowDialog() != true) return;
        var info = ThemeService.Import(dlg.FileName);
        RefreshThemeList();
        SelectInfo(info);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "BetterBar theme (*.json)|*.json",
            Title = "Export theme",
            FileName = _current.Name + ".json",
        };
        if (dlg.ShowDialog() == true) ThemeService.Export(_current, dlg.FileName);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { BuiltIn: false } info) return;
        if (MessageBox.Show($"Delete the theme \"{info.Name}\"?", "BetterBar",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        ThemeService.DeleteUserTheme(info);
        RefreshThemeList();
        SelectInfo(ThemeService.Current);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => SettingsWindow.Navigate(typeof(AppearancePage));

    // ── Helpers ───────────────────────────────────────────────────────────────────
    private static string HexOf(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color? ParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return (Color)ColorConverter.ConvertFromString(s)!; }
        catch { return null; }
    }
}
