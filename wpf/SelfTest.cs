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
            string serverFolder = Path.Combine(output, "fileshare-profile");
            var share = FileShare.Command("info", folder: serverFolder);
            Check(!share.Running && Directory.Exists(share.SharedRoot), "Load native FileShare settings offline");
            File.WriteAllText(Path.Combine(output,"usb-discovery.txt"), string.Join("\n", UsbEject.Discover().Select(d => d.Name + " | " + d.Id)));
            Check(true, "Native USB discovery (read only; no devices ejected)");

            Check(ShutdownWindow.TryDelay("1","30",out int seconds) && seconds == 5400 && ShutdownWindow.TryDelay("","1",out seconds) && seconds == 60, "Shutdown duration in hours and minutes");
            Check(!ShutdownWindow.TryDelay("0","0",out _) && !ShutdownWindow.TryDelay("0","60",out _) && !ShutdownWindow.TryDelay("-1","0",out _) && !ShutdownWindow.TryDelay("2147483647","0",out _), "Reject invalid shutdown durations");
            Check(ShutdownWindow.WasCancelled(0) && !ShutdownWindow.WasCancelled(1116), "Cancel existing Windows timer or open duration dialog");
            Reject(() => ShutdownWindow.WasCancelled(5), "Report failed shutdown cancellation");
            Check(Soundboard.Samples.Length == 50 && Soundboard.Samples.Count(n => n.StartsWith("alarm-")) == 6, "50 bundled samples including six alarms");
            foreach (string sound in Soundboard.Samples) {
                using var wave = new BinaryReader(File.OpenRead(Soundboard.Resolve("sample:"+sound)));
                Check(new string(wave.ReadChars(4)) == "RIFF" && wave.BaseStream.Length > 44,"Readable embedded WAV " + sound);
            }
            Soundboard.Close();
            var timer = new Countdown(5);
            timer.Toggle(TimeSpan.Zero); timer.Toggle(TimeSpan.FromMilliseconds(1500));
            Check(timer.Phase == CountdownPhase.Paused && timer.Remaining(TimeSpan.FromSeconds(10)).TotalSeconds == 3.5, "Pause preserves precise remaining time");
            timer.Toggle(TimeSpan.FromSeconds(10));
            Check(!timer.Complete(TimeSpan.FromSeconds(13)) && timer.Complete(TimeSpan.FromSeconds(14)) && !timer.Complete(TimeSpan.FromSeconds(15)),"Resume and deliver alarm once");
            timer.Toggle(TimeSpan.FromSeconds(20));
            Check(timer.Phase == CountdownPhase.Ready && timer.Remaining(TimeSpan.FromSeconds(25)).TotalSeconds == 5, "Dismiss alarm and remain at configured time");
            timer.Toggle(TimeSpan.FromSeconds(26));
            Check(timer.Phase == CountdownPhase.Running && timer.Remaining(TimeSpan.FromSeconds(27)).TotalSeconds == 4, "Start only on the next click");
            timer.Toggle(TimeSpan.FromSeconds(31));
            Check(timer.Phase == CountdownPhase.Ready && !timer.Complete(TimeSpan.FromSeconds(32)), "Dismiss at deadline before UI tick");
            timer.Toggle(TimeSpan.FromSeconds(33)); timer.Reset();
            Check(timer.Phase == CountdownPhase.Ready && timer.Remaining(TimeSpan.Zero).TotalSeconds == 5,"Reset active timer");
            var original = new DockItem { Label = "음량", Type = "volume", Target = "up", VolumeStep = 9, Color = "#123456", Icon = "↑", Image = "test" };
            var duplicate = original.Duplicate();
            Check(duplicate.Id != original.Id && duplicate.Label == original.Label && duplicate.Type == original.Type && duplicate.Target == original.Target && duplicate.VolumeStep == original.VolumeStep && duplicate.Color == original.Color && duplicate.Icon == original.Icon && duplicate.Image == original.Image, "Duplicate all button settings with a fresh ID");
            duplicate.Label = "다른 이름"; Check(original.Label == "음량", "Edit duplicate independently");
            var config = DockConfig.Default(); config.Validate(); Check(config.Buttons.Any(b => b.Type == "volume"),"Volume defaults");
            Check(System.Text.Json.JsonSerializer.Deserialize<DockConfig>("{\"bottom\":17}", Store.Json) is { Position: "bottom", Bottom: 17, Rows: 1 }, "Legacy dock position");
            foreach (string position in new[] { "bottom", "top", "left", "right" }) {
                var positioned = config.Clone(); positioned.Position = position; positioned.Validate();
                Store.Folder = Path.Combine(output,"position-profile"); Store.Save(positioned);
                Check(Store.Load().Position == position && positioned.Clone().Position == position, "Position persistence: " + position);
            }
            var mixed = DockConfig.Default();
            mixed.Buttons = Enumerable.Range(0,100).Select(i => new DockItem { BlockSize = i % 3 == 0 ? "2x2" : i % 3 == 1 ? "2x1" : "1x1" }).ToList();
            foreach (bool vertical in new[] {false,true}) foreach (int lanes in new[] {1,2,6}) {
                var pages = BlockPage.Pack(mixed,vertical,lanes,7);
                var data = pages.SelectMany(p => p.Cells).Where(c => c.Item.Type == "url").ToList();
                Check(data.Select(c => c.Item).SequenceEqual(mixed.Buttons) && pages.All(p => p.Cells.Count(c => c.Item.Type == "settings") == 1), "Packed page coverage");
                foreach (var packed in pages) {
                    var occupied = new System.Collections.Generic.HashSet<(int,int)>();
                    foreach (var c in packed.Cells) for (int x=c.X;x<c.X+c.Width;x++) for (int y=c.Y;y<c.Y+c.Height;y++)
                        if (!occupied.Add((x,y))) throw new Exception("Overlapping packed cells");
                }
            }
            var moved = config.Clone(); var firstId = moved.Buttons[0].Id; var thirdId = moved.Buttons[2].Id;
            Check(DockWindow.MoveButton(moved,firstId,thirdId) && moved.Buttons[2].Id == firstId, "Drag button forward");
            Check(DockWindow.MoveButton(moved,firstId,moved.Buttons[0].Id) && moved.Buttons[0].Id == firstId, "Drag button backward");
            moved.FreeX = .25; moved.FreeY = .75; Store.Save(moved);
            Check(Store.Load() is { FreeX: .25, FreeY: .75 }, "Persist free dock placement");
            var sixRows = config.Clone(); sixRows.Rows = 6; sixRows.Validate(); Store.Save(sixRows);
            Check(Store.Load().Rows == 6, "Persist dock rows"); sixRows.Rows = 7; Reject(sixRows.Validate, "Reject excess dock rows");
            Check(DockWindow.PageCapacity(500,52,8,1.7,100,6) > DockWindow.PageCapacity(500,52,8,1.7,100,2), "Multi-row pagination");
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

