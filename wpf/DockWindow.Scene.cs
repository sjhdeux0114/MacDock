using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace MacDock;

public sealed partial class DockWindow
{
    internal sealed class DockScene
    {
        internal DockConfig Config { get; private set; } = null!;
        internal readonly Canvas canvas = new();
        internal readonly Border plate = new() { CornerRadius = new(23), BorderThickness = new(1), BorderBrush = Brush("#657783"),
            Background = new LinearGradientBrush(Color.FromArgb(239,38,52,68),Color.FromArgb(239,17,24,35),45) };
        internal readonly List<Tile> tiles = new();
        readonly List<Line> lines = new();
        List<BlockPage>? blockPages;
        internal int PageCount => blockPages?.Count ?? Math.Max(1, (Config.Buttons.Count + pageSize - 1) / pageSize);
        double[] columnExtra = Array.Empty<double>(), rowExtra = Array.Empty<double>();
        int visibleLines = 1;
        sealed class Line(int first, int count, double length)
        {
            public readonly int First = first, Count = count;
            public readonly double Length = length;
            public double Fit, RenderedLength, Depth;
        }

        internal int page;
        int pageSize = 100;
        internal double scale = 1;
        internal Size DesktopSize { get; private set; }
        internal Rect WindowBounds { get; private set; }
        double ActualWidth, ActualHeight;
        internal bool Animating { get; private set; }
        bool Vertical => Config.Position is "left" or "right";
        bool NearEdge => Config.Position is "top" or "left";

        double TileLength(Tile tile) => Vertical ? tile.View.Height : tile.View.Width;

        internal System.Drawing.Rectangle MeasureWindow(DockConfig config)
        {
            Config = config;
            var display = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Config.Monitor) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
            var work = display.WorkingArea;
            var monitor = Native.MonitorFromPoint(new() { X = work.Left + 1, Y = work.Top + 1 }, 2);
            scale = 1;
            if (Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0) scale = dpi / 96d;
            double available = (Vertical ? work.Height : work.Width) / scale;
            double crossRoom = (Vertical ? work.Width : work.Height) / scale;
            double step = Config.Size + Config.Gap;
            double extra = Config.Magnify ? Config.Size * (Config.Zoom - 1) : 0;
            visibleLines = Math.Clamp((int)Math.Floor((crossRoom - 40 - extra + Config.Gap) / step), 1, Config.Rows);
            pageSize = PageCapacity(available, Config.Size, Config.Gap, Config.Magnify ? Config.Zoom : 1, Config.Buttons.Count, visibleLines);
            int shown = Math.Min(pageSize, Config.Buttons.Count) + 1;
            bool paging = Config.Buttons.Count > pageSize;
            int slots = shown + (paging ? 2 : 0);
            int rows = Math.Min(visibleLines, slots);
            int columns = (slots + rows - 1) / rows;
            double span = columns * step - Config.Gap;
            if (rows == 1 && paging) span -= 2 * (Config.Size - ArrowLength(Config.Size));
            double width = Math.Min(available, span + extra + 40);
            double height = rows * step - Config.Gap + extra + 70;
            blockPages = null;
            if (Config.Buttons.Any(b => b.BlockSize != "1x1")) {
                int limit = Math.Max(5, (int)Math.Floor((available - 40 - extra + Config.Gap) / step));
                blockPages = BlockPage.Pack(Config, Vertical, visibleLines, limit);
                double longCells = blockPages.Max(p => Vertical ? p.Rows : p.Columns);
                double crossCells = blockPages.Max(p => Vertical ? p.Columns : p.Rows);
                width = Math.Min(available, longCells * step - Config.Gap + 2 * extra + 40);
                height = crossCells * step - Config.Gap + 2 * extra + 70;
            }
            int pw = Math.Min(work.Width, (int)Math.Ceiling((Vertical ? height : width) * scale));
            int ph = Math.Min(work.Height, (int)Math.Ceiling((Vertical ? width : height) * scale));
            int margin = (int)(Config.Bottom * scale);
            int x = Config.Position switch { "left" => work.Left + margin, "right" => work.Right - pw - margin, _ => work.Left + (work.Width - pw) / 2 };
            int y = Config.Position switch { "top" => work.Top + margin, "bottom" => work.Bottom - ph - margin, _ => work.Top + (work.Height - ph) / 2 };
            if (Config.FreeX is double fx && Config.FreeY is double fy) {
                x = work.Left + (int)Math.Round(Math.Clamp(fx,0,1) * (work.Width - pw));
                y = work.Top + (int)Math.Round(Math.Clamp(fy,0,1) * (work.Height - ph));
            }
            x = Math.Clamp(x, work.Left, work.Right - pw); y = Math.Clamp(y, work.Top, work.Bottom - ph);
            DesktopSize = new Size(work.Width / scale, work.Height / scale);
            WindowBounds = new Rect((x-work.Left)/scale,(y-work.Top)/scale,pw/scale,ph/scale);
            return new System.Drawing.Rectangle(x,y,pw,ph);
        }

