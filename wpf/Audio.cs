using System;
using System.Runtime.InteropServices;

namespace MacDock;

// Core Audio: the current default multimedia output is resolved per operation,
// so unplugging a headset does not leave the dock attached to an old device.
public static class Audio
{
    public static double Next(double current, string action, int step) => Math.Clamp(current + (action == "up" ? step : -step), 0, 100);
    public static (int Level, bool Muted) Read() => WithEndpoint(e => { Marshal.ThrowExceptionForHR(e.GetMasterVolumeLevelScalar(out float v)); Marshal.ThrowExceptionForHR(e.GetMute(out bool m)); return ((int)Math.Round(v * 100), m); });
    public static (int Level, bool Muted) Change(string action, int step)
    {
        if (!new[] { "up", "down", "mute" }.AsSpan().Contains(action) || step < 1 || step > 100) throw new ArgumentException("볼륨 옵션이 올바르지 않습니다.");
        return WithEndpoint(e => {
            Marshal.ThrowExceptionForHR(e.GetMasterVolumeLevelScalar(out float v)); Marshal.ThrowExceptionForHR(e.GetMute(out bool muted));
            if (action == "mute") Marshal.ThrowExceptionForHR(e.SetMute(!muted, IntPtr.Zero));
            else Marshal.ThrowExceptionForHR(e.SetMasterVolumeLevelScalar((float)(Next(v * 100, action, step) / 100), IntPtr.Zero));
            Marshal.ThrowExceptionForHR(e.GetMasterVolumeLevelScalar(out v)); Marshal.ThrowExceptionForHR(e.GetMute(out muted));
            return ((int)Math.Round(v * 100), muted);
        });
    }
    static T WithEndpoint<T>(Func<IAudioEndpointVolume, T> operation)
    {
        object? enumerator = null; IMMDevice? device = null; object? endpoint = null;
        try {
            enumerator = new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out endpoint));
            return operation((IAudioEndpointVolume)endpoint);
        }
        finally {
            if (endpoint != null) Marshal.ReleaseComObject(endpoint);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice
    { [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object result); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channels);
        [PreserveSig] int SetMasterVolumeLevel(float level, IntPtr context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, IntPtr context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
