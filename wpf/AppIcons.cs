using System;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace MacDock;

public static class AppIcons
{
    public static BitmapFrame WindowIcon() => BitmapFrame.Create(new Uri("pack://application:,,,/MacDock.Wpf;component/Assets/macdock.ico"));
    public static System.Drawing.Icon TrayIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MacDock.TrayIcon") ?? throw new InvalidOperationException("Tray icon resource is missing.");
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }
}
