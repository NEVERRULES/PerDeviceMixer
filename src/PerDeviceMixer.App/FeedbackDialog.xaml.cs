using System.Windows;

namespace PerDeviceMixer.App;

public partial class FeedbackDialog : Window
{
    private readonly string _basicInformation;
    private readonly string _diagnosticInformation;

    public FeedbackDialog(string basicInformation, string diagnosticInformation)
    {
        _basicInformation = basicInformation;
        _diagnosticInformation = diagnosticInformation;
        InitializeComponent();
        PreviewTextBox.Text = _basicInformation;
    }

    private void OnDiagnosticsSelectionChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (PreviewTextBox is null) return;
        PreviewTextBox.Text = IncludeDiagnosticsCheckBox.IsChecked == true
            ? _basicInformation + _diagnosticInformation
            : _basicInformation;
    }

    private void OnCopyClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            Clipboard.SetText(PreviewTextBox.Text);
            StatusText.Text = "信息已复制到剪贴板。";
        }
        catch (Exception exception)
        {
            StatusText.Text = "复制失败：" + exception.Message;
        }
    }

    private void OnOpenGitHubClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            ExternalLinkService.Open(ExternalLinkService.CreateIssueUrl(PreviewTextBox.Text));
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = "无法打开 GitHub：" + exception.Message;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs eventArgs) => DialogResult = false;
}
