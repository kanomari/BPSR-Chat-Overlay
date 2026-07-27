using System.Globalization;
using System.Windows;
using BPSRChatOverlay.Config;

namespace BPSRChatOverlay;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _currentConfig;

    public AppConfig? SavedConfig { get; private set; }

    public SettingsWindow(AppConfig currentConfig)
    {
        InitializeComponent();

        _currentConfig = currentConfig;

        FontSizeTextBox.Text =
            currentConfig.FontSize.ToString(CultureInfo.CurrentCulture);
        OpacityTextBox.Text =
            currentConfig.Opacity.ToString(CultureInfo.CurrentCulture);
        TopMostCheckBox.IsChecked = currentConfig.TopMost;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(
                FontSizeTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int fontSize))
        {
            MessageBox.Show(
                this,
                "フォントサイズには整数を入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (fontSize < 8 || fontSize > 48)
        {
            MessageBox.Show(
                this,
                "フォントサイズは8～48の範囲で入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(
                OpacityTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out double opacity))
        {
            MessageBox.Show(
                this,
                "不透明度には小数を入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (opacity < 0.2 || opacity > 1.0)
        {
            MessageBox.Show(
                this,
                "不透明度は0.2～1.0の範囲で入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SavedConfig = new AppConfig
        {
            CaptureDeviceName = _currentConfig.CaptureDeviceName,
            ExeNames = [.. _currentConfig.ExeNames],
            FontSize = fontSize,
            Opacity = opacity,
            TopMost = TopMostCheckBox.IsChecked == true
        };

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
