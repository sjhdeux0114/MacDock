using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock;

public static class IconLibrary
{
    // Original, resolution-independent 24x24 line artwork, embedded in the app.
    public static readonly (string Name, string Path)[] Items = {
        ("폴더", "M3,7 L10,7 12,9 21,9 21,20 3,20 Z M3,7 L3,4 10,4 12,7 21,7 21,9"),
        ("문서", "M5,3 L15,3 20,8 20,21 5,21 Z M15,3 L15,8 20,8 M8,12 L17,12 M8,16 L17,16"),
        ("저장", "M4,3 L18,3 21,6 21,21 3,21 3,3 Z M7,3 L7,9 16,9 16,3 M7,21 L7,14 17,14 17,21"),
        ("복사", "M8,7 L21,7 21,21 8,21 Z M5,17 L3,17 3,3 16,3 16,5"),
        ("클립보드", "M8,5 L4,5 4,21 20,21 20,5 16,5 M8,3 L16,3 16,7 8,7 Z M8,12 L16,12 M8,16 L16,16"),
        ("휴지통", "M3,6 L21,6 M9,6 L9,3 15,3 15,6 M5,6 L6,21 18,21 19,6 M10,10 L10,17 M14,10 L14,17"),
        ("검색", "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L21,21"),
        ("홈", "M2,11 L12,3 22,11 M5,9 L5,21 10,21 10,15 14,15 14,21 19,21 19,9"),
        ("웹", "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M3,12 L21,12 M12,3 C6,8 6,16 12,21 C18,16 18,8 12,3"),
        ("모니터", "M3,4 L21,4 21,17 3,17 Z M12,17 L12,21 M7,21 L17,21"),
        ("키보드", "M2,5 L22,5 22,20 2,20 Z M5,9 L7,9 M10,9 L12,9 M15,9 L17,9 M19,9 L20,9 M5,13 L7,13 M10,13 L12,13 M15,13 L19,13 M7,17 L17,17"),
        ("마우스", "M6,8 C6,1 18,1 18,8 L18,16 C18,23 6,23 6,16 Z M12,3 L12,10 M6,10 L18,10"),
        ("계산기", "M5,2 L19,2 19,22 5,22 Z M8,5 L16,5 16,9 8,9 Z M8,13 L9,13 M14,13 L16,13 M8,17 L9,17 M14,17 L16,17"),
        ("그림", "M3,3 L21,3 21,21 3,21 Z M3,17 L9,11 14,16 17,13 21,17 M17,7 A1,1 0 1 1 15,7 A1,1 0 1 1 17,7"),
        ("펜", "M4,16 L16,4 20,8 8,20 3,21 Z M14,6 L18,10 M4,16 L8,20"),
        ("시계", "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M12,6 L12,12 16,14"),
        ("달력", "M3,5 L21,5 21,21 3,21 Z M3,10 L21,10 M7,2 L7,7 M17,2 L17,7 M7,14 L9,14 M14,14 L16,14 M7,18 L9,18"),
        ("CPU", "M6,6 L18,6 18,18 6,18 Z M9,9 L15,9 15,15 9,15 Z M9,2 L9,6 M15,2 L15,6 M9,18 L9,22 M15,18 L15,22 M2,9 L6,9 M2,15 L6,15 M18,9 L22,9 M18,15 L22,15"),
        ("그래프", "M3,3 L3,21 22,21 M7,17 L11,11 15,14 21,5"),
        ("재생", "M7,3 L21,12 7,21 Z"), ("일시정지", "M6,3 L9,3 9,21 6,21 Z M15,3 L18,3 18,21 15,21 Z"),
        ("다음", "M3,5 L14,12 3,19 Z M18,4 L18,20"), ("정지", "M5,5 L19,5 19,19 5,19 Z"),
        ("음악", "M9,17 L9,5 20,3 20,15 M9,7 L20,5 M9,17 A3,2 0 1 1 3,17 A3,2 0 1 1 9,17 M20,15 A3,2 0 1 1 14,15 A3,2 0 1 1 20,15"),
        ("볼륨", "M3,9 L7,9 12,4 12,20 7,15 3,15 Z M16,8 C20,10 20,14 16,16 M19,4 C25,9 25,15 19,20"),
        ("음소거", "M3,9 L7,9 12,4 12,20 7,15 3,15 Z M16,9 L22,15 M22,9 L16,15"),
        ("마이크", "M9,5 C9,1 15,1 15,5 L15,12 C15,16 9,16 9,12 Z M5,10 L5,12 C5,21 19,21 19,12 L19,10 M12,19 L12,22 M8,22 L16,22"),
        ("카메라", "M3,7 L8,7 10,4 15,4 17,7 21,7 21,21 3,21 Z M16,14 A4,4 0 1 1 8,14 A4,4 0 1 1 16,14"),
        ("다운로드", "M12,2 L12,16 M6,10 L12,16 18,10 M3,16 L3,21 21,21 21,16"),
        ("업로드", "M12,16 L12,2 M6,8 L12,2 18,8 M3,16 L3,21 21,21 21,16"),
        ("전원", "M12,2 L12,11 M7,5 A9,9 0 1 0 17,5"),
        ("잠금", "M5,10 L19,10 19,22 5,22 Z M8,10 L8,6 C8,0 16,0 16,6 L16,10 M12,14 L12,18"),
        ("별", "M12,2 L15,8 22,9 17,14 18,22 12,18 6,22 7,14 2,9 9,8 Z"),
        ("하트", "M12,21 C-6,9 4,-3 12,6 C20,-3 30,9 12,21 Z"),
        ("확인", "M3,12 L9,18 21,5"), ("추가", "M12,3 L12,21 M3,12 L21,12"),
        ("메일", "M3,5 L21,5 21,19 3,19 Z M3,5 L12,13 21,5"),
        ("번개", "M13,2 L4,14 11,14 9,22 21,9 13,9 Z"),
        ("새로고침", "M20,7 A9,9 0 1 0 21,15 M20,2 L20,8 14,8"),
        ("게임", "M8,7 L16,7 C22,7 25,22 18,19 L15,16 9,16 6,19 C-1,22 2,7 8,7 M6,10 L6,14 M4,12 L8,12 M16,11 L16,11.1 M19,13 L19,13.1")
    };
    public static readonly string[] Colors = { "#387EE8", "#2563EB", "#1D4ED8", "#0891B2", "#386976", "#508A95", "#0F766E", "#059669", "#385B47", "#639B81", "#65A30D", "#D19A32", "#D97706", "#EA580C", "#DC2626", "#BE123C", "#8D5266", "#DB2777", "#9333EA", "#604C7A", "#7C3AED", "#59687D", "#334155", "#182735" };
    public static DrawingImage Drawing(int index)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,28,28))));
        var geometry = Geometry.Parse(Items[index].Path).Clone(); geometry.Transform = new TranslateTransform(2,2);
        group.Children.Add(new GeometryDrawing(null,new Pen(Brushes.White,1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },geometry));
        group.Freeze(); return new DrawingImage(group);
    }
    public static string DataUrl(int index)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawImage(Drawing(index),new Rect(8,8,112,112));
        var image = new RenderTargetBitmap(128,128,96,96,PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = new MemoryStream(); encoder.Save(stream);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
}

public sealed class IconPicker : Window
{
    public int? Selected { get; private set; }
    public IconPicker(string background)
    {
        Title = "내장 아이콘 선택"; Icon = AppIcons.WindowIcon(); Width = 660; Height = 560; Background = DockWindow.Brush("#11151D"); Foreground = Brushes.White; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new WrapPanel { Margin = new Thickness(14) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        for (int i = 0; i < IconLibrary.Items.Length; i++) {
            int index = i; var contents = new StackPanel(); contents.Children.Add(new Image { Source = IconLibrary.Drawing(i), Width = 36, Height = 36 });
            contents.Children.Add(new TextBlock { Text = IconLibrary.Items[i].Name, TextAlignment = TextAlignment.Center, Margin = new Thickness(0,5,0,0), Foreground = Brushes.White });
            var button = new Button { Content = contents, Width = 94, Height = 82, Margin = new Thickness(3), Background = DockWindow.Brush(System.Text.RegularExpressions.Regex.IsMatch(background,"^#[0-9a-fA-F]{6}$") ? background : "#386976") };
            button.Click += (_,_) => { Selected = index; DialogResult = true; }; panel.Children.Add(button);
        }
    }
}
