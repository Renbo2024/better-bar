using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BetterBarApp.Models;
using BetterBarApp.Services;

namespace BetterBarApp.Windows;

public partial class ItemTypePicker : Window
{
    public BarItem? CreatedItem { get; private set; }

    public ItemTypePicker(Window owner)
    {
        Owner = owner;
        InitializeComponent();
        TypeList.ItemsSource = ItemTypeRegistry.Types;
        if (ItemTypeRegistry.Types.Count > 0)
            TypeList.SelectedIndex = 0;
    }

    private void OK_Click(object sender, RoutedEventArgs e) => Commit();
    private void TypeList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Commit();

    private void Commit()
    {
        if (TypeList.SelectedItem is not ItemTypeInfo info) return;
        CreatedItem  = info.Factory();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
