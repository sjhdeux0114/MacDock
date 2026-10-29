using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MacDock;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool smoke = args.Contains("--smoke-test"), selfTest = args.Contains("--self-test");
        if (selfTest) return SelfTest.Run();
        using var mutex = new Mutex(true, smoke ? "MacDock.Wpf.Smoke" : "MacDock.Wpf.Desktop",out bool first);
        if (!first) return 0;
        string testOutput = Path.Combine(Environment.CurrentDirectory,"wpf","test-output");
        if (smoke) Store.Folder = Path.Combine(testOutput,"profile");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MacDock.Wpf;component/Theme.xaml",UriKind.Relative) });
        DockConfig config;
        try { config = Store.Load(); }
        catch (Exception error) {
            if (smoke) { Directory.CreateDirectory(testOutput); File.WriteAllText(Path.Combine(testOutput,"smoke-error.txt"),error.ToString()); return 1; }
            MessageBox.Show("설정을 읽지 못했습니다. 기존 설정을 백업한 뒤 기본값으로 시작합니다.\n" + error.Message,"MacDock WPF");
            if (File.Exists(Store.FileName)) File.Copy(Store.FileName,Store.FileName + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss"),false);
            config = DockConfig.Default();
        }
        if (smoke) {
            config.Position = args.FirstOrDefault(a => a.StartsWith("--position="))?.Split('=')[1] ?? "bottom";
            config.Validate();
        }
        var dock = new DockWindow(config); app.MainWindow = dock; dock.Show();
        if (args.Contains("--settings")) dock.OpenSettings();
        if (smoke) {
            dock.MeasureFrames = true;
            var sweep = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            var sweepClock = System.Diagnostics.Stopwatch.StartNew();
            sweep.Tick += (_,_) => {
                double wave = Math.Sin(sweepClock.Elapsed.TotalSeconds * 4);
                dock.TestPointer = config.Position switch {
                    "left" => new Point(40, dock.ActualHeight / 2 + wave * dock.ActualHeight / 3),
                    "right" => new Point(dock.ActualWidth - 40, dock.ActualHeight / 2 + wave * dock.ActualHeight / 3),
                    "top" => new Point(dock.ActualWidth / 2 + wave * dock.ActualWidth / 3, 40),
                    _ => new Point(dock.ActualWidth / 2 + wave * dock.ActualWidth / 3, dock.ActualHeight - 40)
                };
            };
            sweep.Start();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_,_) => {
                timer.Stop(); sweep.Stop();
                try {
                    Directory.CreateDirectory(testOutput);
                    if (dock.VisibleTileCount < 2) throw new Exception("독 버튼이 렌더링되지 않았습니다.");
                    bool vertical = config.Position is "left" or "right";
                    if (vertical ? dock.ActualHeight <= dock.ActualWidth : dock.ActualWidth <= dock.ActualHeight) throw new Exception("Dock orientation mismatch");
                    var tileViews = ((System.Windows.Controls.Canvas)dock.Content).Children.OfType<System.Windows.Controls.Border>().Where(v => v.Cursor == System.Windows.Input.Cursors.Hand).ToArray();
                    Rect? previous = null;
                    foreach (var view in tileViews) {
                        var bounds = view.RenderTransform.TransformBounds(new Rect(0, 0, view.Width, view.Height));
                        if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > dock.ActualWidth || bounds.Bottom > dock.ActualHeight) throw new Exception("Tile clipped: " + config.Position);
                        if (previous is Rect prior && (vertical ? bounds.Top < prior.Bottom : bounds.Left < prior.Right)) throw new Exception("Overlapping tiles");
                        previous = bounds;
                    }
                    Capture(dock,Path.Combine(testOutput,"dock-" + config.Position + ".png"));
                    var settings = new SettingsWindow(dock); settings.Show(); settings.UpdateLayout();
                    Capture(settings,Path.Combine(testOutput,"settings.png"));
                    var picker = new IconPicker("#386976"); picker.Show(); picker.UpdateLayout(); Capture(picker,Path.Combine(testOutput,"icons.png")); picker.Close();
                    var recorder = new MacroRecorder(); recorder.Show(); recorder.UpdateLayout(); Capture(recorder,Path.Combine(testOutput,"recorder.png")); recorder.Close();
                    var metrics = new Metrics(); metrics.Sample(); var volume = Audio.Read();
                    File.WriteAllText(Path.Combine(testOutput,"smoke-result.txt"),$"PASS: WPF dock, settings, CPU/RAM, Core Audio read. Volume={volume.Level}; muted={volume.Muted}; input size={System.Runtime.InteropServices.Marshal.SizeOf<Native.Input>()}");
                    var samples = dock.FrameWorkMilliseconds.OrderBy(n => n).ToArray();
                    if (samples.Length > 0) File.WriteAllText(Path.Combine(testOutput,"animation-profile.txt"), $"Animation callback samples={samples.Length}; median={samples[samples.Length / 2]:F3} ms; p95={samples[(int)((samples.Length - 1) * .95)]:F3} ms; max={samples[^1]:F3} ms. Measures UI update work, not GPU frame presentation.");
                    app.Shutdown(0);
                } catch (Exception error) { File.WriteAllText(Path.Combine(testOutput,"smoke-error.txt"),error.ToString()); app.Shutdown(1); }
            }; timer.Start();
        }
        return app.Run();
    }
    static void Capture(Window window,string file)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);
        bmp.Render(content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var stream = File.Create(file); png.Save(stream);
    }
}
