using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MacDock;

public sealed partial class SettingsWindow
{
    void AddAudioSettings(StackPanel content,DockItem item)
    {
        if (item.Type == "countdown") {
            var fields = new WrapPanel();
            void AddPart(int count,int unit,string label) {
                var values = Enumerable.Range(0,count).Select(n => (n.ToString(),n.ToString("00")));
                var select = Choices(values,(item.TimerSeconds/unit%count).ToString(), value => {
                    int previous = item.TimerSeconds/unit%count;
                    item.TimerSeconds += (int.Parse(value)-previous)*unit;
                });
                select.Width = 75; fields.Children.Add(select);
                fields.Children.Add(new TextBlock { Text = label, Margin = new(0,0,10,8), VerticalAlignment = VerticalAlignment.Center });
            }
            AddPart(24,3600,"시간"); AddPart(60,60,"분"); AddPart(60,1,"초");
            content.Children.Add(Text("카운트다운 시간")); content.Children.Add(fields);
        }
        content.Children.Add(Text(item.Type == "countdown" ? "완료 알람" : "재생할 소리"));
        var sounds = Soundboard.Samples.Select(n => ("sample:"+n, SoundLabel(n))).ToList();
        if (!item.Sound.StartsWith("sample:",StringComparison.Ordinal)) sounds.Insert(0,(item.Sound,Path.GetFileName(item.Sound)));
        content.Children.Add(Choices(sounds,item.Sound,value => item.Sound = value));
        var buttons = new WrapPanel();
        buttons.Children.Add(Button("파일 선택",() => {
            var picker = new OpenFileDialog { Title = "소리 파일 선택", Filter = "오디오 파일|*.wav;*.mp3;*.wma;*.m4a;*.aac" };
            if (picker.ShowDialog(this) == true) { item.Sound = picker.FileName; Render(); }
        }));
        buttons.Children.Add(Button("미리 듣기",() => Soundboard.Play("preview",item.Sound,error => status.Text = error)));
        buttons.Children.Add(Button("미리 듣기 중지",() => Soundboard.Stop("preview")));
        content.Children.Add(buttons);
    }

    static string SoundLabel(string name)
    {
        if (name.StartsWith("alarm-")) return "알람 · " + name[6..];
        string[] parts = name.Split('_');
        string label = parts[0] switch {
            "back"=>"뒤로", "bong"=>"종", "click"=>"클릭", "close"=>"닫기", "confirmation"=>"확인",
            "drop"=>"낙하", "error"=>"오류", "glass"=>"유리", "glitch"=>"글리치", "maximize"=>"확대",
            "minimize"=>"축소", "open"=>"열기", "pluck"=>"현 튕김", "question"=>"질문", "scratch"=>"스크래치",
            "scroll"=>"스크롤", "select"=>"선택", "switch"=>"스위치", "tick"=>"틱", "toggle"=>"토글", _=>parts[0]
        };
        return parts.Length == 2 ? $"{label} {int.Parse(parts[1])}" : label;
    }
}
