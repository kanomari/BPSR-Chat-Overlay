using System;
using System.Threading;
using System.Windows;

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

            MainWindow = new MainWindow();
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_ownsSingleInstanceMutex)
            {
                _singleInstanceMutex?.ReleaseMutex();
                _ownsSingleInstanceMutex = false;
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;

            base.OnExit(e);
        }
    }

}
