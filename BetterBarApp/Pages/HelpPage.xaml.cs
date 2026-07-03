using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace BetterBarApp.Pages;

/// <summary>
/// The Help tab: renders the shipped README (embedded as a manifest resource) as a FlowDocument via
/// MdXaml. Doubles as the user-facing help text. Links open in the default browser.
/// </summary>
public partial class HelpPage : Page
{
    private const string ResourceName = "BetterBarApp.README.md";

    public HelpPage()
    {
        InitializeComponent();
        Viewer.Markdown = LoadReadme();
        Viewer.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnNavigate));
    }

    private static string LoadReadme()
    {
        try
        {
            using var stream = typeof(HelpPage).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null) return "# Help\n\nThe help content could not be found.";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return "# Help\n\nThe help content could not be loaded.\n\n```\n" + ex.Message + "\n```";
        }
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Only follow absolute web links; relative links (LICENSE, #anchors) are no-ops in-app.
        if (e.Uri is { IsAbsoluteUri: true } uri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); } catch { }
        }
        e.Handled = true;
    }
}
