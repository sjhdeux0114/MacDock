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
    readonly TextBlock message = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
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
        if (msg == 0x2e0 || msg == 0x1a) Dispatcher.BeginInvoke(() => { Reposition(); Rebuild(); });
        return IntPtr.Zero;
    }
    public void Apply(DockConfig config) { Config = config; page = 0; Reposition(); Rebuild(); }
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
    public static int PageCapacity(double available, double size, double gap, double zoom, int count)
    {
        double room = available - 40 - size * (zoom - 1);
        if ((count + 1) * (size + gap) - gap <= room) return Math.Max(1, count);
        return Math.Max(1, (int)Math.Floor((room - size - 2 * (ArrowLength(size) + gap)) / (size + gap)));
    }
    double TileLength(Tile tile) => Vertical ? tile.View.Height : tile.View.Width;
    void Reposition()
    {
        if (hwnd == IntPtr.Zero) return;
        var display = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Config.Monitor) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var work = display.WorkingArea;
        var monitor = Native.MonitorFromPoint(new() { X = work.Left + 1, Y = work.Top + 1 }, 2);
        if (Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0) scale = dpi / 96d;
        double available = (Vertical ? work.Height : work.Width) / scale;
        pageSize = PageCapacity(available, Config.Size, Config.Gap, Config.Magnify ? Config.Zoom : 1, Config.Buttons.Count);
        int shown = Math.Min(pageSize, Config.Buttons.Count) + 1;
        double width = Math.Min(available, shown * (Config.Size + Config.Gap) - Config.Gap + (Config.Buttons.Count > pageSize ? 2 * (ArrowLength(Config.Size) + Config.Gap) : 0) + Config.Size * (Config.Magnify ? Config.Zoom - 1 : 0) + 40);
        double height = Config.Size * Config.Zoom + 70;
        int pw = Math.Min(work.Width, (int)Math.Ceiling((Vertical ? height : width) * scale));
        int ph = Math.Min(work.Height, (int)Math.Ceiling((Vertical ? width : height) * scale));
        int margin = (int)(Config.Bottom * scale);
        int x = Config.Position switch { "left" => work.Left + margin, "right" => work.Right - pw - margin, _ => work.Left + (work.Width - pw) / 2 };
        int y = Config.Position switch { "top" => work.Top + margin, "bottom" => work.Bottom - ph - margin, _ => work.Top + (work.Height - ph) / 2 };
        Native.SetWindowPos(hwnd, new IntPtr(-1), Math.Clamp(x, work.Left, work.Right - pw), Math.Clamp(y, work.Top, work.Bottom - ph), pw, ph, 0x10);
        forceFrame = true;
    }
    public void Rebuild()
    {
        tiles.Clear(); canvas.Children.Clear(); canvas.Children.Add(plate);
        page = Math.Clamp(page, 0, Math.Max(0, (Config.Buttons.Count - 1) / pageSize));
        var items = Config.Buttons.Skip(page * pageSize).Take(pageSize).ToList();
        if (Config.Buttons.Count > pageSize) { items.Insert(0, new() { Type = "previous-page", Label = "이전 버튼 페이지", Icon = Vertical ? "↑" : "‹", Color = "#344052" }); items.Add(new() { Type = "next-page", Label = "다음 버튼 페이지", Icon = Vertical ? "↓" : "›", Color = "#344052" }); }
        items.Add(new() { Type = "settings", Label = "설정", Icon = "⚙", Color = "#344052" });
        foreach (var item in items) {
            var value = new TextBlock { Text = item.Icon, FontSize = 27, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var view = new Border { Width = Config.Size, Height = Config.Size, CornerRadius = new(14), Background = Brush(item.Color), BorderBrush = Brush("#667A8B"), BorderThickness = new(.7), Cursor = Cursors.Hand, ToolTip = item.Label, Child = value, RenderTransform = new MatrixTransform() };
            if (Navigation(item)) {
                if (Vertical) view.Height = ArrowLength(Config.Size); else view.Width = ArrowLength(Config.Size);
                view.CornerRadius = new(8); value.FontSize = 16;
            }
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
        double plateLength = tiles.Sum(TileLength) + (tiles.Count - 1) * Config.Gap + 24;
        plate.Width = Vertical ? Config.Size + 24 : plateLength;
        plate.Height = Vertical ? plateLength : Config.Size + 24;
        plate.RenderTransform = new MatrixTransform();
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
        double s = Config.Size, gap = Config.Gap, baseWidth = tiles.Sum(TileLength) + (tiles.Count - 1) * gap;
        double length = Vertical ? ActualHeight : ActualWidth, depth = Vertical ? ActualWidth : ActualHeight;
        double along = Vertical ? mouse.Y : mouse.X, across = Vertical ? mouse.X : mouse.Y;
        double start = (length - baseWidth) / 2, edge = NearEdge ? 20 : depth - 20;
        double inward = NearEdge ? across - edge : edge - across;
        bool active = Config.Magnify && SystemParameters.ClientAreaAnimation && inward > -12 && inward < s * Config.Zoom + 10 && along > start - s && along < start + baseWidth + s;
        double total = gap * (tiles.Count - 1), center = start;
        for (int i = 0; i < tiles.Count; i++) {
            double tileLength = TileLength(tiles[i]);
            double influence = active && !Navigation(tiles[i].Item) ? Math.Max(0, 1 - Math.Abs(along - (center + tileLength / 2)) / (s * 2.2)) : 0;
            center += tileLength + gap;
            double target = 1 + (Config.Zoom - 1) * influence * influence;
            tiles[i].Scale = Follow(tiles[i].Scale, target, dt);
            if (Math.Abs(target - tiles[i].Scale) < .001) tiles[i].Scale = target; else forceFrame = true;
            total += tileLength * tiles[i].Scale;
        }
        // Keep all buttons visible when several neighbours magnify near a screen edge.
        double expansion = total - baseWidth;
        double fit = expansion > 0 ? Math.Clamp((length - 24 - baseWidth) / expansion, 0, 1) : 1;
        total = baseWidth + expansion * fit;
        double x = (length - total) / 2;
        foreach (var tile in tiles) {
            double renderedScale = 1 + (tile.Scale - 1) * fit;
            double width = s * renderedScale;
            double tileLength = TileLength(tile) * renderedScale;
            double cross = NearEdge ? edge : edge - width;
            double left = Vertical ? cross : x, top = Vertical ? x : cross;
            ((MatrixTransform)tile.View.RenderTransform).Matrix = new Matrix(renderedScale, 0, 0, renderedScale, left, top);
            tile.Hit = new Rect(left, top, tile.View.Width * renderedScale, tile.View.Height * renderedScale);
            if (tile.Image != null && tile.Frames!.Frames.Count > 1) tile.Image.Source = tile.Frames.At(watch.ElapsedMilliseconds);
            x += tileLength + gap;
        }
        double plateStart = (length - total - 24) / 2, plateCross = NearEdge ? edge - 12 : edge - s - 12;
        ((MatrixTransform)plate.RenderTransform).Matrix = Vertical
            ? new Matrix(1, 0, 0, (total + 24) / plate.Height, plateCross, plateStart)
            : new Matrix((total + 24) / plate.Width, 0, 0, 1, plateStart, plateCross);
        if (toast.Visibility == Visibility.Visible) { toast.MaxWidth = Math.Max(1, ActualWidth - 4); toast.Measure(new Size(toast.MaxWidth, ActualHeight)); Canvas.SetLeft(toast, (ActualWidth - toast.DesiredSize.Width) / 2); Canvas.SetTop(toast, Config.Position == "top" ? Math.Max(2, ActualHeight - toast.DesiredSize.Height - 2) : 2); if (watch.ElapsedMilliseconds > toastUntil) toast.Visibility = Visibility.Collapsed; }
        if (MeasureFrames) FrameWorkMilliseconds.Add(Stopwatch.GetElapsedTime(workStart).TotalMilliseconds);
    }
}

