using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace MacDock;

public sealed partial class DockWindow
{
    UIElement? pressed;
    Tile? sourceTile;
    Point pressScreen, windowOrigin;
    bool dragged, movingWindow;
    Popup? dragPreview;
    Vector grabOffset;
    readonly Border insertionMark = new() { Background = Brush("#BCFFF1"), CornerRadius = new(2), IsHitTestVisible = false };

    void EnableDragging()
    {
        plate.Cursor = Cursors.SizeAll;
        plate.ToolTip = "여백을 끌어 독 이동";
        PreviewMouseLeftButtonDown += (_, e) => {
            var point = e.GetPosition(this);
            sourceTile = tiles.FirstOrDefault(t => t.Hit.Contains(point));
            if (sourceTile != null && !Config.Buttons.Contains(sourceTile.Item)) return;
            if (sourceTile == null && !plate.IsMouseOver) return;
            pressed = sourceTile == null ? plate : sourceTile.View;
            pressScreen = PointToScreen(point);
            if (sourceTile != null) grabOffset = point - sourceTile.Hit.TopLeft;
            windowOrigin = PointToScreen(new Point(0,0));
            if (!pressed.CaptureMouse()) ClearDrag();
        };
        PreviewMouseMove += (_, e) => {
            if (pressed == null || e.LeftButton != MouseButtonState.Pressed) return;
            Point local = e.GetPosition(this), screen = PointToScreen(local);
            Vector delta = screen - pressScreen;
            if (!dragged && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * scale &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * scale) return;
            dragged = true;
            if (sourceTile == null) {
                movingWindow = true;
                Native.SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(windowOrigin.X + delta.X),
                    (int)Math.Round(windowOrigin.Y + delta.Y), 0, 0, 0x15); // no size, z-order, or activation change
            } else {
                ShowDragFeedback(sourceTile, local);
            }
            e.Handled = true;
        };
        PreviewMouseLeftButtonUp += (_, e) => {
            if (pressed == null) return;
            var point = e.GetPosition(this);
            var source = sourceTile;
            var target = DropTarget(point);
            bool move = movingWindow, drop = dragged;
            e.Handled = drop || source == null || !source.Hit.Contains(point);
            ClearDrag();
            if (!drop) return;
            try {
                var config = Config.Clone();
                if (move) {
                    var origin = PointToScreen(new Point(0,0));
                    var workScreen = Forms.Screen.FromPoint(Forms.Cursor.Position);
                    var area = workScreen.WorkingArea;
                    var end = PointToScreen(new Point(ActualWidth,ActualHeight));
                    config.FreeX = Fraction(origin.X-area.Left,area.Width-(end.X-origin.X));
                    config.FreeY = Fraction(origin.Y-area.Top,area.Height-(end.Y-origin.Y));
                    config.Monitor = workScreen.DeviceName;
                    Store.Save(config); Apply(config); settings?.SyncDraggedPosition(config);
                } else if (source != null && target != null && MoveButton(config,source.Item.Id,target.Item.Id)) {
                    Store.Save(config); Config = config;
                    Rebuild(reposition: true); settings?.SyncDraggedButton(source.Item.Id,target.Item.Id);
                }
            } catch (Exception error) { Rebuild(reposition: true); Notify(error.Message); }
        };
        LostMouseCapture += (_, _) => {
            if (pressed != null && Mouse.Captured != pressed) {
                bool restore = movingWindow;
                ClearDrag();
                if (restore) Rebuild(reposition: true);
            }
        };
    }

    void ShowDragFeedback(Tile source, Point pointer)
    {
        if (dragPreview == null) {
            // Mirror the contents, not the dimmed source or its canvas transform.
            var face = new Border {
                Width = source.Hit.Width, Height = source.Hit.Height, CornerRadius = source.View.CornerRadius,
                Background = source.View.Background, BorderBrush = Brush("#BCFFF1"), BorderThickness = new(2),
                Child = new Border { Background = new VisualBrush(source.View.Child) { Stretch = Stretch.Fill } },
                Opacity = .82, IsHitTestVisible = false
            };
            dragPreview = new Popup { Placement = PlacementMode.Relative, PlacementTarget = canvas,
                AllowsTransparency = true, StaysOpen = true, IsHitTestVisible = false, Child = face };
            source.View.Opacity = .32;
            Panel.SetZIndex(insertionMark, 999);
            canvas.Children.Add(insertionMark);
        }
        dragPreview.HorizontalOffset = pointer.X - grabOffset.X + 4;
        dragPreview.VerticalOffset = pointer.Y - grabOffset.Y + 4;
        dragPreview.IsOpen = true;
        var over = DropTarget(pointer);
        if (over == null) {
            insertionMark.Visibility = Visibility.Hidden;
        } else {
            bool after = Config.Buttons.IndexOf(source.Item) < Config.Buttons.IndexOf(over.Item);
            var bounds = over.Hit;
            const double thickness = 3;
            insertionMark.Width = Vertical ? bounds.Width : thickness;
            insertionMark.Height = Vertical ? thickness : bounds.Height;
            Canvas.SetLeft(insertionMark, Vertical ? bounds.Left : after ? bounds.Right : bounds.Left - thickness);
            Canvas.SetTop(insertionMark, !Vertical ? bounds.Top : after ? bounds.Bottom : bounds.Top - thickness);
            insertionMark.Visibility = Visibility.Visible;
        }
    }
    static double Fraction(double offset, double range) => range <= 0 ? 0 : Math.Clamp(offset / range,0,1);

    Tile? DropTarget(Point point) => tiles.FirstOrDefault(t => t != sourceTile && t.Hit.Contains(point) && Config.Buttons.Contains(t.Item));

    void ClearDrag()
    {
        var captured = pressed;
        pressed = null;
        if (sourceTile != null) sourceTile.View.Opacity = 1;
        if (dragPreview != null) {
            dragPreview.IsOpen = false; dragPreview.Child = null; dragPreview = null;
            canvas.Children.Remove(insertionMark);
        }
        sourceTile = null; dragged = movingWindow = false;
        if (captured != null && Mouse.Captured == captured) captured.ReleaseMouseCapture();
    }

    internal static bool MoveButton(DockConfig config, string sourceId, string targetId)
    {
        int from = config.Buttons.FindIndex(b => b.Id == sourceId);
        int to = config.Buttons.FindIndex(b => b.Id == targetId);
        if (from < 0 || to < 0 || from == to) return false;
        var item = config.Buttons[from];
        config.Buttons.RemoveAt(from); config.Buttons.Insert(to,item);
        return true;
    }
}
