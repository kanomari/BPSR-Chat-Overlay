using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using BPSRChatOverlay.Updates;
using Serilog;
using Serilog.Events;

namespace BPSRChatOverlay
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string MutexName = "BPSRChatOverlay.SingleInstance";

        private Mutex? _singleInstanceMutex;
        private bool _ownsSingleInstanceMutex;
        private bool _fileLoggingInitialized;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // パケットキャプチャの開始前に二重起動を防止します。
                _singleInstanceMutex = new Mutex(false, MutexName);

                try
                {
                    _ownsSingleInstanceMutex =
                        _singleInstanceMutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    _ownsSingleInstanceMutex = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"二重起動の確認中にエラーが発生しました。\n\n{ex.Message}",
                    "BPSR Chat Overlay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            if (!_ownsSingleInstanceMutex)
            {
                MessageBox.Show(
                    "BPSR Chat Overlay は既に起動しています。",
                    "BPSR Chat Overlay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            InitializeLogging();

            Log.Information(
                "Application started. Version: {Version}, OS: {OS}, ProcessBitness: {ProcessBitness}-bit, LogDirectory: {LogDirectory}",
                AppVersionProvider.CurrentVersionText,
                Environment.OSVersion.VersionString,
                Environment.Is64BitProcess ? 64 : 32,
                AppPaths.LogDirectory);

            MainWindow = new MainWindow();
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_fileLoggingInitialized)
            {
                try
                {
                    Log.Information("Application shutdown completed");
                    Log.CloseAndFlush();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to flush file logging: {ex}");
                }

                _fileLoggingInitialized = false;
            }

            if (_ownsSingleInstanceMutex)
            {
                _singleInstanceMutex?.ReleaseMutex();
                _ownsSingleInstanceMutex = false;
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;

            base.OnExit(e);
        }

        private void InitializeLogging()
        {
            string logDirectory = AppPaths.LogDirectory;

            try
            {
                Directory.CreateDirectory(logDirectory);

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(
                        AppPaths.LogFilePathPattern,
                        restrictedToMinimumLevel: LogEventLevel.Information,
                        outputTemplate:
                            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                        rollingInterval: RollingInterval.Day,
                        fileSizeLimitBytes: 10 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: null,
                        retainedFileTimeLimit: TimeSpan.FromDays(7))
                    .CreateLogger();

                _fileLoggingInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize file logging: {ex}");
                MessageBox.Show(
                    "ログファイルを初期化できませんでした。ファイルログなしで起動します。",
                    "BPSR Chat Overlay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

    }

}
