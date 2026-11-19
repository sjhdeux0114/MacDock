using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MacDock;

public sealed partial class SettingsWindow
{
    DockWindow.DockScene? previewScene;

    internal void VerifyPreview() => dock.VerifyPreview(previewScene!);

    void RefreshPreviewValues()
    {
        if (previewScene != null) dock.UpdateValues(previewScene);
    }

    void ShowDockPreview()
    {
        var scene = new DockWindow.DockScene { page = previewPage };
        scene.MeasureWindow(draft);
        scene.BuildTiles(draft);
        previewPage = scene.page;
        var bounds = scene.WindowBounds;
        scene.Arrange(bounds.Size,new Point(-100000,-100000),0,0);
        previewScene = scene;
        RefreshPreviewValues();

        var desktop = new Canvas {
            Width = scene.DesktopSize.Width, Height = scene.DesktopSize.Height, ClipToBounds = true,
            Background = new LinearGradientBrush(Color.FromRgb(43,68,82), Color.FromRgb(20,31,45),65)
        };
        var surface = scene.canvas;
        surface.Width = bounds.Width; surface.Height = bounds.Height;
        desktop.Children.Add(surface);
        Canvas.SetLeft(surface,bounds.X); Canvas.SetTop(surface,bounds.Y);
        preview.Child = desktop;

        foreach (var tile in scene.tiles) {
            var item = tile.Item;
            var button = Button(item.Label, () => {
                switch (item.Type) {
                    case "settings": monitorChoice.Focus(); break;
                    case "previous-page": previewPage = (scene.page + scene.PageCount - 1) % scene.PageCount; ShowDockPreview(); break;
                    case "next-page": previewPage = (scene.page + 1) % scene.PageCount; ShowDockPreview(); break;
                    default: if (ValidFields()) Navigate(DockScreen.Item,item); break;
                }
            });
            button.Opacity = 0; button.Margin = new(0); button.ToolTip = item.Label;
            button.Width = tile.Hit.Width; button.Height = tile.Hit.Height;
            Canvas.SetLeft(button,tile.Hit.X); Canvas.SetTop(button,tile.Hit.Y);
            surface.Children.Add(button);
            if (draft.Buttons.Contains(item)) EnablePreviewReorder(button,scene,tile);
            if (item == selectedItem) {
                tile.View.BorderBrush = DockWindow.Brush("#BCFFF1"); tile.View.BorderThickness = new(2);
            }
        }
        var plate = scene.plate;
        plate.Cursor = Cursors.SizeAll;
        double dx = Math.Max(0,desktop.Width-bounds.Width), dy = Math.Max(0,desktop.Height-bounds.Height);
        Vector grab = default;
        plate.MouseLeftButtonDown += (_, e) => {
            var pointer = e.GetPosition(desktop);
            grab = pointer - new Point(Canvas.GetLeft(surface), Canvas.GetTop(surface));
            e.Handled = plate.CaptureMouse();
        };
        plate.MouseMove += (_, e) => {
            if (!plate.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(desktop) - grab;
            double left = Math.Clamp(point.X, 0, dx), top = Math.Clamp(point.Y, 0, dy);
            if (left == Canvas.GetLeft(surface) && top == Canvas.GetTop(surface)) return;
            Canvas.SetLeft(surface, left); Canvas.SetTop(surface, top);
            draft.FreeX = dx > 0 ? left / dx : .5;
            draft.FreeY = dy > 0 ? top / dy : .5;
            status.Text = "변경 저장을 누르면 독 위치가 적용됩니다.";
        };
        plate.MouseLeftButtonUp += (_, _) => { if (plate.IsMouseCaptured) plate.ReleaseMouseCapture(); };
    }

    void EnablePreviewReorder(Button button, DockWindow.DockScene scene, DockWindow.Tile source)
    {
        Point press = default;
        Vector grab = default;
        Border? face = null;
        Border? marker = null;
        DockWindow.Tile? Target(Point point) => scene.tiles.FirstOrDefault(tile =>
            tile != source && tile.Hit.Contains(point) && draft.Buttons.Contains(tile.Item));

        void ClearFeedback()
        {
            if (marker == null) return;
            scene.canvas.Children.Remove(marker);
            scene.canvas.Children.Remove(face!);
            face = null;
            marker = null;
            source.View.Opacity = 1;
            button.ClearValue(CursorProperty);
        }

        // Button handles capture and ordinary clicks. Measure the drag threshold in
        // window coordinates and hit-test tiles in the scaled preview's coordinates.
        button.PreviewMouseLeftButtonDown += (_, e) => {
            press = e.GetPosition(this);
            grab = e.GetPosition(scene.canvas) - source.Hit.TopLeft;
        };
        button.PreviewMouseMove += (_, e) => {
            if (!button.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
            if (marker == null) {
                var delta = e.GetPosition(this) - press;
                if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                marker = new Border { Background = DockWindow.Brush("#BCFFF1"), IsHitTestVisible = false };
                Panel.SetZIndex(marker,1000);
                scene.canvas.Children.Add(marker);
                face = DockWindow.CreateDragFace(source);
                Panel.SetZIndex(face,999);
                scene.canvas.Children.Add(face);
                source.View.Opacity = .45;
                button.Cursor = Cursors.SizeAll;
            }
            var pointer = e.GetPosition(scene.canvas);
            Canvas.SetLeft(face!,pointer.X-grab.X+4); Canvas.SetTop(face!,pointer.Y-grab.Y+4);
            var target = Target(pointer);
            marker.Visibility = target == null ? Visibility.Hidden : Visibility.Visible;
            if (target != null) {
                bool after = draft.Buttons.IndexOf(source.Item) < draft.Buttons.IndexOf(target.Item);
                bool vertical = draft.Position is "left" or "right";
                var hit = target.Hit;
                marker.Width = vertical ? hit.Width : 4;
                marker.Height = vertical ? 4 : hit.Height;
                Canvas.SetLeft(marker,vertical ? hit.Left : after ? hit.Right : hit.Left - 4);
                Canvas.SetTop(marker,!vertical ? hit.Top : after ? hit.Bottom : hit.Top - 4);
            }
            e.Handled = true;
        };
        button.PreviewMouseLeftButtonUp += (_, e) => {
            if (marker == null) return;
            var target = Target(e.GetPosition(scene.canvas));
            e.Handled = true;
            button.ReleaseMouseCapture();
            if (target != null && ValidFields() && DockWindow.MoveButton(draft,source.Item.Id,target.Item.Id)) {
                Render();
                status.Text = "버튼 순서를 바꿨습니다. 변경 저장을 누르면 독에 적용됩니다.";
            }
        };
        button.LostMouseCapture += (_, _) => ClearFeedback();
        button.PreviewKeyDown += (_, e) => {
            if (marker == null || e.Key != Key.Escape) return;
            button.ReleaseMouseCapture(); e.Handled = true;
        };
    }
}
