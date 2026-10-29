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
            var config = DockConfig.Default(); config.Validate(); Check(config.Buttons.Any(b => b.Type == "volume"),"Volume defaults");
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
            File.WriteAllText(Path.Combine(output,"tests-result.txt"),$"PASS: {passed} checks. No keys sent; no system volume changes."); return 0;
        } catch (Exception error) { File.WriteAllText(Path.Combine(output,"tests-result.txt"),error.ToString()); return 1; }
    }
}

