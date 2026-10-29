using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MacDock;

public static class UsbEject
{
    internal record Node(uint DevInst, string Id, string Service, uint Capabilities);
    internal record Device(string Id, string Name);
    static int busy;

    // Stop at the USB storage function: never eject a hub or a composite parent.
    internal static Node? SelectTarget(IReadOnlyList<Node> ancestry)
    {
        int storage = -1;
        for (int i = 0; i < ancestry.Count; i++) {
            var n = ancestry[i];
            if (n.Service.Equals("USBSTOR", StringComparison.OrdinalIgnoreCase) || n.Service.Equals("UASPStor", StringComparison.OrdinalIgnoreCase)) { storage = i; break; }
            if (n.Id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) break;
        }
        if (storage < 0) return null;
        return ancestry.Take(storage + 1).FirstOrDefault(n => (n.Capabilities & 4) != 0);
    }

    public static async Task<string> Run()
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new Exception("USB 안전 제거가 진행 중입니다.");
        try { return await Task.Run(() => Eject(Discover(), Request)); }
        finally { Volatile.Write(ref busy, 0); }
    }

    internal static string Eject(IEnumerable<Device> devices, Func<Device, string?> request)
    {
        var results = new List<string>(); int success = 0, failed = 0;
        foreach (var device in devices.DistinctBy(d => d.Id, StringComparer.OrdinalIgnoreCase)) {
            string? error;
            try { error = request(device); } catch (Exception ex) { error = ex.Message; }
            if (error == null) { success++; results.Add("제거 완료: " + device.Name); }
            else { failed++; results.Add("제거 실패: " + device.Name + " — " + error); }
        }
        return results.Count == 0 ? "안전하게 제거할 수 있는 USB 저장장치가 없습니다." :
            $"USB 안전 제거: 완료 {success}개 / 실패 {failed}개\n\n" + string.Join("\n", results) +
            (failed > 0 ? "\n\n실패한 장치는 연결 상태로 남아 있습니다. 파일과 사용 중인 앱을 닫고 다시 시도하세요." : "\n\n완료된 장치는 분리해도 됩니다.");
    }

    internal static List<Device> Discover()
    {
        Guid diskClass = new("4d36e967-e325-11ce-bfc1-08002be10318");
        IntPtr set = SetupDiGetClassDevsW(ref diskClass, null, IntPtr.Zero, 2);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var devices = new List<Device>();
        try {
            for (uint i = 0; ; i++) {
                var info = new DevInfo { Size = (uint)Marshal.SizeOf<DevInfo>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref info)) {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                var ancestry = new List<Node>(); uint current = info.DevInst;
                for (int depth = 0; depth < 32; depth++) {
                    string id = DeviceId(current);
                    var caps = Property(current, 16); // CM_DRP_CAPABILITIES
                    ancestry.Add(new(current, id, TextProperty(current, 5), caps.Length >= 4 ? BitConverter.ToUInt32(caps) : 0));
                    if (id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) break;
                    if (CM_Get_Parent(out uint parent, current, 0) != 0) break;
                    current = parent;
                }
                var target = SelectTarget(ancestry);
                if (target == null) continue;
                string name = TextProperty(info.DevInst, 13); // CM_DRP_FRIENDLYNAME
                if (string.IsNullOrWhiteSpace(name)) name = TextProperty(info.DevInst, 1);
                devices.Add(new(target.Id, string.IsNullOrWhiteSpace(name) ? target.Id : name));
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        return devices;
    }

    static string? Request(Device device)
    {
        // Resolve again because an earlier request or physical unplug can invalidate DEVINSTs.
        uint found = CM_Locate_DevNodeW(out uint node, device.Id, 0);
        if (found != 0) return $"장치가 사라졌거나 확인할 수 없습니다 (0x{found:X}).";
        var vetoName = new StringBuilder(1024);
        uint result = CM_Request_Device_EjectW(node, out uint veto, vetoName, (uint)vetoName.Capacity, 0);
        if (result == 0) return null;
        string reason = veto switch {
            4 or 5 or 6 or 8 or 9 => "앱 또는 시스템이 장치를 사용 중입니다",
            10 => "드라이버가 안전 제거를 지원하지 않습니다",
            12 => "안전 제거 권한이 없습니다",
            _ => "Windows가 안전 제거를 허용하지 않았습니다"
        };
        return $"{reason} (코드 0x{result:X}, 사유 {veto})" + (vetoName.Length > 0 ? ": " + vetoName : "");
    }

    static string DeviceId(uint node)
    {
        var value = new StringBuilder(512);
        uint result = CM_Get_Device_IDW(node, value, (uint)value.Capacity, 0);
        if (result != 0) throw new Exception($"USB 장치 정보를 읽지 못했습니다 (0x{result:X}). 다시 시도하세요.");
        return value.ToString();
    }
    static byte[] Property(uint node, uint property)
    {
        uint size = 8192; var bytes = new byte[size];
        if (CM_Get_DevNode_Registry_PropertyW(node, property, out _, bytes, ref size, 0) != 0) return Array.Empty<byte>();
        return bytes[..(int)size];
    }
    static string TextProperty(uint node, uint property) => Encoding.Unicode.GetString(Property(node, property)).TrimEnd('\0');

    [StructLayout(LayoutKind.Sequential)]
    struct DevInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DevInfo info);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_Device_IDW(uint node, StringBuilder buffer, uint length, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint property, out uint type, byte[] buffer, ref uint size, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Request_Device_EjectW(uint node, out uint veto, StringBuilder name, uint length, uint flags);
}