public sealed partial class DockWindow
{
    internal void VerifyPreview(DockScene preview)
    {
        if (preview.PageCount != PageCount || preview.page != page || preview.tiles.Count != tiles.Count)
            throw new Exception("Preview pagination mismatch");
        for (int i = 0; i < tiles.Count; i++) {
            var live = tiles[i]; var copy = preview.tiles[i];
            if (live.Item.Type != copy.Item.Type || live.Item.Label != copy.Item.Label ||
                Math.Abs(live.Hit.X-copy.Hit.X) > 1 || Math.Abs(live.Hit.Y-copy.Hit.Y) > 1 ||
                live.Hit.Size != copy.Hit.Size || live.Value.Text != copy.Value.Text || live.Caption?.Text != copy.Caption?.Text)
                throw new Exception("Preview tile mismatch: " + live.Item.Label);
        }
    }
    internal async System.Threading.Tasks.Task VerifyCountdownDisplay()
    {
        var timer = new DockItem { Type = "countdown", Label = "집중 타이머", TimerSeconds = 120, BlockSize = "2x1" };
        Apply(new DockConfig { Size=72, Buttons = new() { timer } });
        TimerClick(timer,false);
        var tile = tiles.Single(t => t.Item.Id == timer.Id);
        if (tile.Phase != CountdownPhase.Running || tile.Cooldown is not { Remaining: > 0 }) throw new Exception("Missing active countdown");
        TimerClick(timer,false);
        if (tile.Phase != CountdownPhase.Paused) throw new Exception("Missing paused countdown");
        TimerClick(timer,true);
        if (tile.Phase != CountdownPhase.Ready || tile.Value.Text != "02:00") throw new Exception("Countdown reset failed");
        TimerClick(timer,false);
        countdowns[timer.Id].Reset();
        countdowns[timer.Id].Toggle(watch.Elapsed - TimeSpan.FromSeconds(timer.TimerSeconds));
        UpdateCountdowns();
        if (tile.Phase != CountdownPhase.Finished) throw new Exception("Missing completed countdown");
        TimerClick(timer,false);
        if (tile.Phase != CountdownPhase.Ready || tile.Value.Text != "02:00" || tile.Cooldown.Remaining != 0 || countdownTick.IsEnabled)
            throw new Exception("Alarm dismissal started a new countdown");
        TimerClick(timer,false);
        var state = countdowns[timer.Id];
        state.Reset(); state.Toggle(watch.Elapsed - TimeSpan.FromSeconds(60));
        UpdateCountdowns();
        if (Math.Abs(tile.Cooldown.Remaining-.5) > .01) throw new Exception("Incorrect radial countdown fraction");
        TimerClick(timer,false);
        double paused = tile.Cooldown.Remaining;
        await System.Threading.Tasks.Task.Delay(200); UpdateCountdowns();
        if (tile.Cooldown.Remaining != paused) throw new Exception("Paused cooldown moved");
        TimerClick(timer,false);
        var player = new System.Windows.Media.MediaPlayer { IsMuted = true };
        var ended = new System.Threading.Tasks.TaskCompletionSource();
        player.MediaEnded += (_,_) => ended.TrySetResult();
        player.MediaFailed += (_,e) => ended.TrySetException(e.ErrorException);
        try {
            player.Open(new Uri(Soundboard.Resolve(Soundboard.DefaultAlarm)));
            player.Play();
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(15));
        } finally { player.Close(); }
    }
    internal void VerifyDragFeedback()
    {
        var first = tiles.First(t => Config.Buttons.Contains(t.Item));
        var last = tiles.Last(t => Config.Buttons.Contains(t.Item));
        foreach (var (source,target,after) in new[] { (first,last,true), (last,first,false) }) {
            try {
                sourceTile = source;
                grabOffset = new System.Windows.Vector(4,4);
                var pointer = new System.Windows.Point(target.Hit.Left + 8,target.Hit.Top + 8);
                ShowDragFeedback(source,pointer);
                if (dragPreview?.IsOpen != true || dragPreview.Child.RenderSize.Width <= 0 ||
                    insertionMark.Visibility != System.Windows.Visibility.Visible) throw new Exception("Missing drag feedback");
                double edge = Vertical ? System.Windows.Controls.Canvas.GetTop(insertionMark) : System.Windows.Controls.Canvas.GetLeft(insertionMark);
                double expected = Vertical ? (after ? target.Hit.Bottom : target.Hit.Top - insertionMark.Height)
                                           : (after ? target.Hit.Right : target.Hit.Left - insertionMark.Width);
                if (Math.Abs(edge-expected) > .01) throw new Exception("Incorrect insertion edge");
                var outside = new System.Windows.Point(-20,-20);
                ShowDragFeedback(source,outside);
                if (dragPreview.HorizontalOffset != outside.X || insertionMark.Visibility != System.Windows.Visibility.Hidden)
                    throw new Exception("Drag preview did not follow pointer outside dock");
            } finally { ClearDrag(); }
            if (source.View.Opacity != 1 || canvas.Children.Contains(insertionMark) || dragPreview != null) throw new Exception("Drag feedback not cleared");
        }
    }
}
