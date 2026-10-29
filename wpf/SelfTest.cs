using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MacDock;

public static class SelfTest
{
    public static int Run()
    {
        string output = Path.Combine(Environment.CurrentDirectory,"wpf","test-output"); Directory.CreateDirectory(output);
        try {
            int passed = 0;
            void Check(bool condition,string name) { if (!condition) throw new Exception(name); passed++; }
            void Reject(Action test,string name) { try { test(); } catch { passed++; return; } throw new Exception(name); }
            Check(UsbEject.SelectTarget(new UsbEject.Node[] { new(1,"SCSI\\disk","disk",0), new(2,"USB\\VID_1234","UASPStor",4) })?.DevInst == 2, "UASP external SSD selection");
            Check(UsbEject.SelectTarget(new UsbEject.Node[] { new(1,"USBSTOR\\disk","disk",4), new(2,"USB\\VID_1234","USBSTOR",4) })?.DevInst == 1, "USB flash nearest removable node");
            Check(UsbEject.SelectTarget(new UsbEject.Node[] { new(1,"SCSI\\disk","disk",4), new(2,"PCI\\disk","storahci",0) }) == null, "Exclude internal SATA disk");
            Check(UsbEject.SelectTarget(new UsbEject.Node[] { new(1,"SCSI\\disk","disk",0), new(2,"USB\\VID_1234","USBSTOR",0), new(3,"USB\\hub","USBHUB3",4) }) == null, "Never eject USB hub");
            Check(UsbEject.SelectTarget(new UsbEject.Node[] { new(1,"SCSI\\disk","disk",0), new(2,"USB\\VID_1234&MI_00","UASPStor",0), new(3,"USB\\VID_1234","usbccgp",4) }) == null, "Never eject composite parent");
            int ejectCalls = 0;
            var ejectResult = UsbEject.Eject(new[] { new UsbEject.Device("one","USB A"), new UsbEject.Device("ONE","USB A partition 2"), new UsbEject.Device("two","USB B"), new UsbEject.Device("three","USB C") }, d => { ejectCalls++; if (d.Id == "one") return "사용 중"; if (d.Id == "two") throw new Exception("분리됨"); return null; });
            Check(ejectCalls == 3 && ejectResult.Contains("완료 1개 / 실패 2개") && ejectResult.Contains("USB C"), "Deduplicate devices and continue after veto or unplug");
            Check(UsbEject.Eject(Array.Empty<UsbEject.Device>(), _ => throw new Exception()).Contains("없습니다"), "No USB devices");
            var usbConfig = new DockConfig { Buttons = new() { new DockItem { Type = "usb-eject", Label = "USB 모두 제거" } } }; usbConfig.Validate();
            Check(usbConfig.Clone().Buttons.Single().Type == "usb-eject" && Presets.Items.Any(p => p.Type == "usb-eject"), "USB config and preset round trip");
            File.WriteAllText(Path.Combine(output,"usb-discovery.txt"), string.Join("\n", UsbEject.Discover().Select(d => d.Name + " | " + d.Id)));
            Check(true, "Native USB discovery (read only; no devices ejected)");

            var config = DockConfig.Default(); config.Validate(); Check(config.Buttons.Any(b => b.Type == "volume"),"Volume defaults");
            Check(System.Text.Json.JsonSerializer.Deserialize<DockConfig>("{\"bottom\":17}", Store.Json) is { Position: "bottom", Bottom: 17 }, "Legacy dock position");
            foreach (string position in new[] { "bottom", "top", "left", "right" }) {
                var positioned = config.Clone(); positioned.Position = position; positioned.Validate();
                Store.Folder = Path.Combine(output,"position-profile"); Store.Save(positioned);
                Check(Store.Load().Position == position && positioned.Clone().Position == position, "Position persistence: " + position);
            }
            var invalidPosition = config.Clone(); invalidPosition.Position = "diagonal"; Reject(invalidPosition.Validate, "Invalid dock position");
            Check(DockWindow.PageCapacity(720, 52, 8, 1.7, 100) >= 8, "Vertical capacity at 150 percent DPI");
            Check(DockWindow.PageCapacity(500, 52, 8, 1.7, 100) >= 5, "Short screen capacity");
            Check(DockWindow.PageCapacity(720, 52, 8, 1.7, 8) == 8, "No unnecessary page arrows");
            Check(DockWindow.ArrowLength(52) == 20 && DockWindow.ArrowLength(80) == 28, "Compact arrow buttons");
            var steps = Macro.Parse("Ctrl+C\nwait 300\nCtrl+V\ntext 안녕하세요"); Check(steps.Count == 4 && steps[0].Keys!.SequenceEqual(new ushort[] {17,67}) && steps[3].Text == "안녕하세요","Macro sequence");
            Reject(() => Macro.Parse("Ctrl+Ctrl+C"),"Duplicate modifiers"); Reject(() => Macro.Parse("wait 10001"),"Long delay"); Reject(() => Macro.Parse("NoSuchKey"),"Invalid key");
            Check(Marshal.SizeOf<Native.Input>() == 40,"x64 INPUT ABI");
            Check(Audio.Next(98,"up",5) == 100 && Audio.Next(2,"down",5) == 0 && Audio.Next(40,"up",5) == 45,"Volume step/clamp");
            Check(Metrics.Percent(800,1000,850,1200) == 75,"CPU delta");
            Check(MacroRecorder.Chord(System.Windows.Input.Key.S,System.Windows.Input.ModifierKeys.Control) == "Ctrl+S", "Record Ctrl+S");
            Check(MacroRecorder.Chord(System.Windows.Input.Key.LeftCtrl,System.Windows.Input.ModifierKeys.Control) == null, "Ignore lone modifier");
            Check(MacroRecorder.Chord(System.Windows.Input.Key.E,System.Windows.Input.ModifierKeys.Windows) == null, "Win combinations require manual entry");
            var punctuation = MacroRecorder.Chord(System.Windows.Input.Key.OemPlus,System.Windows.Input.ModifierKeys.Shift)!;
            Check(Macro.Parse(punctuation)[0].Keys!.SequenceEqual(new ushort[] {16,187}), "Recorded punctuation round trip");
            foreach (int index in Enumerable.Range(0,IconLibrary.Items.Length)) { IconLibrary.Drawing(index); ImageFrames.Load(IconLibrary.DataUrl(index)); }
            Check(IconLibrary.Items.Length >= 36 && IconLibrary.Colors.Length == 24, "Embedded icon artwork and palette decode");
            Check(HardwareMetrics.Rate(100, 2100, 2) == 1000 && HardwareMetrics.Rate(100, 5, 1) == 0 && HardwareMetrics.Rate(0, 1, 0) == 0, "Network deltas and reset");
            Check(HardwareMetrics.Speed(null) == "—" && HardwareMetrics.Speed(1500000).EndsWith("MB/s") && HardwareMetrics.Speed(1500).EndsWith("KB/s"), "Network units and unavailable state");
            var hardware = new HardwareMetrics(); hardware.Sample(true, true); System.Threading.Thread.Sleep(1100); hardware.Sample(false, true);
            File.WriteAllText(Path.Combine(output,"hardware-result.txt"), $"GPU={hardware.Gpu}; temperature={hardware.Temperature}; {hardware.GpuInfo}\nUP={HardwareMetrics.Speed(hardware.Upload)}; DOWN={HardwareMetrics.Speed(hardware.Download)}; {hardware.NetworkInfo}");
            double a = 1, b = 1;
            for (int i = 0; i < 6; i++) a = DockWindow.Follow(a, 2, 1d / 60);
            for (int i = 0; i < 12; i++) b = DockWindow.Follow(b, 2, 1d / 120);
            Check(Math.Abs(a - b) < .00001 && a > 1.89, "Refresh-rate independent response");
            foreach (var preset in Enumerable.Range(0,Presets.Items.Length).Select(Presets.Create)) { var testConfig = new DockConfig { Buttons = new() { preset } }; testConfig.Validate(); }
            Check(Presets.Items.Any(p => p.Target == "calc.exe") && Presets.Items.Any(p => p.Target == "mspaint.exe"), "Windows quick-add presets");
            Store.Folder = Path.Combine(output,"self-test-profile"); Store.Save(config); var restored = Store.Load(); Check(restored.Buttons.Count == config.Buttons.Count,"Settings round trip");
            restored.Buttons.Last().VolumeStep = 9; Store.Save(restored); Check(Store.Load().Buttons.Last().VolumeStep == 9 && File.Exists(Store.FileName + ".bak"),"Atomic save and backup");
            restored.Buttons.Last().VolumeStep = 0; Reject(restored.Validate,"Volume validation");
            var importPath = Path.Combine(output,"electron-fixture.json"); File.WriteAllText(importPath,"{\"size\":52,\"buttons\":[{\"type\":\"extension\"},{\"type\":\"macro\",\"target\":\"Ctrl+C\"}]}"); var imported = Store.Import(importPath); Check(imported.Skipped == 1 && imported.Config.Buttons.Count == 1,"Electron import");
            File.WriteAllText(Path.Combine(output,"tests-result.txt"),$"PASS: {passed} checks. No keys sent; no system volume changes; no USB devices ejected."); return 0;
        } catch (Exception error) { File.WriteAllText(Path.Combine(output,"tests-result.txt"),error.ToString()); return 1; }
    }
}
