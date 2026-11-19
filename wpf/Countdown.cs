using System;

namespace MacDock;

internal enum CountdownPhase { Ready, Running, Paused, Finished }

internal sealed class Countdown(int duration)
{
    internal int Duration { get; } = duration;
    internal CountdownPhase Phase { get; private set; }
    TimeSpan end, remaining = TimeSpan.FromSeconds(duration);
    internal TimeSpan Remaining(TimeSpan now) => Phase == CountdownPhase.Running
        ? TimeSpan.FromTicks(Math.Max(0,(end-now).Ticks)) : remaining;
    internal void Toggle(TimeSpan now)
    {
        // A click can arrive after the deadline but before the next UI tick.
        if (Phase == CountdownPhase.Finished || Complete(now)) {
            Reset();
            return;
        }
        if (Phase == CountdownPhase.Running) {
            remaining = Remaining(now); Phase = CountdownPhase.Paused;
        } else {
            end = now + remaining; Phase = CountdownPhase.Running;
        }
    }
    internal void Reset() { remaining = TimeSpan.FromSeconds(Duration); Phase = CountdownPhase.Ready; }
    internal bool Complete(TimeSpan now)
    {
        if (Phase != CountdownPhase.Running || now < end) return false;
        remaining = TimeSpan.Zero; Phase = CountdownPhase.Finished; return true;
    }
    internal static string Format(int seconds) => seconds >= 3600
        ? $"{seconds/3600}:{seconds/60%60:00}:{seconds%60:00}" : $"{seconds/60:00}:{seconds%60:00}";
}
