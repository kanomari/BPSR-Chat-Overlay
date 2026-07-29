using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Serilog;

namespace BPSRChatOverlay;

public partial class UpdateAvailableWindow : Window
{
    private readonly Uri? _releasePageUri;

    public UpdateAvailableWindow(
        string messageText,
        string? currentVersionText,
        string? latestVersionText,
        Uri? releasePageUri,
        string secondaryButtonText)
    {
        InitializeComponent();

        MessageTextBlock.Text = messageText;
        SecondaryButton.Content = secondaryButtonText;
        _releasePageUri = releasePageUri;

        if (string.IsNullOrWhiteSpace(currentVersionText))
        {
            VersionDetailsGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            CurrentVersionTextBlock.Text = currentVersionText;
        }

        if (string.IsNullOrWhiteSpace(latestVersionText))
        {
            LatestVersionLabel.Visibility = Visibility.Collapsed;
            LatestVersionTextBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            LatestVersionTextBlock.Text = latestVersionText;
        }

        OpenReleasePageButton.Visibility = releasePageUri is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // マウスボタンが解放済みの場合は移動を開始しません。
        }
    }

    private void OpenReleasePageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_releasePageUri is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _releasePageUri.AbsoluteUri,
                UseShellExecute = true
            });
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to open the GitHub Releases page. Url: {Url}",
                _releasePageUri);
            MessageBox.Show(
                this,
                "ダウンロードページを開けませんでした。",
                "BPSR Chat Overlay",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SecondaryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
