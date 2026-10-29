using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace MacDock;

public sealed class HardwareMetrics
{
    readonly Dictionary<string, (long Sent, long Received, double Time)> previous = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    double nextGpu;
    public int? Gpu { get; private set; }
    public int? Temperature { get; private set; }
    public string GpuInfo { get; private set; } = "NVIDIA 드라이버의 GPU 측정 기능이 필요합니다.";
    public double? Upload { get; private set; }
    public double? Download { get; private set; }
    public string NetworkInfo { get; private set; } = "연결된 이더넷 없음";
    public static double Rate(long oldValue, long value, double elapsed) => elapsed > 0 && value >= oldValue ? (value - oldValue) / elapsed : 0;
    public static string Speed(double? bytes) => bytes is not { } b ? "—" : b >= 1_000_000 ? (b / 1_000_000).ToString("0.0") + " MB/s" : b >= 1000 ? (b / 1000).ToString("0.0") + " KB/s" : b.ToString("0") + " B/s";
    public void Sample(bool gpu, bool network)
    {
        if (network) SampleNetwork();
        if (gpu && clock.Elapsed.TotalSeconds >= nextGpu) { nextGpu = clock.Elapsed.TotalSeconds + 3; SampleGpu(); }
    }
    void SampleNetwork()
    {
        Upload = Download = null;
        var names = new List<string>(); var seen = new HashSet<string>();
        double up = 0, down = 0, now = clock.Elapsed.TotalSeconds;
        try {
            var physical = PhysicalAdapters();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => physical.Contains(n.Id) && n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT)) {
                try {
                    var stats = adapter.GetIPStatistics(); seen.Add(adapter.Id); names.Add(adapter.Name);
                    if (previous.TryGetValue(adapter.Id, out var old)) { up += Rate(old.Sent, stats.BytesSent, now - old.Time); down += Rate(old.Received, stats.BytesReceived, now - old.Time); Upload = up; Download = down; }
                    previous[adapter.Id] = (stats.BytesSent, stats.BytesReceived, now);
                } catch (NetworkInformationException) { }
            }
            foreach (string id in previous.Keys.Where(id => !seen.Contains(id)).ToArray()) previous.Remove(id);
            NetworkInfo = names.Count == 0 ? "연결된 이더넷 없음" : "이더넷 합계: " + string.Join(", ", names) + " · 초당 바이트 (Wi-Fi 제외)";
        } catch (NetworkInformationException) { previous.Clear(); NetworkInfo = "이더넷 정보를 읽을 수 없습니다."; }
    }
    static HashSet<string> PhysicalAdapters()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // NCF_PHYSICAL excludes virtual switches, VPNs and filter-driver duplicates.
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}");
        if (root == null) return ids;
        foreach (string name in root.GetSubKeyNames()) {
            if (!int.TryParse(name, out _)) continue;
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("Characteristics") is int flags && (flags & 4) != 0 && key.GetValue("NetCfgInstanceId") is string id) ids.Add(id);
        }
        return ids;
    }
    void SampleGpu()
    {
        Gpu = Temperature = null;
        try {
            string exe = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            if (!File.Exists(exe)) { GpuInfo = "GPU 측정 미지원: NVIDIA 드라이버의 nvidia-smi가 필요합니다."; return; }
            using var process = new Process { StartInfo = new ProcessStartInfo(exe, "--id=0 --query-gpu=name,utilization.gpu,temperature.gpu --format=csv,noheader,nounits") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.Start(); var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(1500)) { process.Kill(true); GpuInfo = "GPU 측정 응답 시간 초과"; return; }
            if (process.ExitCode != 0) { GpuInfo = "GPU 드라이버가 측정값을 제공하지 않습니다."; return; }
            var fields = output.GetAwaiter().GetResult().Trim().Split(',');
            if (fields.Length != 3) { GpuInfo = "GPU 측정값을 읽을 수 없습니다."; return; }
            GpuInfo = fields[0].Trim() + " · NVIDIA GPU 0 · 3초 간격";
            if (int.TryParse(fields[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int usage) && usage is >= 0 and <= 100) Gpu = usage;
            if (int.TryParse(fields[2].Trim(), out int temp) && temp is >= 0 and <= 150) Temperature = temp;
        } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { GpuInfo = "GPU 측정 기능을 사용할 수 없습니다."; }
    }
}
