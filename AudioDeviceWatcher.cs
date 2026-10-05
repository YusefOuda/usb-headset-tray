using System.Runtime.InteropServices;

namespace UsbHeadsetTray;

// Receives endpoint notifications from the audio service and re-raises them on the UI thread.
sealed class AudioDeviceWatcher : IMMNotificationClient, IDisposable
{
    readonly IMMDeviceEnumerator _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
    readonly SynchronizationContext _ui;
    bool _registered;

    // Fires for capture devices too; handlers filter by device id.
    public event Action<string, DeviceState>? DeviceStateChanged;
    // eMultimedia role only (this app sets all roles together). Fires for changes made by this app as well.
    public event Action<EDataFlow, string?>? DefaultDeviceChanged;

    public AudioDeviceWatcher(SynchronizationContext ui)
    {
        _ui = ui;
        _registered = _enumerator.RegisterEndpointNotificationCallback(this) == 0;
    }

    // Callbacks run on audio service threads and must not block, so only post to the UI thread here.
    int IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        _ui.Post(_ => DeviceStateChanged?.Invoke(deviceId, newState), null);
        return 0;
    }

    int IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
    {
        if (flow is EDataFlow.eRender or EDataFlow.eCapture && role == ERole.eMultimedia)
            _ui.Post(_ => DefaultDeviceChanged?.Invoke(flow, defaultDeviceId), null);
        return 0;
    }

    int IMMNotificationClient.OnDeviceAdded(string deviceId) => 0;
    int IMMNotificationClient.OnDeviceRemoved(string deviceId) => 0;
    int IMMNotificationClient.OnPropertyValueChanged(string deviceId, PROPERTYKEY key) => 0;

    public void Dispose()
    {
        if (_registered)
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
            _registered = false;
        }
        Marshal.ReleaseComObject(_enumerator);
    }
}
