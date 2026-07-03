using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterBarApp.Models;
using BetterBarApp.Services;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace BetterBarApp.Controls;

/// <summary>
/// Renders the Power item's action buttons (Power / Hibernate / Sleep / Log Off) — icon, optional
/// label, configurable sizing — laid out left-to-right and vertically centred. Hibernate is hidden
/// unless the system supports it. Clicking a button performs the action (after an optional
/// confirmation prompt).
/// </summary>
public partial class PowerControl : UserControl
{
    private sealed record PowerEntry(string Label, string Verb, SymbolRegular Icon, Action Perform);

    private readonly PowerItem _item;

    public PowerControl(PowerItem item)
    {
        _item = item;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Build();

    private void Build()
    {
        Outer.Margin = new Thickness(_item.OuterMargin, 0, _item.OuterMargin, 0);
        Host.Children.Clear();

        bool first = true;
        if (_item.ShowPower)
            AddButton(new PowerEntry("Power", "shut down", SymbolRegular.Power24, PowerActions.Shutdown), ref first);
        if (_item.ShowReboot)
            AddButton(new PowerEntry("Reboot", "restart", SymbolRegular.ArrowClockwise24, PowerActions.Reboot), ref first);
        if (_item.ShowHibernate && PowerActions.HibernateAvailable)
            AddButton(new PowerEntry("Hibernate", "hibernate", SymbolRegular.Bed24, PowerActions.Hibernate), ref first);
        if (_item.ShowSleep)
            AddButton(new PowerEntry("Sleep", "sleep", SymbolRegular.Sleep24, PowerActions.Sleep), ref first);
        if (_item.ShowLogOff)
            AddButton(new PowerEntry("Log Off", "log off", SymbolRegular.SignOut24, PowerActions.LogOff), ref first);
    }

    private void AddButton(PowerEntry entry, ref bool first)
    {
        var icon = new SymbolIcon { Symbol = entry.Icon, FontSize = Math.Max(8, _item.IconSize) };
        icon.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TaskBtnFg");

        var content = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(icon);

        if (_item.ShowLabels)
        {
            var label = new TextBlock
            {
                Text = entry.Label,
                FontSize = Math.Max(1, _item.LabelFontSize),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            };
            if (!string.IsNullOrWhiteSpace(_item.LabelFontFamily))
                label.FontFamily = new FontFamily(_item.LabelFontFamily);
            label.SetResourceReference(TextBlock.ForegroundProperty, "TaskBtnFg");
            content.Children.Add(label);
        }

        var btn = new Button
        {
            Content = content,
            Padding = new Thickness(4, 2, 4, 2),
            ToolTip = entry.Label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (TryFindResource("BarIconButton") is Style style) btn.Style = style;   // hand cursor + fade hover
        btn.Click += (_, _) => Invoke(entry);

        var column = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        if (!first) column.Margin = new Thickness(_item.IconSpacing, 0, 0, 0);
        first = false;
        column.Children.Add(btn);
        Host.Children.Add(column);
    }

    private void Invoke(PowerEntry entry)
    {
        if (_item.ConfirmAction)
        {
            var confirm = System.Windows.MessageBox.Show(
                $"Are you sure you want to {entry.Verb}?", "BetterBar",
                System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.OK) return;
        }

        entry.Perform();
    }
}
