using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MacDock;

public record MacroStep(ushort[]? Keys = null, string? Text = null, int Wait = 0);
public static class Macro
{
    public static readonly string[] Media = { "PLAYPAUSE", "NEXT", "PREVIOUS", "STOP", "MUTE", "VOLUMEUP", "VOLUMEDOWN" };
    static readonly Dictionary<string, ushort> Codes = new(StringComparer.OrdinalIgnoreCase) {
        ["CTRL"] = 17, ["CONTROL"] = 17, ["SHIFT"] = 16, ["ALT"] = 18, ["WIN"] = 91, ["ENTER"] = 13, ["TAB"] = 9,
        ["ESC"] = 27, ["ESCAPE"] = 27, ["SPACE"] = 32, ["BACKSPACE"] = 8, ["DELETE"] = 46, ["INSERT"] = 45, ["HOME"] = 36, ["END"] = 35,
        ["PAGEUP"] = 33, ["PAGEDOWN"] = 34, ["LEFT"] = 37, ["UP"] = 38, ["RIGHT"] = 39, ["DOWN"] = 40, ["PRINTSCREEN"] = 44,
        ["MUTE"] = 173, ["VOLUMEDOWN"] = 174, ["VOLUMEUP"] = 175, ["NEXT"] = 176, ["PREVIOUS"] = 177, ["STOP"] = 178, ["PLAYPAUSE"] = 179
    };
    static bool busy;
    static Macro() { for (int i = 1; i <= 24; i++) Codes["F" + i] = (ushort)(111 + i); }
    public static string NameForKey(ushort vk) => Codes.FirstOrDefault(p => p.Value == vk).Key ?? (vk is >= 48 and <= 57 or >= 65 and <= 90 ? ((char)vk).ToString() : "VK_" + vk.ToString("X2"));
    static ushort ParseKey(string name)
    {
        if (Codes.TryGetValue(name,out ushort vk)) return vk;
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) return name[0];
        if (name.Length == 5 && name.StartsWith("VK_") && ushort.TryParse(name[3..],System.Globalization.NumberStyles.HexNumber,null,out vk) && vk >= 8 && vk <= 254) return vk;
        throw new Exception("지원하지 않는 키: " + name);
    }
    public static List<MacroStep> Parse(string source)
    {
        var result = new List<MacroStep>(); int waitTotal = 0;
        foreach (string line in source.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)) {
            string text = line.Trim(); if (text.Length == 0) continue;
            if (text.StartsWith("wait ", StringComparison.OrdinalIgnoreCase)) {
                if (!int.TryParse(text[5..], out int ms) || ms < 0 || ms > 10000 || (waitTotal += ms) > 30000) throw new Exception("대기는 단계당 10초, 전체 30초 이하입니다.");
                result.Add(new(Wait: ms));
            } else if (text.StartsWith("text ", StringComparison.OrdinalIgnoreCase)) {
                if (text.Length > 2005) throw new Exception("텍스트 입력은 2000자 이하입니다."); result.Add(new(Text: text[5..]));
            } else {
                var names = text.ToUpperInvariant().Split('+', StringSplitOptions.TrimEntries);
                if (names.Length > 5 || names.Distinct().Count() != names.Length) throw new Exception("키 조합을 확인하세요.");
                var keys = names.Select(ParseKey).ToArray();
                result.Add(new(Keys: keys));
            }
        }
        if (result.Count < 1 || result.Count > 100) throw new Exception("매크로는 1~100단계로 입력하세요."); return result;
    }
    static Native.Input Key(ushort vk, ushort scan, uint flags) => new() { Type = 1, Data = new() { Keyboard = new() { Key = vk, Scan = scan, Flags = flags } } };
    static uint Extended(ushort vk) => vk == 91 || (vk >= 33 && vk <= 46) || (vk >= 173 && vk <= 179) || vk == 111 ? 1u : 0u;
    static void Send(Native.Input[] events)
    {
        if (Native.SendInput((uint)events.Length, events, Marshal.SizeOf<Native.Input>()) == events.Length) return;
        var releases = events.Where(e => (e.Data.Keyboard.Flags & 2) == 0).Reverse().Select(e => Key(e.Data.Keyboard.Key, e.Data.Keyboard.Scan, e.Data.Keyboard.Flags | 2)).ToArray();
        Native.SendInput((uint)releases.Length, releases, Marshal.SizeOf<Native.Input>());
        throw new Exception("키 입력이 차단되었습니다. 관리자 권한 앱에는 입력할 수 없을 수 있습니다.");
    }
    public static async Task Run(string source)
    {
        var steps = Parse(source); if (busy) throw new Exception("다른 매크로가 실행 중입니다."); busy = true;
        try {
            foreach (var step in steps) {
                if (step.Keys != null) Send(step.Keys.Select(k => Key(k, 0, Extended(k))).Concat(step.Keys.Reverse().Select(k => Key(k, 0, Extended(k) | 2))).ToArray());
                else if (step.Text != null) Send(step.Text.SelectMany(c => new[] { Key(0, c, 4), Key(0, c, 6) }).ToArray());
                else await Task.Delay(step.Wait);
            }
        } finally { busy = false; }
    }
}

public static class Actions
{
    public static async Task Run(DockItem item)
    {
        switch (item.Type) {
            case "clock": case "cpu": case "memory": case "gpu": case "gpu-temperature": case "network-up": case "network-down": return;
            case "volume": await Task.Run(() => Audio.Change(item.Target, item.VolumeStep)); return;
            case "macro": await Macro.Run(item.Target); return;
            case "media": if (!Macro.Media.Contains(item.Target)) throw new Exception("알 수 없는 미디어 명령입니다."); await Macro.Run(item.Target); return;
            case "url": if (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) throw new Exception("HTTP/HTTPS 주소만 지원합니다."); break;
            case "path": case "program": break;
            case "folder": if (!Directory.Exists(item.Target)) throw new Exception("폴더를 찾을 수 없습니다. 경로 또는 드라이브 연결을 확인하세요."); break;
            default: throw new Exception("지원하지 않는 기능입니다.");
        }
        if (string.IsNullOrWhiteSpace(item.Target)) throw new Exception("실행할 경로를 입력하세요.");
        Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
    }
}

