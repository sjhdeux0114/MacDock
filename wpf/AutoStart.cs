using System;
using System.IO;
using Microsoft.Win32;

namespace MacDock;

public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "MacDock.Wpf";
    public static bool Enabled() { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(ValueName) is string; }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled) { key.DeleteValue(ValueName,false); return; }
        string exe = Environment.ProcessPath ?? throw new Exception("실행 파일 경로를 찾을 수 없습니다.");
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase)) throw new Exception("배포 폴더의 실행 파일로 시작한 뒤 자동 실행을 설정하세요.");
        string launcher = Path.Combine(Path.GetDirectoryName(exe)!,"Run-MacDock.cmd");
        string command = File.Exists(launcher) ? "\"" + Path.Combine(Environment.SystemDirectory,"cmd.exe") + "\" /d /c \"\"" + launcher + "\"\"" : "\"" + exe + "\"";
        key.SetValue(ValueName,command,RegistryValueKind.String);
    }
}
