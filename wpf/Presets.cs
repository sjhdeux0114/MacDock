using System;
using System.Collections.Generic;

namespace MacDock;

public static class Presets
{
    public static readonly (string Name, string Type, string Target, string Icon)[] Items = {
        ("GPU 사용량", "gpu", "", "▥"), ("GPU 온도", "gpu-temperature", "", "◉"), ("이더넷 업로드", "network-up", "", "↑"), ("이더넷 다운로드", "network-down", "", "↓"),
        ("계산기", "program", "calc.exe", "▦"), ("메모장", "program", "notepad.exe", "▤"), ("그림판", "program", "mspaint.exe", "✎"),
        ("파일 탐색기", "program", "explorer.exe", "▰"), ("작업 관리자", "program", "taskmgr.exe", "▥"),
        ("Windows 설정", "program", "ms-settings:", "⚙"), ("화면 캡처", "macro", "Win+Shift+S", "✂"),
        ("복사", "macro", "Ctrl+C", "⧉"), ("붙여넣기", "macro", "Ctrl+V", "▣"), ("실행 취소", "macro", "Ctrl+Z", "↶"),
        ("전체 선택", "macro", "Ctrl+A", "▧"), ("저장", "macro", "Ctrl+S", "▣"), ("USB 모두 제거", "usb-eject", "", "⏏")
    };
    public static DockItem Create(int index)
    {
        var p = Items[index]; return new DockItem { Label = p.Name, Type = p.Type, Target = p.Target, Icon = p.Icon, Color = p.Type == "macro" ? "#59687d" : "#387e98" };
    }
}
