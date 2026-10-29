using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MacDock;

public sealed class MacroRecorder : Window
{
    readonly List<string> lines = new();
    readonly TextBox preview = new() { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 190 };
    readonly CheckBox timing = new() { Content = "입력 사이 대기 시간 포함" };
    readonly TextBlock hint = new() { Text = "시작을 누른 뒤 이 창에서 키를 입력하세요.", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,12) };
    readonly Stopwatch clock = new();
    bool recording; long previous; int totalWait;
    public string Recorded => string.Join("\n",lines);
    public MacroRecorder()
    {
        Title = "매크로 키 녹화"; Icon = AppIcons.WindowIcon(); Width = 570; Height = 430; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = DockWindow.Brush("#11151D"); Foreground = Brushes.White;
        var body = new StackPanel { Margin = new Thickness(24) }; Content = body;
        body.Children.Add(hint); body.Children.Add(timing); body.Children.Add(preview);
        var controls = new WrapPanel { Margin = new Thickness(0,12,0,0) }; body.Children.Add(controls);
        void Add(string text,Action action) { var button = new Button { Content = text }; button.Click += (_,_) => action(); controls.Children.Add(button); }
        Add("● 녹화 시작",() => { lines.Clear(); preview.Text = ""; previous = -1; totalWait = 0; clock.Restart(); recording = true; timing.IsEnabled = false; hint.Text = "녹화 중 · F8: 중지 · Esc: 취소\n이 창 안에서만 기록합니다. Windows 키 조합은 직접 입력하세요."; preview.Focus(); });
        Add("■ 중지",Stop);
        Add("입력에 적용",() => { Stop(); if (lines.Count == 0) { hint.Text = "먼저 키를 녹화하세요."; return; } Macro.Parse(Recorded); DialogResult = true; });
        Add("취소",() => { recording = false; DialogResult = false; });
        PreviewKeyDown += Capture;
        Deactivated += (_,_) => { if (recording) { Stop(); hint.Text = "다른 창으로 이동하여 녹화를 중지했습니다. 기록을 확인 후 적용하세요."; } };
        Closed += (_,_) => recording = false;
    }
    void Stop() { recording = false; clock.Stop(); timing.IsEnabled = true; hint.Text = "녹화 중지 · 확인 후 ‘입력에 적용’을 누르세요."; }
    public static string? Chord(Key key, ModifierKeys modifiers)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed or Key.DeadCharProcessed) return null;
        if ((modifiers & ModifierKeys.Windows) != 0) return null;
        int vk = KeyInterop.VirtualKeyFromKey(key); if (vk < 8 || vk > 254) return null;
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        parts.Add(Macro.NameForKey((ushort)vk)); return string.Join("+",parts);
    }
    void Capture(object sender,KeyEventArgs e)
    {
        if (!recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F8) { Stop(); return; }
        if (key == Key.Escape) { recording = false; DialogResult = false; return; }
        if (e.IsRepeat) return;
        var chord = Chord(key,Keyboard.Modifiers); if (chord == null) return;
        if (lines.Count >= 98) { Stop(); return; }
        long now = clock.ElapsedMilliseconds;
        if (timing.IsChecked == true && previous >= 0) { int delay = (int)Math.Min(10000,now-previous); delay = Math.Min(delay,30000-totalWait); if (delay >= 50) { lines.Add("wait " + delay); totalWait += delay; } }
        lines.Add(chord); previous = now; preview.Text = Recorded; preview.ScrollToEnd();
    }
}
