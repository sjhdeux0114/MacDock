using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MacDock;

public sealed class DockItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Label { get; set; } = "새 버튼";
    public string Icon { get; set; } = "◇";
    public string Color { get; set; } = "#508a95";
    public string Image { get; set; } = "";
    public string Type { get; set; } = "url";
    public string Target { get; set; } = "";
    public int VolumeStep { get; set; } = 5;
    public static string FolderName(string path)
    {
        string trimmed = path.Trim().TrimEnd('\\', '/');
        int split = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        string name = trimmed[(split + 1)..];
        return name.EndsWith(':') ? name + "\\" : name;
    }
    public void SetFolderPath(string path)
    {
        bool automatic = string.IsNullOrWhiteSpace(Label) || Label == "새 버튼" || Label == "폴더" || Label == FolderName(Target);
        Target = path;
        if (automatic && !string.IsNullOrWhiteSpace(path)) Label = FolderName(path);
    }
    public override string ToString() => Label;
}

public sealed class DockConfig
{
    public int Version { get; set; } = 2;
    public string Monitor { get; set; } = "";
    public string Position { get; set; } = "bottom";
    public double Size { get; set; } = 52;
    public double Zoom { get; set; } = 1.7;
    public double Gap { get; set; } = 8;
    public double Bottom { get; set; } = 10;
    public bool Magnify { get; set; } = true;
    public List<DockItem> Buttons { get; set; } = new();
    public static DockConfig Default() => new() { Buttons = new() {
        New("시계", "◷", "#315966", "clock", "24h"), New("CPU", "▥", "#385b47", "cpu"), New("RAM", "▥", "#604c7a", "memory"),
        New("웹 열기", "◎", "#387ee8", "url", "https://www.google.com"), New("탐색기", "▰", "#d19a32", "program", "explorer.exe"),
        New("메모장", "▤", "#639b81", "program", "notepad.exe"), New("복사", "⧉", "#59687d", "macro", "Ctrl+C"),
        New("붙여넣기", "▣", "#59687d", "macro", "Ctrl+V"), New("재생 / 일시정지", "▶", "#8d5266", "media", "PLAYPAUSE"),
        New("볼륨 내리기", "−", "#386976", "volume", "down"), New("볼륨 올리기", "+", "#386976", "volume", "up"), New("음소거", "♪", "#386976", "volume", "mute")
    } };
    static DockItem New(string label, string icon, string color, string type, string target = "") => new() { Label = label, Icon = icon, Color = color, Type = type, Target = target };
    public DockConfig Clone() => JsonSerializer.Deserialize<DockConfig>(JsonSerializer.Serialize(this, Store.Json), Store.Json)!;
    public void Validate()
    {
        if (!double.IsFinite(Size) || Size < 32 || Size > 80 || !double.IsFinite(Zoom) || Zoom < 1 || Zoom > 2.2 || !double.IsFinite(Gap) || Gap < 4 || Gap > 20 || !double.IsFinite(Bottom) || Bottom < 0 || Bottom > 100) throw new Exception("독 크기 또는 간격의 범위를 확인하세요.");
        if (Position is not ("bottom" or "top" or "left" or "right")) throw new Exception("독 위치를 선택하세요.");
        if (Buttons == null || Buttons.Count > 100) throw new Exception("버튼은 100개까지 사용할 수 있습니다.");
        var ids = new HashSet<string>();
        foreach (var b in Buttons)
        {
            if (b == null || !new[] { "url", "program", "path", "folder", "clock", "cpu", "memory", "gpu", "gpu-temperature", "network-up", "network-down", "macro", "media", "volume" }.Contains(b.Type)) throw new Exception("지원하지 않는 버튼 종류입니다.");
            if (string.IsNullOrEmpty(b.Id) || !ids.Add(b.Id)) { b.Id = Guid.NewGuid().ToString(); ids.Add(b.Id); }
            if (b.Label == null || b.Label.Length > 80 || b.Icon == null || b.Icon.Length > 16 || b.Target == null || b.Target.Length > 8192) throw new Exception("버튼 이름 또는 내용이 너무 깁니다.");
            if (!Regex.IsMatch(b.Color ?? "", "^#[0-9a-fA-F]{6}$")) throw new Exception("버튼 색상은 #387ee8 같은 형식입니다.");
            if ((b.Image?.Length ?? 0) > 4_000_000) throw new Exception("이미지는 2.9MB 이하로 사용하세요.");
            b.Image ??= "";
            if (b.VolumeStep < 1 || b.VolumeStep > 100) throw new Exception("볼륨 조절 폭은 1~100%p입니다.");
            if (b.Type == "macro") Macro.Parse(b.Target);
            if (b.Type == "volume" && !new[] { "up", "down", "mute" }.Contains(b.Target)) throw new Exception("볼륨 동작을 선택하세요.");
            if (b.Type == "media" && !Macro.Media.Contains(b.Target)) throw new Exception("미디어 동작을 선택하세요.");
            if (b.Type == "url" && (!Uri.TryCreate(b.Target, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))) throw new Exception("웹 버튼에 HTTP/HTTPS 주소를 입력하세요.");
        }
    }
}

public static class Store
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacDock.Wpf");
    public static string FileName => Path.Combine(Folder, "settings.json");
    public static DockConfig Read(string file) { var c = JsonSerializer.Deserialize<DockConfig>(File.ReadAllText(file), Json) ?? throw new Exception("설정이 비어 있습니다."); c.Validate(); return c; }
    public static DockConfig Load() => File.Exists(FileName) ? Read(FileName) : DockConfig.Default();
    public static void Save(DockConfig config)
    {
        config.Validate(); Directory.CreateDirectory(Folder);
        File.WriteAllText(FileName + ".tmp", JsonSerializer.Serialize(config, Json));
        if (File.Exists(FileName)) File.Replace(FileName + ".tmp", FileName, FileName + ".bak");
        else File.Move(FileName + ".tmp", FileName);
    }
    public static (DockConfig Config, int Skipped) Import(string path)
    {
        var config = JsonSerializer.Deserialize<DockConfig>(File.ReadAllText(path), Json) ?? throw new Exception("설정이 비어 있습니다.");
        int removed = config.Buttons.RemoveAll(b => b.Type == "extension");
        config.Validate(); return (config, removed);
    }
}