        internal static Brush TileBrush(string color) => Brush(System.Text.RegularExpressions.Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$") ? color : "#344052");

        internal void BuildTiles(DockConfig config)
        {
            Config = config;
            tiles.Clear(); canvas.Children.Clear(); canvas.Children.Add(plate);
            page = Math.Clamp(page, 0, PageCount - 1);
            var gridPage = blockPages?[page];
            var items = Config.Buttons.Skip(page * pageSize).Take(pageSize).ToList();
            if (Config.Buttons.Count > pageSize) { items.Insert(0, new() { Type = "previous-page", Label = "이전 버튼 페이지", Icon = Vertical ? "↑" : "‹", Color = "#344052" }); items.Add(new() { Type = "next-page", Label = "다음 버튼 페이지", Icon = Vertical ? "↓" : "›", Color = "#344052" }); }
            items.Add(new() { Type = "settings", Label = "설정", Icon = "⚙", Color = "#344052" });
            if (gridPage != null) {
                items = gridPage.Cells.Select(c => c.Item).ToList();
                columnExtra = new double[gridPage.Columns]; rowExtra = new double[gridPage.Rows];
            }
            foreach (var item in items) {
                var value = new TextBlock { Text = item.Icon, FontSize = 27, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var view = new Border { Width = Config.Size, Height = Config.Size, CornerRadius = new(14), Background = TileBrush(item.Color), BorderBrush = Brush("#667A8B"), BorderThickness = new(.7), Cursor = Cursors.Hand, ToolTip = item.Label, Child = value, RenderTransform = new MatrixTransform() };
                if (gridPage != null) {
                    var cell = gridPage.Cells[tiles.Count];
                    view.Width = cell.Width * (Config.Size + Config.Gap) - Config.Gap;
                    view.Height = cell.Height * (Config.Size + Config.Gap) - Config.Gap;
                } else if (Navigation(item)) {
                    if (Vertical) view.Height = ArrowLength(Config.Size); else view.Width = ArrowLength(Config.Size);
                    view.CornerRadius = new(8); value.FontSize = 16;
                }
                // Wide blocks have extra room along one axis; reserve height for both lines.
                double textSize = Math.Min(Math.Sqrt(view.Width * view.Height), view.Height * 1.35);
                if (!Navigation(item)) value.FontSize = 27 * textSize / Config.Size;
                var tile = new Tile { Item = item, View = view, Value = value };
                if (new[] { "countdown", "clock", "cpu", "memory", "volume", "gpu", "gpu-temperature", "network-up", "network-down" }.Contains(item.Type)) {
                    view.Child = null;
                    value.FontSize = textSize * .40; value.FontWeight = FontWeights.Bold;
                    var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    stack.Children.Add(new Viewbox { Child = value, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = view.Width - 6, Height = textSize * .46 });
                    tile.Caption = new TextBlock { Text = item.Label, FontSize = Math.Max(9, textSize * .17), Opacity = .9, TextAlignment = TextAlignment.Center };
                    stack.Children.Add(new Viewbox { Child = tile.Caption, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = view.Width - 6, Height = textSize * .20 });
                    if (item.Type == "countdown") {
                        tile.Cooldown = new CooldownOverlay { IsHitTestVisible = false };
                        var face = new Grid();
                        face.Children.Add(tile.Cooldown);
                        face.Children.Add(stack);
                        view.Child = face;
                    } else view.Child = stack;
                } else if (!string.IsNullOrEmpty(item.Image)) {
                    try { tile.Frames = ImageFrames.Load(item.Image); tile.Image = new Image { Source = tile.Frames.Frames[0], Stretch = Stretch.UniformToFill }; view.Child = tile.Image; view.Clip = new RectangleGeometry(new Rect(0,0,view.Width,view.Height),14,14); }
                    catch { view.ToolTip = item.Label + " · 이미지 형식을 확인하세요"; }
                }
                if (item.Type == "folder" || item.Type == "path" || item.Type == "macro" || item.Type == "program" || item.Type == "usb-eject" || item.Type == "file-share" || item.Type == "shutdown-timer" || item.Type == "sound") {
                    var graphic = (UIElement)view.Child; view.Child = null;
                    var folderContent = new Grid();
                    folderContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    folderContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    folderContent.Children.Add(graphic is TextBlock ? new Viewbox { Child = graphic, StretchDirection = StretchDirection.DownOnly } : graphic);
                    var caption = new TextBlock { Text = string.IsNullOrWhiteSpace(item.Label) ? (item.Type == "macro" ? "매크로" : item.Type == "program" ? "프로그램" : DockItem.FolderName(item.Target)) : item.Label,
                        FontSize = Math.Max(10, textSize * .20), FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(3,0,3,2), VerticalAlignment = VerticalAlignment.Center };
                    double textWidth = view.Width - 9, textHeight = (view.Height - 4) * .55;
                    while (caption.FontSize > 1) {
                        caption.Measure(new Size(textWidth + caption.Margin.Left + caption.Margin.Right, double.PositiveInfinity));
                        if (caption.DesiredSize.Height <= textHeight) break;
                        caption.FontSize -= .5;
                    }
                    Grid.SetRow(caption,1); folderContent.Children.Add(caption); view.Child = folderContent;
                }
                var graphicLayer = view.Child;
                view.Child = null;
                var layers = new Grid();
                layers.Children.Add(graphicLayer);
                var highlight = new Border {
                    CornerRadius = view.CornerRadius, BorderThickness = new(2),
                    BorderBrush = Brush("#BCFFF1"), Background = Brush("#247AE7D5"),
                    IsHitTestVisible = false, Visibility = Visibility.Hidden
                };
                layers.Children.Add(highlight);
                view.Child = layers;
                view.MouseEnter += (_, _) => highlight.Visibility = Visibility.Visible;
                view.MouseLeave += (_, _) => highlight.Visibility = Visibility.Hidden;
                canvas.Children.Add(view); tiles.Add(tile);
            }
            lines.Clear();
            int rows = Math.Min(visibleLines, tiles.Count);
            int first = 0;
            for (int row = 0; row < rows; row++) {
                int count = tiles.Count / rows + (row < tiles.Count % rows ? 1 : 0);
                double span = (count - 1) * Config.Gap;
                for (int i = first; i < first + count; i++) span += TileLength(tiles[i]);
                lines.Add(new Line(first, count, span));
                first += count;
            }
            plate.Width = plate.Height = 1;
            plate.RenderTransform = new MatrixTransform();

        }

        internal void Arrange(Size size, Point mouse, double dt, long elapsedMilliseconds)
        {
            ActualWidth = size.Width; ActualHeight = size.Height;
            Animating = false;
            if (blockPages != null) AnimateBlocks(blockPages[page], mouse, dt, elapsedMilliseconds);
            else AnimateLines(mouse, dt, elapsedMilliseconds);
        }
        void AnimateBlocks(BlockPage layout, Point mouse, double dt, long elapsedMilliseconds)
        {
            double s = Config.Size, gap = Config.Gap, step = s + gap;
            int cols = columnExtra.Length, rows = rowExtra.Length;
            double baseW = cols * step - gap, baseH = rows * step - gap;
            double Origin(double available, double extent, bool centered) =>
                centered ? (available - extent) / 2 : NearEdge ? 20 : available - extent - 20;
            double left = Origin(ActualWidth, baseW, !Vertical), top = Origin(ActualHeight, baseH, Vertical);
            Array.Clear(columnExtra); Array.Clear(rowExtra);
            for (int i = 0; i < tiles.Count; i++) {
                var t = tiles[i]; var cell = layout.Cells[i];
                var rect = new Rect(left + cell.X * step, top + cell.Y * step, t.View.Width, t.View.Height);
                double dx = Math.Max(0, Math.Max(rect.Left - mouse.X, mouse.X - rect.Right));
                double dy = Math.Max(0, Math.Max(rect.Top - mouse.Y, mouse.Y - rect.Bottom));
                double influence = Config.Magnify && SystemParameters.ClientAreaAnimation && !Navigation(t.Item)
                    ? Math.Max(0, 1 - Math.Sqrt(dx*dx + dy*dy) / s) : 0;
                double target = 1 + (Config.Zoom - 1) * influence * influence;
                t.Scale = Follow(t.Scale,target,dt);
                if (Math.Abs(target-t.Scale) < .001) t.Scale = target; else Animating = true;
                double dw = t.View.Width * (t.Scale - 1) / cell.Width, dh = t.View.Height * (t.Scale - 1) / cell.Height;
                for (int x=cell.X; x<cell.X+cell.Width; x++) columnExtra[x] = Math.Max(columnExtra[x],dw);
                for (int y=cell.Y; y<cell.Y+cell.Height; y++) rowExtra[y] = Math.Max(rowExtra[y],dh);
            }
            double extraW = columnExtra.Sum(), extraH = rowExtra.Sum();
            double fit = 1;
            if (extraW > 0) fit = Math.Min(fit, (ActualWidth - 40 - baseW) / extraW);
            if (extraH > 0) fit = Math.Min(fit, (ActualHeight - 40 - baseH) / extraH);
            fit = Math.Max(0,fit);
            double width = baseW + extraW * fit, height = baseH + extraH * fit;
            left = Origin(ActualWidth,width,!Vertical); top = Origin(ActualHeight,height,Vertical);
            // Prefix sums give every item the same grid boundaries, including internal gaps.
            for (int x=1; x<cols; x++) columnExtra[x] += columnExtra[x-1];
            for (int y=1; y<rows; y++) rowExtra[y] += rowExtra[y-1];
            for (int i=0; i<tiles.Count; i++) {
                var t=tiles[i]; var c=layout.Cells[i];
                double beforeX = c.X == 0 ? 0 : columnExtra[c.X-1], beforeY = c.Y == 0 ? 0 : rowExtra[c.Y-1];
                double factor = 1 + (t.Scale-1)*fit;
                double freeW = (columnExtra[c.X+c.Width-1]-beforeX)*fit - t.View.Width*(factor-1);
                double freeH = (rowExtra[c.Y+c.Height-1]-beforeY)*fit - t.View.Height*(factor-1);
                double x = left+c.X*step+beforeX*fit+freeW/2, y = top+c.Y*step+beforeY*fit+freeH/2;
                ((MatrixTransform)t.View.RenderTransform).Matrix = new Matrix(factor,0,0,factor,x,y);
                t.Hit = new Rect(x,y,t.View.Width*factor,t.View.Height*factor);
                if (t.Image != null && t.Frames!.Frames.Count > 1) t.Image.Source = t.Frames.At(elapsedMilliseconds);
            }
            plate.Width=width+24; plate.Height=height+24;
            ((MatrixTransform)plate.RenderTransform).Matrix = new Matrix(1,0,0,1,left-12,top-12);
        }
        void AnimateLines(Point mouse, double dt, long elapsedMilliseconds)
        {
            double s = Config.Size, gap = Config.Gap;
            double length = Vertical ? ActualHeight : ActualWidth, depth = Vertical ? ActualWidth : ActualHeight;
            double along = Vertical ? mouse.Y : mouse.X, across = Vertical ? mouse.X : mouse.Y;
            double normalDepth = lines.Count * (s + gap) - gap;
            double edge = NearEdge ? 20 : depth - 20;
            double inward = NearEdge ? across - edge : edge - across;
            bool active = Config.Magnify && SystemParameters.ClientAreaAnimation && inward > -12 && inward < normalDepth + s * (Config.Zoom - 1) + 10;
            int nearest = Math.Clamp((int)Math.Floor((inward + gap / 2) / (s + gap)), 0, lines.Count - 1);
            int hoverLine = NearEdge ? nearest : lines.Count - 1 - nearest;
            double fullDepth = -gap, maxLength = 0;
            for (int row = 0; row < lines.Count; row++) {
                var line = lines[row];
                double center = (length - line.Length) / 2;
                double expansion = 0, maxScale = 1;
                for (int i = line.First; i < line.First + line.Count; i++) {
                    var tile = tiles[i];
                    double tileLength = TileLength(tile);
                    double influence = active && row == hoverLine && !Navigation(tile.Item)
                        ? Math.Max(0, 1 - Math.Abs(along - (center + tileLength / 2)) / (s * 2.2)) : 0;
                    center += tileLength + gap;
                    double target = 1 + (Config.Zoom - 1) * influence * influence;
                    tile.Scale = Follow(tile.Scale, target, dt);
                    if (Math.Abs(target - tile.Scale) < .001) tile.Scale = target; else Animating = true;
                    expansion += tileLength * (tile.Scale - 1);
                    maxScale = Math.Max(maxScale, tile.Scale);
                }
                line.Fit = expansion > 0 ? Math.Clamp((length - 24 - line.Length) / expansion, 0, 1) : 1;
                line.RenderedLength = line.Length + expansion * line.Fit;
                line.Depth = s * (1 + (maxScale - 1) * line.Fit);
                maxLength = Math.Max(maxLength, line.RenderedLength);
                fullDepth += line.Depth + gap;
            }
            double crossStart = NearEdge ? edge : edge - fullDepth;
            double cross = crossStart;
            foreach (var line in lines) {
                double x = (length - line.RenderedLength) / 2;
                for (int i = line.First; i < line.First + line.Count; i++) {
                    var tile = tiles[i];
                    double renderedScale = 1 + (tile.Scale - 1) * line.Fit;
                    double offset = NearEdge ? cross : cross + line.Depth - s * renderedScale;
                    double left = Vertical ? offset : x, top = Vertical ? x : offset;
                    ((MatrixTransform)tile.View.RenderTransform).Matrix = new Matrix(renderedScale, 0, 0, renderedScale, left, top);
                    tile.Hit = new Rect(left, top, tile.View.Width * renderedScale, tile.View.Height * renderedScale);
                    if (tile.Image != null && tile.Frames!.Frames.Count > 1) tile.Image.Source = tile.Frames.At(elapsedMilliseconds);
                    x += TileLength(tile) * renderedScale + gap;
                }
                cross += line.Depth + gap;
            }
            double plateStart = (length - maxLength - 24) / 2;
            double plateDepth = lines.Count == 1 ? s : fullDepth;
            double plateCross = NearEdge ? edge - 12 : edge - plateDepth - 12;
            plate.Width = (Vertical ? plateDepth : maxLength) + 24;
            plate.Height = (Vertical ? maxLength : plateDepth) + 24;
            ((MatrixTransform)plate.RenderTransform).Matrix = new Matrix(1, 0, 0, 1,
                Vertical ? plateCross : plateStart, Vertical ? plateStart : plateCross);
        }
    }
}
