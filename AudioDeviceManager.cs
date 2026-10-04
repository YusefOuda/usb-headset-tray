using System.Runtime.InteropServices;

namespace UsbHeadsetTray;

record AudioDevice(string Id, string Name);

static class AudioDeviceManager
{
    public static List<AudioDevice> GetPlaybackDevices()
    {
        var devices = new List<AudioDevice>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.Active, out var collection);
            collection.GetCount(out var count);
            for (int i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                device.GetId(out var id);
                device.OpenPropertyStore(StorageAccessMode.Read, out var props);
                var key = PropertyKeys.PKEY_Device_FriendlyName;
                props.GetValue(ref key, out var pv);
                devices.Add(new AudioDevice(id, pv.GetString() ?? id));
                Marshal.ReleaseComObject(props);
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

    public static string? GetDefaultPlaybackDeviceId()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
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

    public static void SetDefaultPlaybackDevice(string deviceId)
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
