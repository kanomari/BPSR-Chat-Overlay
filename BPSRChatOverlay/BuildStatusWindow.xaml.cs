using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BPSRChatOverlay.Config;
using BPSRChatOverlay.Managers;
using BPSRChatOverlay.Models;
using BPSRChatOverlay.UIResources;

namespace BPSRChatOverlay;

public partial class BuildStatusWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
    private const double BaseWindowHeight = 42.0;
    private const int ResizeBorderPixels = 8;

    private static readonly Brush ReadingHighlightBrush =
        new SolidColorBrush(Color.FromArgb(0x90, 0x42, 0x72, 0xA8));
    private static readonly Brush WarningHighlightBrush =
        new SolidColorBrush(Color.FromArgb(0xA0, 0xA1, 0x45, 0x32));
    private static readonly Brush TransparentBrush = Brushes.Transparent;

    private readonly BuildStatusCaptureManager _captureManager;
    private readonly Func<int, int, bool> _registerCombination;
    private AppConfig _config;
    private BuildStatusSnapshot _snapshot;
    private IntPtr _windowHandle;
    private HwndSource? _windowSource;
    private bool _isApplyingSettings;

    public BuildStatusWindow(
        BuildStatusCaptureManager captureManager,
        AppConfig config,
        Func<int, int, bool> registerCombination)
    {
        InitializeComponent();
        _captureManager = captureManager;
        _registerCombination = registerCombination;
        _config = config;
        _snapshot = _captureManager.Current;

        RestorePlacement();
        ApplySettings(config);
        ApplyStatus(_snapshot);
        _captureManager.StatusChanged += CaptureManager_StatusChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowMessageHook);
        ApplyClickThrough(_config.ClickThrough);
    }

    protected override void OnClosed(EventArgs e)
    {
        _captureManager.StatusChanged -= CaptureManager_StatusChanged;
        _windowSource?.RemoveHook(WindowMessageHook);
        base.OnClosed(e);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (_isApplyingSettings ||
            !IsLoaded ||
            _config.ClickThrough)
        {
            return;
        }

        double scale = Math.Clamp(
            ActualHeight / BaseWindowHeight,
            AppConfig.MinBuildStatusWindowScale,
            AppConfig.MaxBuildStatusWindowScale);
        _config.BuildStatusWindowScale = scale;
        _config.BuildStatusWindowWidth = Math.Clamp(
            ActualWidth,
            AppConfig.MinBuildStatusWindowWidth,
            AppConfig.MaxBuildStatusWindowWidth);
        RootScaleTransform.ScaleX = scale;
        RootScaleTransform.ScaleY = scale;
    }

    public void ApplySettings(AppConfig config)
    {
        _config = config;
        _isApplyingSettings = true;
        try
        {
            Width = Math.Clamp(
                config.BuildStatusWindowWidth,
                AppConfig.MinBuildStatusWindowWidth,
                AppConfig.MaxBuildStatusWindowWidth);

            double scale = Math.Clamp(
                config.BuildStatusWindowScale,
                AppConfig.MinBuildStatusWindowScale,
                AppConfig.MaxBuildStatusWindowScale);
            Height = BaseWindowHeight * scale;
            RootScaleTransform.ScaleX = scale;
            RootScaleTransform.ScaleY = scale;
        }
        finally
        {
            _isApplyingSettings = false;
        }

        byte backgroundAlpha = (byte)Math.Round(
            Math.Clamp(config.BuildStatusBackgroundOpacity, 0.0, 1.0) *
            byte.MaxValue);
        RootBorder.Background = new SolidColorBrush(
            Color.FromArgb(backgroundAlpha, 0x1A, 0x1E, 0x27));

        DropShadowEffect? textShadow = config.EnableChatTextShadow
            ? new DropShadowEffect
            {
                Color = ChatColors.CreateBrush(
                    config.ChatTextShadowColor,
                    ChatColors.DefaultChatTextShadowColor).Color,
                Opacity = 0.30,
                Direction = 315,
                ShadowDepth = 1,
                BlurRadius = 1.5,
                RenderingBias = RenderingBias.Performance
            }
            : null;
        textShadow?.Freeze();
        WarningText.Effect = textShadow;
        TypeText.Effect = textShadow;
        SeparatorText.Effect = textShadow;
        CultivateText.Effect = textShadow;

        Topmost = config.TopMost;
        ApplyClickThrough(config.ClickThrough);
        ApplyStatus(_snapshot);
    }

    public void SetDisplayed(bool displayed)
    {
        if (displayed)
        {
            if (!IsVisible)
            {
                Show();
            }

            return;
        }

        Hide();
    }

    public void ApplyClickThrough(bool enabled)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        IntPtr currentStyle = GetWindowLongPtr(_windowHandle, GwlExStyle);
        long updatedStyle = currentStyle.ToInt64();

        if (enabled)
        {
            updatedStyle |= WsExTransparent | WsExLayered;
        }
        else
        {
            updatedStyle &= ~WsExTransparent;
        }

        SetWindowLongPtr(
            _windowHandle,
            GwlExStyle,
            new IntPtr(updatedStyle));
    }

    private void CaptureManager_StatusChanged(BuildStatusSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => ApplyStatus(snapshot));
            return;
        }

        ApplyStatus(snapshot);
    }

    private void ApplyStatus(BuildStatusSnapshot snapshot)
    {
        _snapshot = snapshot;

        bool warningsEnabled = _config.EnableBuildStatusWarnings;
        bool typeReading =
            string.IsNullOrEmpty(snapshot.TypeName) &&
            !snapshot.IsTypeUnselected;
        bool typeWarning = warningsEnabled && snapshot.IsTypeUnselected;

        TypeText.Text = snapshot.IsTypeUnselected
            ? "型未選択"
            : typeReading
                ? "読取中"
                : snapshot.TypeName;
        FontWeight normalFontWeight = _config.EnableBoldMessageText
            ? FontWeights.Bold
            : FontWeights.Normal;
        TypeText.FontWeight = typeReading || typeWarning
            ? FontWeights.Bold
            : normalFontWeight;
        TypeReadingHighlight.Background = typeWarning
            ? WarningHighlightBrush
            : typeReading
                ? ReadingHighlightBrush
                : TransparentBrush;

        bool cultivateReading =
            snapshot.CultivateAreaId is null &&
            !snapshot.IsCultivateDisabled;
        bool combinationMismatch =
            warningsEnabled &&
            snapshot.TalentId is { } talentId &&
            snapshot.CultivateAreaId is { } cultivateAreaId &&
            !_config.BuildStatusRegistrations.Any(registration =>
                registration.TalentId == talentId &&
                registration.CultivateAreaId == cultivateAreaId);
        bool cultivateWarning =
            warningsEnabled &&
            (snapshot.IsCultivateDisabled || combinationMismatch);

        CultivateText.Text = cultivateReading
            ? "読取中"
            : snapshot.IsCultivateDisabled
                ? "無効"
                : snapshot.CultivateName ?? "読取中";
        CultivateText.FontWeight = cultivateReading || cultivateWarning
            ? FontWeights.Bold
            : normalFontWeight;
        CultivateReadingHighlight.Background = cultivateWarning
            ? WarningHighlightBrush
            : cultivateReading
                ? ReadingHighlightBrush
                : TransparentBrush;

        WarningText.Visibility = typeWarning || cultivateWarning
            ? Visibility.Visible
            : Visibility.Collapsed;

        bool combinationRegistered =
            snapshot.TalentId is { } registeredTalentId &&
            snapshot.CultivateAreaId is { } registeredCultivateAreaId &&
            _config.BuildStatusRegistrations.Any(registration =>
                registration.TalentId == registeredTalentId &&
                registration.CultivateAreaId == registeredCultivateAreaId);
        bool canRegister =
            snapshot.TalentId.HasValue &&
            !string.IsNullOrEmpty(snapshot.TypeName) &&
            !snapshot.IsTypeUnselected &&
            snapshot.CultivateAreaId.HasValue &&
            !string.IsNullOrEmpty(snapshot.CultivateName) &&
            !snapshot.IsCultivateDisabled &&
            !combinationRegistered;
        RegisterCombinationButton.Visibility = canRegister
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RegisterCombinationButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_snapshot.TalentId is not { } talentId ||
            string.IsNullOrEmpty(_snapshot.TypeName) ||
            _snapshot.IsTypeUnselected ||
            _snapshot.CultivateAreaId is not { } cultivateAreaId ||
            string.IsNullOrEmpty(_snapshot.CultivateName) ||
            _snapshot.IsCultivateDisabled ||
            _config.BuildStatusRegistrations.Any(registration =>
                registration.TalentId == talentId &&
                registration.CultivateAreaId == cultivateAreaId))
        {
            ApplyStatus(_snapshot);
            return;
        }

        if (_registerCombination(talentId, cultivateAreaId))
        {
            ApplyStatus(_snapshot);
        }
    }

    private void RestorePlacement()
    {
        if (_config.BuildStatusWindowLeft is { } left &&
            _config.BuildStatusWindowTop is { } top &&
            double.IsFinite(left) &&
            double.IsFinite(top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void Window_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed &&
            !_config.ClickThrough)
        {
            DragMove();
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal &&
            double.IsFinite(Left) &&
            double.IsFinite(Top))
        {
            _config.BuildStatusWindowLeft = Left;
            _config.BuildStatusWindowTop = Top;
        }
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmNcHitTest ||
            _config.ClickThrough ||
            !GetWindowRect(hwnd, out NativeRect rectangle))
        {
            return IntPtr.Zero;
        }

        int cursorX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        int cursorY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        bool left = cursorX <= rectangle.Left + ResizeBorderPixels;
        bool right = cursorX >= rectangle.Right - ResizeBorderPixels;
        bool top = cursorY <= rectangle.Top + ResizeBorderPixels;
        bool bottom = cursorY >= rectangle.Bottom - ResizeBorderPixels;

        int hitTest = (left, right, top, bottom) switch
        {
            (true, _, true, _) => HtTopLeft,
            (_, true, true, _) => HtTopRight,
            (true, _, _, true) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtLeft,
            (_, true, _, _) => HtRight,
            (_, _, true, _) => HtTop,
            (_, _, _, true) => HtBottom,
            _ => 0
        };

        if (hitTest == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(hitTest);
    }

    private static IntPtr GetWindowLongPtr(IntPtr windowHandle, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : new IntPtr(GetWindowLong32(windowHandle, index));
    }

    private static IntPtr SetWindowLongPtr(
        IntPtr windowHandle,
        int index,
        IntPtr newValue)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : new IntPtr(
                SetWindowLong32(windowHandle, index, newValue.ToInt32()));
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(
        IntPtr windowHandle,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongW",
        SetLastError = true)]
    private static extern int GetWindowLong32(
        IntPtr windowHandle,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr windowHandle,
        int index,
        IntPtr newValue);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongW",
        SetLastError = true)]
    private static extern int SetWindowLong32(
        IntPtr windowHandle,
        int index,
        int newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRect rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
