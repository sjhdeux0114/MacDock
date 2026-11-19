using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MacDock;

public sealed partial class DockWindow : Window
{
    public DockConfig Config { get; private set; }
    readonly DockScene scene = new();
    Canvas canvas => scene.canvas;
    Border plate => scene.plate;
    List<Tile> tiles => scene.tiles;
    int PageCount => scene.PageCount;
    int page { get => scene.page; set => scene.page = value; }
    internal int CurrentPage => page;
    readonly Border toast = new() { CornerRadius = new(7), Background = Brush("#522839"), Padding = new(10,5,10,5), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly TextBlock message = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer metricsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly Metrics metrics = new();
    readonly HardwareMetrics hardware = new();
    readonly Forms.NotifyIcon tray;
    readonly System.Drawing.Icon trayIcon;
    SettingsWindow? settings;
    IntPtr hwnd;
    double scale => scene.scale;
    long toastUntil;
    double lastFrame;
    bool sampling;
    bool updatingLayout;
    bool forceFrame = true;
    Point lastMouse = new(double.NaN, double.NaN);
    public Point? TestPointer { get; set; }
    public List<double> FrameWorkMilliseconds { get; } = new();
    public bool MeasureFrames { get; set; }
    public int VisibleTileCount => tiles.Count;
    public Action? OpenSettingsOverride;
    internal sealed class Tile
    {
        public required DockItem Item;
        public required Border View;
        public required TextBlock Value;
        public CooldownOverlay? Cooldown;
        public CountdownPhase? Phase;
        public TextBlock? Caption;
        public Image? Image;
        public ImageFrames? Frames;
        public double Scale = 1;
        public Rect Hit;
    }
    public static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    public DockWindow(DockConfig config)
    {
        Config = config; Title = "MacDock WPF"; Icon = AppIcons.WindowIcon(); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Foreground = Brush("#E8EDF6"); Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        Width = 900; Height = 180; Content = canvas;
        toast.Child = message;
        EnableDragging();
        countdownTick.Tick += (_,_) => UpdateCountdowns();
        SourceInitialized += (_, _) => {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(hwnd, -20, new IntPtr(Native.GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000L | 0x80L));
            HwndSource.FromHwnd(hwnd).AddHook(Hook); Reposition();
        };
        Loaded += (_, _) => { Rebuild(); CompositionTarget.Rendering += Rendering; metricsTimer.Start(); Sample(); };
        metricsTimer.Tick += (_, _) => Sample();
        MouseWheel += (_, e) => { if (PageCount <= 1) return; page = Math.Clamp(page + (e.Delta < 0 ? 1 : -1), 0, PageCount - 1); Rebuild(); };
        trayIcon = AppIcons.TrayIcon();
        tray = new Forms.NotifyIcon { Text = "MacDock WPF", Icon = trayIcon, Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("MacDock 설정", null, (_, _) => Dispatcher.Invoke(OpenSettings)); menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(() => Application.Current.Shutdown()));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) => { countdownTick.Stop(); Soundboard.Close(); ClearDrag(); CompositionTarget.Rendering -= Rendering; metricsTimer.Stop(); tray.Dispose(); trayIcon.Dispose(); SystemEvents.DisplaySettingsChanged -= DisplayChanged; };
    }
    void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { Rebuild(reposition: true); });
    IntPtr Hook(IntPtr handle, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE: preserve the macro target.
        if (msg == 0x84) {
            int x = (short)(l.ToInt64() & 0xffff), y = (short)((l.ToInt64() >> 16) & 0xffff);
            var local = PointFromScreen(new Point(x, y));
            if (!tiles.Any(t => t.Hit.Contains(local)) && !plate.RenderTransform.TransformBounds(new Rect(0,0,plate.ActualWidth,plate.ActualHeight)).Contains(local)) { handled = true; return new IntPtr(-1); }
        }
        if ((msg == 0x2e0 || msg == 0x1a) && !movingWindow) Dispatcher.BeginInvoke(() => { Rebuild(reposition: true); });
        return IntPtr.Zero;
    }
    public void Apply(DockConfig config) { Config = config; page = 0; Rebuild(reposition: true); }
    public void OpenSettings()
    {
        if (OpenSettingsOverride != null) { OpenSettingsOverride(); return; }
        if (settings == null) { settings = new SettingsWindow(this); settings.Closed += (_, _) => settings = null; settings.Show(); }
        else { settings.WindowState = WindowState.Normal; settings.Activate(); }
    }
    bool Vertical => Config.Position is "left" or "right";
    bool NearEdge => Config.Position is "top" or "left";
    static bool Navigation(DockItem item) => item.Type is "previous-page" or "next-page";
    public static double ArrowLength(double size) => Math.Max(20, size * .35);
    public static int PageCapacity(double available, double size, double gap, double zoom, int count, int rows = 1)
    {
        double room = available - 40 - size * (zoom - 1);
        if (rows > 1) {
            int slots = rows * Math.Max(1, (int)Math.Floor((room + gap) / (size + gap)));
            return count + 1 <= slots ? Math.Max(1, count) : Math.Max(1, slots - 3);
        }
        if ((count + 1) * (size + gap) - gap <= room) return Math.Max(1, count);
        return Math.Max(1, (int)Math.Floor((room - size - 2 * (ArrowLength(size) + gap)) / (size + gap)));
    }
    void Reposition()
    {
        if (hwnd == IntPtr.Zero) return;
        var bounds = scene.MeasureWindow(Config);
        Native.SetWindowPos(hwnd,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x10);
        forceFrame = true;
    }
    void Rebuild(bool reposition = false)
    {
        // SetWindowPos can synchronously dispatch a WPF rendering callback.
        // Publish the new page, tile views and animation tracks as one layout.
        if (pressed != null) ClearDrag();
        updatingLayout = true;
        try {
            if (reposition) Reposition();
            ConfigureCountdowns(); BuildTiles(); UpdateCountdowns();
        } finally { updatingLayout = false; }
        forceFrame = true;
        Animate();
    }

    void BuildTiles()
    {
        scene.BuildTiles(Config);
        foreach (var tile in tiles) {
            var item = tile.Item;
            var view = tile.View;
            bool resetTimer = false;
            if (item.Type == "countdown") view.PreviewMouseLeftButtonDown += (_,e) => resetTimer = e.ClickCount == 2;
            view.MouseLeftButtonUp += async (_, e) => {
                e.Handled = true;
                try {
                    if (item.Type == "countdown") TimerClick(item,resetTimer);
                    else if (item.Type == "sound") Soundboard.Play(item.Id,item.Sound,Notify);
                    else if (item.Type == "file-share") { OpenSettings(); if (settings != null) await settings.ShowFileShare(); }
                    else if (item.Type == "settings") OpenSettings();
                    else if (item.Type == "previous-page" || item.Type == "next-page") { page = (page + (item.Type == "next-page" ? 1 : -1) + PageCount) % PageCount; Rebuild(); }
                    else { if (item.Type == "usb-eject") Notify("USB 저장장치를 안전하게 제거하는 중입니다…"); await Actions.Run(item); if (item.Type == "volume") Sample(); }
                } catch (Exception error) { Notify(error.Message); }
            };
        }
        canvas.Children.Add(toast); Panel.SetZIndex(toast,1000); Sample();
    }
    public void Notify(string text) { message.Text = text; toast.Visibility = Visibility.Visible; toastUntil = watch.ElapsedMilliseconds + 5000; }
    internal event Action? Sampled;
    (int Level, bool Muted)? lastVolume;
    async void Sample()
    {
        if (sampling) return;
        sampling = true;
        (int Level, bool Muted)? volume = null;
        bool needsGpu = tiles.Any(t => t.Item.Type is "gpu" or "gpu-temperature");
        bool needsNetwork = tiles.Any(t => t.Item.Type is "network-up" or "network-down");
        bool needsVolume = tiles.Any(t => t.Item.Type == "volume");
        try { await System.Threading.Tasks.Task.Run(() => { metrics.Sample(); hardware.Sample(needsGpu, needsNetwork); if (needsVolume) { try { volume = Audio.Read(); } catch { } } }); }
        finally { sampling = false; }
        lastVolume = volume;
        UpdateValues(scene);
        Sampled?.Invoke();
    }
    internal void UpdateValues(DockScene target)
    {
        foreach (var t in target.tiles) {
            switch (t.Item.Type) {
                case "countdown" when target != scene:
                    var timer = countdowns.TryGetValue(t.Item.Id,out var active) && active.Duration == t.Item.TimerSeconds ? active : new Countdown(t.Item.TimerSeconds);
                    DisplayTimer(t,timer,watch.Elapsed); break;
                case "clock": t.Value.Text = DateTime.Now.ToString(t.Item.Target == "12h" ? "hh:mm" : "HH:mm"); t.Caption!.Text = DateTime.Now.ToString(t.Item.Target == "12h" ? "tt M.d (ddd)" : "M.d (ddd)"); break;
                case "cpu": t.Value.Text = metrics.Cpu + "%"; t.Caption!.Text = "CPU"; break;
                case "memory": t.Value.Text = metrics.Memory + "%"; t.Caption!.Text = "RAM"; break;
                case "gpu": t.Value.Text = hardware.Gpu is { } g ? g + "%" : "—"; t.Caption!.Text = "GPU"; t.View.ToolTip = hardware.GpuInfo; break;
                case "gpu-temperature": t.Value.Text = hardware.Temperature is { } temp ? temp + "°C" : "—"; t.Caption!.Text = "GPU 온도"; t.View.ToolTip = hardware.GpuInfo; break;
                case "network-up": case "network-down": t.Value.Text = HardwareMetrics.Speed(t.Item.Type == "network-up" ? hardware.Upload : hardware.Download); t.Caption!.Text = t.Item.Type == "network-up" ? "↑ UP" : "↓ DOWN"; t.View.ToolTip = hardware.NetworkInfo; break;
                case "volume": t.Value.Text = lastVolume is { } v ? (v.Muted ? "음소거" : v.Level + "%") : "—"; t.Caption!.Text = t.Item.Target switch { "up" => "+ " + t.Item.VolumeStep + "%p", "down" => "− " + t.Item.VolumeStep + "%p", _ => "♪ 음소거" }; break;
            }
        }
    }
    void Rendering(object? sender, EventArgs e) => Animate();
    public static double Follow(double current, double target, double seconds) => current + (target - current) * (1 - Math.Exp(-Math.Clamp(seconds, 0, .1) / .045));
    void Animate()
    {
        if (dragPreview != null || updatingLayout || tiles.Count == 0 || ActualWidth == 0) return;
        double now = watch.Elapsed.TotalSeconds, dt = lastFrame == 0 ? 1d / 60 : now - lastFrame; lastFrame = now;
        Native.GetCursorPos(out var cursor); var mouse = TestPointer ?? PointFromScreen(new Point(cursor.X, cursor.Y));
        bool animatedImage = tiles.Any(t => t.Frames?.Frames.Count > 1);
        if (!forceFrame && mouse == lastMouse && !animatedImage && toast.Visibility != Visibility.Visible) return;
        lastMouse = mouse; forceFrame = false;
        long workStart = Stopwatch.GetTimestamp();
        scene.Arrange(new Size(ActualWidth,ActualHeight),mouse,dt,watch.ElapsedMilliseconds);
        forceFrame |= scene.Animating;
        if (toast.Visibility == Visibility.Visible) { toast.MaxWidth = Math.Max(1, ActualWidth - 4); toast.Measure(new Size(toast.MaxWidth, ActualHeight)); Canvas.SetLeft(toast, (ActualWidth - toast.DesiredSize.Width) / 2); Canvas.SetTop(toast, Config.Position == "top" ? Math.Max(2, ActualHeight - toast.DesiredSize.Height - 2) : 2); if (watch.ElapsedMilliseconds > toastUntil) toast.Visibility = Visibility.Collapsed; }
        if (MeasureFrames) FrameWorkMilliseconds.Add(Stopwatch.GetElapsedTime(workStart).TotalMilliseconds);
    }
}
