using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace MacDock;

internal sealed class ShutdownWindow : Window
{
    readonly TextBox hours = new() { Text = "0", Width = 85 }, minutes = new() { Text = "30", Width = 85 };
    readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0,10,0,14) };
    readonly Button schedule = new() { Content = "종료 예약" };
    static bool handlingClick;

    internal ShutdownWindow()
    {
        Title = "Windows 자동 종료"; Icon = AppIcons.WindowIcon();
        Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = DockWindow.Brush("#11151D"); Foreground = DockWindow.Brush("#E8EDF6");
        var body = new StackPanel(); Content = new Border { Child = body, Padding = new(24), Background = Background };
        body.Children.Add(new TextBlock { Text = "얼마 후에 PC를 종료할까요?", FontSize = 18, Margin = new(0,0,0,12) });
        var row = new WrapPanel(); body.Children.Add(row);
        void Field(TextBox box, string label) {
            AutomationProperties.SetName(box, label); row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new(0,0,14,6) });
            box.TextChanged += (_,_) => Preview();
        }
        Field(hours,"시간"); Field(minutes,"분"); body.Children.Add(hint);
        body.Children.Add(new TextBlock { Text = "예약 시각에 Windows가 앱을 닫습니다. 작업 중인 파일을 미리 저장하세요.", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,14) });
        var buttons = new WrapPanel(); body.Children.Add(buttons);
        buttons.Children.Add(schedule);
        buttons.Children.Add(new Button { Content = "닫기", IsCancel = true });
        schedule.Click += async (_, _) => await Schedule();

        Preview();
    }

    internal static bool TryDelay(string hoursText, string minutesText, out int seconds)
    {
        seconds = 0;
        bool Parse(string field, out int n) => int.TryParse(string.IsNullOrWhiteSpace(field) ? "0" : field.Trim(),
            NumberStyles.None, CultureInfo.InvariantCulture, out n);
        if (!Parse(hoursText, out int h) || !Parse(minutesText, out int m) || m > 59) return false;
        long total = (h * 60L + m) * 60;
        if (total < 60 || total > 315360000) return false;
        seconds = (int)total; return true;
    }

    void Preview()
    {
        schedule.IsEnabled = TryDelay(hours.Text, minutes.Text, out int seconds);
        hint.Text = schedule.IsEnabled ? $"종료 예정  {DateTime.Now.AddSeconds(seconds):yyyy-MM-dd HH:mm}" :
            "1분 이상, 최대 3650일을 입력하세요. 분은 0~59 사이의 정수입니다.";
    }

    internal static async System.Threading.Tasks.Task Toggle()
    {
        if (handlingClick) return;
        handlingClick = true;
        try {
            // Windows reports ERROR_NO_SHUTDOWN_IN_PROGRESS when there is no timer.
            int result = await RunShutdown("/a");
            if (WasCancelled(result))
                MessageBox.Show("기존 자동 종료 예약을 취소했습니다.", "Windows 자동 종료", MessageBoxButton.OK, MessageBoxImage.Information);
            else new ShutdownWindow().ShowDialog();
        } finally { handlingClick = false; }
    }

    internal static bool WasCancelled(int exitCode) => exitCode switch {
        0 => true, 1116 => false, _ => throw new Win32Exception(exitCode)
    };

    static async System.Threading.Tasks.Task<int> RunShutdown(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe")) {
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows 종료 명령을 실행할 수 없습니다.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    async System.Threading.Tasks.Task Schedule()
    {
        if (!TryDelay(hours.Text, minutes.Text, out int seconds)) { Preview(); return; }
        IsEnabled = false;
        try {
            var due = DateTime.Now.AddSeconds(seconds);
            int result = await RunShutdown("/s", "/t", seconds.ToString(CultureInfo.InvariantCulture));
            if (result != 0) throw new Win32Exception(result);
            MessageBox.Show(this, $"{due:yyyy-MM-dd HH:mm}에 Windows가 종료됩니다. 독의 자동 종료 버튼을 다시 누르면 예약이 취소됩니다.",
                "종료 예약 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        } catch (Exception error) { hint.Text = error.Message; }
        finally { IsEnabled = true; }
    }
}