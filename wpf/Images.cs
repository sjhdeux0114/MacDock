using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace MacDock;

public sealed class ImageFrames
{
    public List<BitmapSource> Frames { get; } = new();
    public List<int> Durations { get; } = new();
    public static ImageFrames Load(string data)
    {
        byte[] bytes = data.StartsWith("data:image/", StringComparison.Ordinal) ? Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]) : throw new Exception("지원하지 않는 이미지 형식입니다.");
        using var stream = new MemoryStream(bytes);
        using var original = System.Drawing.Image.FromStream(stream);
        var result = new ImageFrames();
        int count = original.RawFormat.Guid == ImageFormat.Gif.Guid ? original.GetFrameCount(FrameDimension.Time) : 1;
        if (count > 300) throw new Exception("GIF는 300프레임 이하로 사용하세요.");
        byte[]? delays = null;
        try { delays = original.GetPropertyItem(0x5100)?.Value; } catch (ArgumentException) { }
        // Resize frames before caching: bounded decoded memory, independent of source dimensions.
        for (int i = 0; i < count; i++) {
            if (count > 1) original.SelectActiveFrame(FrameDimension.Time, i);
            using var scaled = new System.Drawing.Bitmap(160, 160);
            using (var graphics = System.Drawing.Graphics.FromImage(scaled)) {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                double ratio = Math.Max(160d / original.Width, 160d / original.Height);
                int width = (int)(original.Width * ratio), height = (int)(original.Height * ratio);
                graphics.DrawImage(original, (160 - width) / 2, (160 - height) / 2, width, height);
            }
            using var png = new MemoryStream(); scaled.Save(png, ImageFormat.Png); png.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = png; image.EndInit(); image.Freeze(); result.Frames.Add(image);
            result.Durations.Add(delays != null && delays.Length >= (i + 1) * 4 ? Math.Max(20, BitConverter.ToInt32(delays, i * 4) * 10) : 100);
        }
        return result;
    }
    public BitmapSource At(long elapsed)
    {
        long total = 0; foreach (int ms in Durations) total += ms;
        long current = elapsed % total;
        for (int i = 0; i < Frames.Count; i++) { current -= Durations[i]; if (current < 0) return Frames[i]; }
        return Frames[0];
    }
}
