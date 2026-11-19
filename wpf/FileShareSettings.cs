using System;

using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MacDock;

internal sealed class FileShareSettings : StackPanel
{
    readonly TextBox folder = new(), port = new();
    readonly PasswordBox password = new(), confirm = new();
    readonly CheckBox startup = new() { Content = "Windows 로그인 시 파일 공유 서버 시작" };
    readonly TextBox addresses = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0,12,0,0) };
    readonly Button toggle = new() { Content = "서버 시작" };
    FileShare.State? current;
    bool busy;
    internal FileShareSettings()
    {
        Margin = new(34,16,34,24);
        TextBlock.SetForeground(this, DockWindow.Brush("#E8EDF6"));
        foreach (var box in new[] { password, confirm }) {
            box.Background = DockWindow.Brush("#1B2330"); box.Foreground = DockWindow.Brush("#E8EDF6");
            box.BorderBrush = DockWindow.Brush("#344154"); box.Padding = new(8); box.Margin = new(0,0,8,8);
        }
        Label("공유 폴더");
        var row = new DockPanel();
        var browse = new Button { Content = "폴더 선택" };
        browse.Click += (_, _) => {
            var picker = new OpenFolderDialog { Title = "공유할 폴더 선택" };
            if (picker.ShowDialog(Window.GetWindow(this)) == true) folder.Text = picker.FolderName;
        };
        DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(folder); Children.Add(row);
        Label("접속 포트"); Children.Add(port);
        Label("접속 비밀번호 (4~128자, 변경할 때만 입력)"); Children.Add(password);
        Label("비밀번호 확인"); Children.Add(confirm);
        Children.Add(startup);
        var actions = new WrapPanel { Margin = new(0,12,0,8) };
        var save = new Button { Content = "서버 설정 저장" };
        save.Click += async (_, _) => await Save();
        toggle.Click += async (_, _) => await Execute(current?.Running == true ? "stop" : "start");
        actions.Children.Add(save); actions.Children.Add(toggle); Children.Add(actions);
        Label("같은 Wi-Fi에서 접속할 주소"); Children.Add(addresses);
        Children.Add(message);

    }

    void Label(string value) => Children.Add(new TextBlock { Text = value, Margin = new(0,10,0,4) });

    async Task Save()
    {
        if (!int.TryParse(port.Text, out int number) || number < 1 || number > 65535) { message.Text = "포트는 1~65535 사이로 입력하세요."; return; }
        if (password.Password != confirm.Password) { message.Text = "비밀번호 확인이 일치하지 않습니다."; return; }
        await Execute("save", new { sharedRoot = folder.Text.Trim(), port = number,
            password = password.Password.Length == 0 ? null : password.Password,
            autoStart = startup.IsChecked != current?.AutoStart ? startup.IsChecked : null });
    }

    internal Task LoadSettings() => current == null ? Execute("info") : Task.CompletedTask;

    internal async Task Execute(string action, object? payload = null)
    {
        if (busy) return;
        busy = true; IsEnabled = false;
        try {
            string? failure = null;
            try { current = await Task.Run(() => FileShare.Command(action, payload)); }
            catch (Exception error) when (action == "start") {
                failure = error.Message;
                current = await Task.Run(() => FileShare.Command("info"));
            }
            folder.Text = current.SharedRoot; port.Text = current.ConfiguredPort.ToString();
            startup.IsChecked = current.AutoStart;
            password.Clear(); confirm.Clear();
            addresses.Text = string.Join(Environment.NewLine, current.PhoneUrls);
            toggle.Content = current.Running ? "서버 중지" : "서버 시작";
            toggle.IsEnabled = !current.NeedsSetup;
            message.Text = current.NeedsSetup ? "공유 폴더와 비밀번호를 저장한 뒤 서버를 시작하세요." :
                current.Running && current.ActivePort != current.ConfiguredPort ? $"현재 포트는 {current.ActivePort}입니다. 서버를 중지한 뒤 시작하면 새 포트가 적용됩니다." :
                current.Running ? $"서버 실행 중 · 포트 {current.ActivePort}" : "서버가 중지되어 있습니다.";
            if (failure != null) message.Text = failure;
            if (action == "save") message.Text = "서버 설정을 저장했습니다. " + message.Text;
        } catch (Exception error) { message.Text = error.Message; }
        finally { IsEnabled = true; busy = false; }
    }
}
