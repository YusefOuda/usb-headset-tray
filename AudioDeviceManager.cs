using System.Runtime.InteropServices;

namespace UsbHeadsetTray;

record AudioDevice(string Id, string Name, DeviceState State = DeviceState.Active);

static class AudioDeviceManager
{
    // flow: eRender for playback (output), eCapture for recording (input)
    public static List<AudioDevice> GetDevices(EDataFlow flow, DeviceState stateMask = DeviceState.Active)
    {
        var devices = new List<AudioDevice>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            enumerator.EnumAudioEndpoints(flow, stateMask, out var collection);
            collection.GetCount(out var count);
            for (int i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                device.GetId(out var id);
                device.GetState(out var state);
                devices.Add(new AudioDevice(id, GetFriendlyName(device) ?? id, state));
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(collection);
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
        return devices;
    }

    // Property store can be unavailable for non-present devices
    static string? GetFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(StorageAccessMode.Read, out var props) != 0) return null;
        try
        {
            var key = PropertyKeys.PKEY_Device_FriendlyName;
            return props.GetValue(ref key, out var pv) == 0 ? pv.GetString() : null;
        }
        finally
        {
            Marshal.ReleaseComObject(props);
        }
    }

    public static DeviceState? GetDeviceState(string deviceId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDevice(deviceId, out var device) != 0) return null;
            device.GetState(out var state);
            Marshal.ReleaseComObject(device);
            return state;
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    public static string? GetDefaultDeviceId(EDataFlow flow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            enumerator.GetDefaultAudioEndpoint(flow, ERole.eMultimedia, out var device);
            device.GetId(out var id);
            Marshal.ReleaseComObject(device);
            return id;
        }
        catch { return null; }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    // Works for playback and recording devices alike; the id identifies the endpoint
    public static void SetDefaultDevice(string deviceId)
    {
        var policy = (IPolicyConfig)new CPolicyConfigClient();
        try
        {
            policy.SetDefaultEndpoint(deviceId, ERole.eConsole);
            policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            policy.SetDefaultEndpoint(deviceId, ERole.eCommunications);
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }
}
