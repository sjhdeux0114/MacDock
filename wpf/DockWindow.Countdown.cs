using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MacDock;

public sealed partial class DockWindow
{
    readonly Dictionary<string,Countdown> countdowns = new();
    readonly DispatcherTimer countdownTick = new() { Interval = TimeSpan.FromMilliseconds(150) };
    static readonly Brush TimerRunning = Brush("#13675F"), TimerPaused = Brush("#73552C"), TimerFinished = Brush("#A63849");

    void ConfigureCountdowns()
    {
        var timers = Config.Buttons.Where(i => i.Type == "countdown").ToDictionary(i => i.Id);
        foreach (var id in countdowns.Keys.ToArray()) {
            if (!timers.TryGetValue(id,out var item) || countdowns[id].Duration != item.TimerSeconds) {
                countdowns.Remove(id); Soundboard.Stop(id);
            }
        }
        foreach (var timer in timers.Values) countdowns.TryAdd(timer.Id,new Countdown(timer.TimerSeconds));
    }

    void TimerClick(DockItem item,bool reset)
    {
        var timer = countdowns[item.Id];
        Soundboard.Stop(item.Id);
        if (reset) timer.Reset(); else timer.Toggle(watch.Elapsed);
        UpdateCountdowns();
    }

    void UpdateCountdowns()
    {
        var now = watch.Elapsed;
        foreach (var pair in countdowns) {
            if (pair.Value.Complete(now)) {
                var item = Config.Buttons.First(i => i.Id == pair.Key);
                Soundboard.Play(item.Id,item.Sound,error => { System.Media.SystemSounds.Exclamation.Play(); Notify(error); });
            }
        }
        foreach (var tile in tiles) {
            if (tile.Cooldown == null || !countdowns.TryGetValue(tile.Item.Id,out var timer)) continue;
            int seconds = (int)Math.Ceiling(timer.Remaining(now).TotalSeconds);
            tile.Value.Text = Countdown.Format(seconds);
            tile.Cooldown.SetRemaining(timer.Phase is CountdownPhase.Running or CountdownPhase.Paused ? timer.Remaining(now).TotalSeconds / timer.Duration : 0);
            if (tile.Phase == timer.Phase) continue;
            tile.Phase = timer.Phase;
            tile.Caption!.Text = timer.Phase switch { CountdownPhase.Running => "▶ 진행 중", CountdownPhase.Paused => "Ⅱ 일시정지", CountdownPhase.Finished => "✓ 완료", _ => tile.Item.Label };
            tile.View.Background = timer.Phase switch { CountdownPhase.Running => TimerRunning, CountdownPhase.Paused => TimerPaused, CountdownPhase.Finished => TimerFinished, _ => Brush(tile.Item.Color) };
        }
        countdownTick.IsEnabled = countdowns.Values.Any(t => t.Phase == CountdownPhase.Running);
    }
}
