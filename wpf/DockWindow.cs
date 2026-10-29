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

public sealed class DockWindow : Window
{
    public DockConfig Config { get; private set; }
    readonly Canvas canvas = new();
    readonly Border plate = new() { CornerRadius = new(23), BorderThickness = new(1), BorderBrush = Brush("#657783") };
    readonly Border toast = new() { CornerRadius = new(7), Background = Brush("#522839"), Padding = new(10,5,10,5), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly TextBlock message = new() { FontSize = 12 };
    readonly List<Tile> tiles = new();
    readonly DispatcherTimer metricsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly Metrics metrics = new();
    readonly HardwareMetrics hardware = new();
    readonly Forms.NotifyIcon tray;
    readonly System.Drawing.Icon trayIcon;
    SettingsWindow? settings;
    IntPtr hwnd;
    double scale = 1;
    int page, pageSize = 100;
    long toastUntil;
    double lastFrame;
    bool sampling;
    bool forceFrame = true;
    Point lastMouse = new(double.NaN, double.NaN);
    public Point? TestPointer { get; set; }
    public List<double> FrameWorkMilliseconds { get; } = new();
    public bool MeasureFrames { get; set; }
    public int VisibleTileCount => tiles.Count;
    public Action? OpenSettingsOverride;
    sealed class Tile
    {
        public required DockItem Item;
        public required Border View;
        public required TextBlock Value;
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
        plate.Background = new LinearGradientBrush(Color.FromArgb(239, 38, 52, 68), Color.FromArgb(239, 17, 24, 35), 45);
        toast.Child = message;
        SourceInitialized += (_, _) => {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(hwnd, -20, new IntPtr(Native.GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000L | 0x80L));
            HwndSource.FromHwnd(hwnd).AddHook(Hook); Reposition();
        };
        Loaded += (_, _) => { Rebuild(); CompositionTarget.Rendering += Rendering; metricsTimer.Start(); Sample(); };
        metricsTimer.Tick += (_, _) => Sample();
        MouseWheel += (_, e) => { if (Config.Buttons.Count <= pageSize) return; page = Math.Clamp(page + (e.Delta < 0 ? 1 : -1), 0, (Config.Buttons.Count - 1) / pageSize); Rebuild(); };
        trayIcon = AppIcons.TrayIcon();
        tray = new Forms.NotifyIcon { Text = "MacDock WPF", Icon = trayIcon, Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("MacDock 설정", null, (_, _) => Dispatcher.Invoke(OpenSettings)); menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(() => Application.Current.Shutdown()));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) => { CompositionTarget.Rendering -= Rendering; metricsTimer.Stop(); tray.Dispose(); trayIcon.Dispose(); SystemEvents.DisplaySettingsChanged -= DisplayChanged; };
    }
    void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { Reposition(); Rebuild(); });
    IntPtr Hook(IntPtr handle, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE: preserve the macro target.
        if (msg == 0x84) {
            int x = (short)(l.ToInt64() & 0xffff), y = (short)((l.ToInt64() >> 16) & 0xffff);
            var local = PointFromScreen(new Point(x, y));
            if (!tiles.Any(t => t.Hit.Contains(local))) { handled = true; return new IntPtr(-1); }
        }
        if (msg == 0x2e0) Dispatcher.BeginInvoke(Reposition);
        return IntPtr.Zero;
    }
    public void Apply(DockConfig config) { Config = config; page = 0; Reposition(); Rebuild(); }
    public void OpenSettings()
    {
        if (OpenSettingsOverride != null) { OpenSettingsOverride(); return; }
        if (settings == null) { settings = new SettingsWindow(this); settings.Closed += (_, _) => settings = null; settings.Show(); }
        else { settings.WindowState = WindowState.Normal; settings.Activate(); }
    }
    void Reposition()
    {
        if (hwnd == IntPtr.Zero) return;
        var display = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Config.Monitor) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var work = display.WorkingArea;
        var monitor = Native.MonitorFromPoint(new() { X = work.Left + 1, Y = work.Top + 1 }, 2);
        if (Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0) scale = dpi / 96d;
        double available = work.Width / scale;
        pageSize = Math.Max(1, (int)((available - 80 - Config.Size * Config.Zoom * 3) / (Config.Size + Config.Gap)) - 3);
        int shown = Math.Min(pageSize, Config.Buttons.Count) + 1 + (Config.Buttons.Count > pageSize ? 2 : 0);
        double width = Math.Min(available, shown * (Config.Size + Config.Gap) + Config.Size * Config.Zoom * 3 + 40);
        double height = Config.Size * Config.Zoom + 70;
        int pw = (int)Math.Ceiling(width * scale), ph = (int)Math.Ceiling(height * scale);
        Native.SetWindowPos(hwnd, new IntPtr(-1), work.Left + (work.Width - pw) / 2, work.Bottom - ph - (int)(Config.Bottom * scale), pw, ph, 0x10);
    }
    public void Rebuild()
    {
        tiles.Clear(); canvas.Children.Clear(); canvas.Children.Add(plate);
        page = Math.Clamp(page, 0, Math.Max(0, (Config.Buttons.Count - 1) / pageSize));
        var items = Config.Buttons.Skip(page * pageSize).Take(pageSize).ToList();
        if (Config.Buttons.Count > pageSize) { items.Insert(0, new() { Type = "previous-page", Label = "이전 버튼 페이지", Icon = "‹", Color = "#344052" }); items.Add(new() { Type = "next-page", Label = "다음 버튼 페이지", Icon = "›", Color = "#344052" }); }
        items.Add(new() { Type = "settings", Label = "설정", Icon = "⚙", Color = "#344052" });
        foreach (var item in items) {
            var value = new TextBlock { Text = item.Icon, FontSize = 27, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var view = new Border { Width = Config.Size, Height = Config.Size, CornerRadius = new(14), Background = Brush(item.Color), BorderBrush = Brush("#667A8B"), BorderThickness = new(.7), Cursor = Cursors.Hand, ToolTip = item.Label, Child = value, RenderTransform = new MatrixTransform() };
            var tile = new Tile { Item = item, View = view, Value = value };
            if (new[] { "clock", "cpu", "memory", "volume", "gpu", "gpu-temperature", "network-up", "network-down" }.Contains(item.Type)) {
                view.Child = null;
                value.FontSize = Config.Size * .40; value.FontWeight = FontWeights.Bold;
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new Viewbox { Child = value, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = Config.Size - 6, Height = Config.Size * .46 });
                tile.Caption = new TextBlock { Text = item.Label, FontSize = Math.Max(9, Config.Size * .17), Opacity = .9, TextAlignment = TextAlignment.Center };
                stack.Children.Add(new Viewbox { Child = tile.Caption, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = Config.Size - 6, Height = Config.Size * .20 }); view.Child = stack;
            } else if (!string.IsNullOrEmpty(item.Image)) {
                try { tile.Frames = ImageFrames.Load(item.Image); tile.Image = new Image { Source = tile.Frames.Frames[0], Stretch = Stretch.UniformToFill }; view.Child = tile.Image; view.Clip = new RectangleGeometry(new Rect(0,0,Config.Size,Config.Size),14,14); }
                catch { view.ToolTip = item.Label + " · 이미지 형식을 확인하세요"; }
            }
            if (item.Type == "folder" || item.Type == "path" || item.Type == "macro" || item.Type == "program") {
                var graphic = (UIElement)view.Child; view.Child = null;
                var folderContent = new Grid();
                folderContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                folderContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(15, Config.Size * .28)) });
                folderContent.Children.Add(graphic);
                var caption = new TextBlock { Text = string.IsNullOrWhiteSpace(item.Label) ? (item.Type == "macro" ? "매크로" : item.Type == "program" ? "프로그램" : DockItem.FolderName(item.Target)) : item.Label,
                    FontSize = Math.Max(10, Config.Size * .20), FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(3,0,3,2), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(caption,1); folderContent.Children.Add(caption); view.Child = folderContent;
            }
            view.MouseLeftButtonUp += async (_, e) => {
                e.Handled = true;
                try {
                    if (item.Type == "settings") OpenSettings();
                    else if (item.Type == "previous-page" || item.Type == "next-page") { page = (page + (item.Type == "next-page" ? 1 : -1) + (Config.Buttons.Count - 1) / pageSize + 1) % ((Config.Buttons.Count - 1) / pageSize + 1); Rebuild(); }
                    else { await Actions.Run(item); if (item.Type == "volume") Sample(); }
                } catch (Exception error) { Notify(error.Message); }
            };
            canvas.Children.Add(view); tiles.Add(tile);
        }
        plate.Width = tiles.Count * (Config.Size + Config.Gap) - Config.Gap + 24;
        plate.Height = Config.Size + 24; plate.RenderTransform = new MatrixTransform();
        canvas.Children.Add(toast); Panel.SetZIndex(toast, 1000); forceFrame = true; Sample(); Animate();
    }
    public void Notify(string text) { message.Text = text; toast.Visibility = Visibility.Visible; toastUntil = watch.ElapsedMilliseconds + 5000; }
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
        foreach (var t in tiles) {
            switch (t.Item.Type) {
                case "clock": t.Value.Text = DateTime.Now.ToString(t.Item.Target == "12h" ? "hh:mm" : "HH:mm"); t.Caption!.Text = DateTime.Now.ToString(t.Item.Target == "12h" ? "tt M.d (ddd)" : "M.d (ddd)"); break;
                case "cpu": t.Value.Text = metrics.Cpu + "%"; t.Caption!.Text = "CPU"; break;
                case "memory": t.Value.Text = metrics.Memory + "%"; t.Caption!.Text = "RAM"; break;
                case "gpu": t.Value.Text = hardware.Gpu is { } g ? g + "%" : "—"; t.Caption!.Text = "GPU"; t.View.ToolTip = hardware.GpuInfo; break;
                case "gpu-temperature": t.Value.Text = hardware.Temperature is { } temp ? temp + "°C" : "—"; t.Caption!.Text = "GPU 온도"; t.View.ToolTip = hardware.GpuInfo; break;
                case "network-up": case "network-down": t.Value.Text = HardwareMetrics.Speed(t.Item.Type == "network-up" ? hardware.Upload : hardware.Download); t.Caption!.Text = t.Item.Type == "network-up" ? "↑ UP" : "↓ DOWN"; t.View.ToolTip = hardware.NetworkInfo; break;
                case "volume": t.Value.Text = volume is { } v ? (v.Muted ? "음소거" : v.Level + "%") : "—"; t.Caption!.Text = t.Item.Target switch { "up" => "+ " + t.Item.VolumeStep + "%p", "down" => "− " + t.Item.VolumeStep + "%p", _ => "♪ 음소거" }; break;
            }
        }
    }
    void Rendering(object? sender, EventArgs e) => Animate();
    public static double Follow(double current, double target, double seconds) => current + (target - current) * (1 - Math.Exp(-Math.Clamp(seconds, 0, .1) / .045));
    void Animate()
    {
        if (tiles.Count == 0 || ActualWidth == 0) return;
        double now = watch.Elapsed.TotalSeconds, dt = lastFrame == 0 ? 1d / 60 : now - lastFrame; lastFrame = now;
        Native.GetCursorPos(out var cursor); var mouse = TestPointer ?? PointFromScreen(new Point(cursor.X, cursor.Y));
        bool animatedImage = tiles.Any(t => t.Frames?.Frames.Count > 1);
        if (!forceFrame && mouse == lastMouse && !animatedImage && toast.Visibility != Visibility.Visible) return;
        lastMouse = mouse; forceFrame = false;
        long workStart = Stopwatch.GetTimestamp();
        double s = Config.Size, gap = Config.Gap, baseWidth = tiles.Count * (s + gap) - gap;
        double start = (ActualWidth - baseWidth) / 2, bottom = ActualHeight - 20;
        bool active = Config.Magnify && SystemParameters.ClientAreaAnimation && mouse.Y > bottom - s * Config.Zoom - 10 && mouse.Y < bottom + 12 && mouse.X > start - s && mouse.X < start + baseWidth + s;
        double total = gap * (tiles.Count - 1);
        for (int i = 0; i < tiles.Count; i++) {
            double influence = active ? Math.Max(0, 1 - Math.Abs(mouse.X - (start + i * (s + gap) + s / 2)) / (s * 2.2)) : 0;
            double target = 1 + (Config.Zoom - 1) * influence * influence;
            tiles[i].Scale = Follow(tiles[i].Scale, target, dt);
            if (Math.Abs(target - tiles[i].Scale) < .001) tiles[i].Scale = target; else forceFrame = true;
            total += s * tiles[i].Scale;
        }
        double x = (ActualWidth - total) / 2;
        foreach (var tile in tiles) {
            double width = s * tile.Scale;
            ((MatrixTransform)tile.View.RenderTransform).Matrix = new Matrix(tile.Scale, 0, 0, tile.Scale, x, bottom - width);
            tile.Hit = new Rect(x, bottom - width, width, width);
            if (tile.Image != null && tile.Frames!.Frames.Count > 1) tile.Image.Source = tile.Frames.At(watch.ElapsedMilliseconds);
            x += width + gap;
        }
        ((MatrixTransform)plate.RenderTransform).Matrix = new Matrix((total + 24) / plate.Width, 0, 0, 1, (ActualWidth - total - 24) / 2, bottom - s - 12);
        if (toast.Visibility == Visibility.Visible) { toast.Measure(new Size(ActualWidth, 40)); Canvas.SetLeft(toast, (ActualWidth - toast.DesiredSize.Width) / 2); Canvas.SetTop(toast, 2); if (watch.ElapsedMilliseconds > toastUntil) toast.Visibility = Visibility.Collapsed; }
        if (MeasureFrames) FrameWorkMilliseconds.Add(Stopwatch.GetElapsedTime(workStart).TotalMilliseconds);
    }
}

