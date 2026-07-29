using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows.Media;
using Serilog;

namespace BPSRChatOverlay;

public sealed class NotificationSoundPlayer : IDisposable
{
    private readonly MediaPlayer _mediaPlayer = new();
    private bool _fallbackPlayed;
    private bool _disposed;

    public NotificationSoundPlayer()
    {
        _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
        _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
        _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
    }

    public void Play(string? filePath)
    {
        ThrowIfDisposed();
        StopAndClose();
        _fallbackPlayed = false;

        string path = filePath?.Trim() ?? string.Empty;

        if (path.Length == 0)
        {
            PlaySystemSoundOnce();
            return;
        }

        string extension = Path.GetExtension(path);

        if ((!extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
             !extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) ||
            !File.Exists(path))
        {
            Log.Warning(
                "Notification sound file is unavailable. Path: {Path}",
                path);
            PlaySystemSoundOnce();
            return;
        }

        try
        {
            _mediaPlayer.Open(new Uri(Path.GetFullPath(path)));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open notification sound. Path: {Path}", path);
            PlaySystemSoundOnce();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mediaPlayer.MediaOpened -= MediaPlayer_MediaOpened;
        _mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
        _mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
        StopAndClose();
    }

    private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
    {
        try
        {
            _mediaPlayer.Position = TimeSpan.Zero;
            _mediaPlayer.Play();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to play notification sound");
            PlaySystemSoundOnce();
        }
    }

    private void MediaPlayer_MediaFailed(
        object? sender,
        ExceptionEventArgs e)
    {
        Log.Warning(
            e.ErrorException,
            "Notification sound playback failed");
        StopAndClose();
        PlaySystemSoundOnce();
    }

    private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
    {
        _mediaPlayer.Stop();
        _mediaPlayer.Position = TimeSpan.Zero;
    }

    private void PlaySystemSoundOnce()
    {
        if (_fallbackPlayed)
        {
            return;
        }

        _fallbackPlayed = true;
        SystemSounds.Asterisk.Play();
    }

    private void StopAndClose()
    {
        try
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to stop notification sound: {ex}");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
