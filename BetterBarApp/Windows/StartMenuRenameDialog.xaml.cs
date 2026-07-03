using System.Windows;

namespace BetterBarApp.Windows;

public partial class StartMenuRenameDialog : Window
{
    /// <summary>Final entered text. Null until OK pressed.</summary>
    public string? ResultName { get; private set; }

    public StartMenuRenameDialog(string currentName, Window? owner)
    {
        Owner = owner;
        InitializeComponent();
        NameBox.Text = currentName;
        Loaded += (_, _) =>
        {
            // Select all and focus the text so the user can immediately overtype.
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ResultName   = NameBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
