using System.Runtime.InteropServices;
using BPSRChatOverlay.Config;
using Serilog;

namespace BPSRChatOverlay.Hotkeys;

internal enum HotkeyRegistrationState
{
    Registered,
    NotConfigured,
    Failed
}

internal sealed record HotkeyRegistrationResult(
    HotkeyAction Action,
    HotkeyRegistrationState State,
    HotkeyGesture? Gesture = null,
    int ErrorCode = 0);

internal sealed class HotkeyUpdatePreparation
{
    public HotkeyUpdatePreparation(
        IReadOnlyList<HotkeyRegistrationResult> results,
        PreparedHotkeyUpdate? preparedUpdate)
    {
        Results = results;
        PreparedUpdate = preparedUpdate;
    }

    public IReadOnlyList<HotkeyRegistrationResult> Results { get; }

    public PreparedHotkeyUpdate? PreparedUpdate { get; }

    public bool IsSuccess => PreparedUpdate is not null;
}

internal sealed class PreparedHotkeyUpdate : IDisposable
{
    private GlobalHotkeyManager? _manager;

    internal PreparedHotkeyUpdate(
        GlobalHotkeyManager manager,
        IReadOnlyDictionary<int, HotkeyAction> desiredActions,
        IReadOnlyCollection<int> createdRegistrationIds)
    {
        _manager = manager;
        DesiredActions = desiredActions;
        CreatedRegistrationIds = createdRegistrationIds;
    }

    internal IReadOnlyDictionary<int, HotkeyAction> DesiredActions { get; }

    internal IReadOnlyCollection<int> CreatedRegistrationIds { get; }

    public void Commit()
    {
        GlobalHotkeyManager manager = _manager ??
            throw new InvalidOperationException(
                "The prepared hotkey update is no longer active.");
        _manager = null;
        manager.Commit(this);
    }

    public void Rollback()
    {
        GlobalHotkeyManager? manager = Interlocked.Exchange(
            ref _manager,
            null);
        manager?.Rollback(this);
    }

    public void Dispose()
    {
        Rollback();
    }
}

