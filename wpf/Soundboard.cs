using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;

namespace MacDock;

internal static class Soundboard
{
    internal const string DefaultSound = "sample:click_001", DefaultAlarm = "sample:alarm-threeTone1";
    const string Prefix = "MacDock.Sound.";
    static readonly Assembly Assembly = typeof(Soundboard).Assembly;
    internal static readonly string[] Samples = Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".wav", StringComparison.Ordinal))
        .Select(n => n.Substring(Prefix.Length, n.Length - Prefix.Length - 4)).OrderBy(n => n).ToArray();
    static readonly Dictionary<string,MediaPlayer> playing = new();
    static DirectoryInfo? cache;

    internal static bool IsValid(string? choice) => choice != null && choice.Length <= 8192 &&
        (choice.StartsWith("sample:", StringComparison.Ordinal) ? Samples.Contains(choice[7..]) : Path.IsPathFullyQualified(choice));

    internal static string Resolve(string choice)
    {
        if (!IsValid(choice)) throw new InvalidDataException("소리를 선택하세요.");
        if (!choice.StartsWith("sample:",StringComparison.Ordinal)) {
            if (!File.Exists(choice)) throw new FileNotFoundException("선택한 소리 파일을 찾을 수 없습니다.",choice);
            return choice;
        }
        string name = choice[7..] + ".wav";
        cache ??= Directory.CreateTempSubdirectory("MacDock-audio-");
        string path = Path.Combine(cache.FullName,name);
        if (!File.Exists(path)) {
            using var input = Assembly.GetManifestResourceStream(Prefix+name)!;
            try { using var output = File.Create(path); input.CopyTo(output); }
            catch { File.Delete(path); throw; }
        }
        return path;
    }

    internal static void Stop(string channel)
    {
        if (playing.Remove(channel,out var player)) player.Close();
    }

    internal static void Play(string channel,string choice,Action<string> error)
    {
        Stop(channel);
        var player = new MediaPlayer();
        playing[channel] = player;
        void Finish() { if (playing.TryGetValue(channel,out var active) && active == player) Stop(channel); }
        player.MediaEnded += (_,_) => Finish();
        player.MediaFailed += (_,e) => {
            if (!playing.TryGetValue(channel,out var active) || active != player) return;
            Finish(); error("소리를 재생할 수 없습니다. " + e.ErrorException.Message);
        };
        try { player.Open(new Uri(Resolve(choice))); player.Volume = 1; player.Play(); }
        catch (Exception ex) { Finish(); error(ex.Message); }
    }

    internal static void Close()
    {
        foreach (string key in playing.Keys.ToArray()) Stop(key);
        if (cache != null) {
            try { cache.Delete(true); } catch (IOException) {} catch (UnauthorizedAccessException) {}
            cache = null;
        }
    }
}