internal sealed class GlobalHotkeyManager : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const int ClickThroughPreferredId = 0x4250;
    private const int CollapsePreferredId = 0x4249;

    private static readonly int[] RegistrationIds =
    [
        ClickThroughPreferredId,
        CollapsePreferredId,
        0x4251,
        0x4252,
        0x4253,
        0x4254,
        0x4255,
        0x4256
    ];

    private readonly IntPtr _windowHandle;
    private readonly Dictionary<int, HotkeyGesture> _registrations = [];
    private readonly Dictionary<int, HotkeyAction> _activeActions = [];
    private bool _disposed;

    public GlobalHotkeyManager(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "A valid window handle is required.",
                nameof(windowHandle));
        }

        _windowHandle = windowHandle;
    }

    public IReadOnlyList<HotkeyRegistrationResult> RegisterInitial(
        HotkeySettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);

        var results = new List<HotkeyRegistrationResult>(2)
        {
            RegisterInitial(
                HotkeyAction.ClickThroughToggle,
                settings.ClickThroughToggle,
                ClickThroughPreferredId),
            RegisterInitial(
                HotkeyAction.CollapseToggle,
                settings.CollapseToggle,
                CollapsePreferredId)
        };

        return results;
    }

    public HotkeyUpdatePreparation PrepareUpdate(HotkeySettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);

        var requested = new (HotkeyAction Action, HotkeyGestureConfig Config)[]
        {
            (HotkeyAction.ClickThroughToggle, settings.ClickThroughToggle),
            (HotkeyAction.CollapseToggle, settings.CollapseToggle)
        };
        var desiredActions = new Dictionary<int, HotkeyAction>();
        var createdRegistrationIds = new List<int>(2);
        var results = new List<HotkeyRegistrationResult>(2);

        foreach ((HotkeyAction action, HotkeyGestureConfig config) in requested)
        {
            if (!HotkeyGesture.TryCreate(config, out HotkeyGesture gesture))
            {
                results.Add(new HotkeyRegistrationResult(
                    action,
                    HotkeyRegistrationState.NotConfigured));
                continue;
            }

            int existingId = FindRegistrationId(gesture);
            if (existingId >= 0)
            {
                desiredActions.Add(existingId, action);
                results.Add(new HotkeyRegistrationResult(
                    action,
                    HotkeyRegistrationState.Registered,
                    gesture));
                continue;
            }

            int registrationId = GetFreeRegistrationId();
            if (registrationId < 0)
            {
                const int notEnoughMemoryError = 8;
                LogRegistrationFailure(action, gesture, notEnoughMemoryError);
                results.Add(new HotkeyRegistrationResult(
                    action,
                    HotkeyRegistrationState.Failed,
                    gesture,
                    notEnoughMemoryError));
                continue;
            }

            if (!TryRegister(
                    registrationId,
                    action,
                    gesture,
                    out int errorCode))
            {
                results.Add(new HotkeyRegistrationResult(
                    action,
                    HotkeyRegistrationState.Failed,
                    gesture,
                    errorCode));
                continue;
            }

            createdRegistrationIds.Add(registrationId);
            desiredActions.Add(registrationId, action);
            results.Add(new HotkeyRegistrationResult(
                action,
                HotkeyRegistrationState.Registered,
                gesture));
        }

        if (results.Any(result =>
                result.State == HotkeyRegistrationState.Failed))
        {
            RollbackRegistrations(createdRegistrationIds);
            return new HotkeyUpdatePreparation(results, null);
        }

        return new HotkeyUpdatePreparation(
            results,
            new PreparedHotkeyUpdate(
                this,
                desiredActions,
                createdRegistrationIds));
    }

    public bool TryGetAction(int registrationId, out HotkeyAction action)
    {
        return _activeActions.TryGetValue(registrationId, out action);
    }

    internal void Commit(PreparedHotkeyUpdate update)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _activeActions.Clear();
        foreach ((int registrationId, HotkeyAction action) in
                 update.DesiredActions)
        {
            _activeActions.Add(registrationId, action);
        }

        HashSet<int> desiredIds = update.DesiredActions.Keys.ToHashSet();
        int[] obsoleteIds = _registrations.Keys
            .Where(id => !desiredIds.Contains(id))
            .ToArray();

        foreach (int obsoleteId in obsoleteIds)
        {
            TryUnregister(obsoleteId, "obsolete hotkey after commit");
        }
    }

    internal void Rollback(PreparedHotkeyUpdate update)
    {
        if (_disposed)
        {
            return;
        }

        RollbackRegistrations(update.CreatedRegistrationIds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeActions.Clear();

        foreach (int registrationId in _registrations.Keys.ToArray())
        {
            TryUnregister(registrationId, "application shutdown");
        }

        _registrations.Clear();
    }

    private HotkeyRegistrationResult RegisterInitial(
        HotkeyAction action,
        HotkeyGestureConfig config,
        int preferredId)
    {
        if (!HotkeyGesture.TryCreate(config, out HotkeyGesture gesture))
        {
            return new HotkeyRegistrationResult(
                action,
                HotkeyRegistrationState.NotConfigured);
        }

        int registrationId = GetFreeRegistrationId(preferredId);
        if (registrationId < 0)
        {
            const int notEnoughMemoryError = 8;
            LogRegistrationFailure(action, gesture, notEnoughMemoryError);
            return new HotkeyRegistrationResult(
                action,
                HotkeyRegistrationState.Failed,
                gesture,
                notEnoughMemoryError);
        }

        if (!TryRegister(
                registrationId,
                action,
                gesture,
                out int errorCode))
        {
            return new HotkeyRegistrationResult(
                action,
                HotkeyRegistrationState.Failed,
                gesture,
                errorCode);
        }

        _activeActions.Add(registrationId, action);
        return new HotkeyRegistrationResult(
            action,
            HotkeyRegistrationState.Registered,
            gesture);
    }

    private bool TryRegister(
        int registrationId,
        HotkeyAction action,
        HotkeyGesture gesture,
        out int errorCode)
    {
        uint modifiers = ModNoRepeat;
        if (gesture.Control)
        {
            modifiers |= ModControl;
        }

        if (gesture.Shift)
        {
            modifiers |= ModShift;
        }

        if (gesture.Alt)
        {
            modifiers |= ModAlt;
        }

        if (!RegisterHotKey(
                _windowHandle,
                registrationId,
                modifiers,
                (uint)gesture.VirtualKey))
        {
            errorCode = Marshal.GetLastWin32Error();
            LogRegistrationFailure(action, gesture, errorCode);
            return false;
        }

        errorCode = 0;
        _registrations.Add(registrationId, gesture);
        return true;
    }

    private void LogRegistrationFailure(
        HotkeyAction action,
        HotkeyGesture gesture,
        int errorCode)
    {
        Log.Warning(
            "Failed to register global hotkey. Action: {Action}, Hotkey: {Hotkey}, Win32ErrorCode: {Win32ErrorCode}",
            HotkeyUtilities.GetActionDisplayName(action),
            HotkeyUtilities.FormatGesture(gesture),
            errorCode);
    }

    private int FindRegistrationId(HotkeyGesture gesture)
    {
        foreach ((int registrationId, HotkeyGesture registeredGesture) in
                 _registrations)
        {
            if (registeredGesture == gesture)
            {
                return registrationId;
            }
        }

        return -1;
    }

    private int GetFreeRegistrationId(int? preferredId = null)
    {
        if (preferredId is { } preferred &&
            !_registrations.ContainsKey(preferred))
        {
            return preferred;
        }

        return RegistrationIds.FirstOrDefault(
            id => !_registrations.ContainsKey(id),
            -1);
    }

    private void RollbackRegistrations(
        IEnumerable<int> createdRegistrationIds)
    {
        foreach (int registrationId in createdRegistrationIds.Reverse())
        {
            TryUnregister(registrationId, "hotkey update rollback");
        }
    }

    private bool TryUnregister(int registrationId, string reason)
    {
        if (!_registrations.ContainsKey(registrationId))
        {
            return true;
        }

        if (!UnregisterHotKey(_windowHandle, registrationId))
        {
            int errorCode = Marshal.GetLastWin32Error();
            Log.Warning(
                "Failed to unregister global hotkey. RegistrationId: {RegistrationId}, Reason: {Reason}, Win32ErrorCode: {Win32ErrorCode}",
                registrationId,
                reason,
                errorCode);
            return false;
        }

        _registrations.Remove(registrationId);
        _activeActions.Remove(registrationId);
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(
        IntPtr windowHandle,
        int id);
}
