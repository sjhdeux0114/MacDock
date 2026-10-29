using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MacDock;

public sealed class SettingsWindow : Window
{
    readonly DockWindow dock;
    DockConfig draft;
    readonly StackPanel body = new();
    readonly TextBlock status = new() { Text = "준비됨", Margin = new(30,10,30,10) };
    readonly Dictionary<TextBox, Func<bool>> validators = new();
    bool autoStart = AutoStart.Enabled();
    static readonly (string Key, string Label)[] Types = { ("url", "웹사이트"), ("program", "프로그램"), ("folder", "지정 폴더 열기"), ("path", "파일 / 폴더"), ("clock", "실시간 시계"), ("cpu", "CPU 사용량"), ("memory", "메모리 사용량"), ("gpu", "GPU 사용량 (NVIDIA)"), ("gpu-temperature", "GPU 온도 (NVIDIA)"), ("network-up", "이더넷 업로드"), ("network-down", "이더넷 다운로드"), ("macro", "키 매크로 / 텍스트"), ("media", "미디어 제어"), ("volume", "시스템 볼륨"), ("usb-eject", "USB 모두 안전하게 제거") };
    public SettingsWindow(DockWindow dock)
    {
        this.dock = dock; draft = dock.Config.Clone(); Title = "MacDock WPF 설정"; Icon = AppIcons.WindowIcon(); Width = 940; Height = 800; MinWidth = 740; MinHeight = 550;
        Background = DockWindow.Brush("#11151D"); Foreground = DockWindow.Brush("#E8EDF6"); FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
        var layout = new DockPanel { Background = Background }; Content = layout;
        var header = new Grid { Margin = new(34,26,34,20) }; header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel(); title.Children.Add(Text("YOUR WORKSPACE, ONE ROW.", 10, "#8AC5C4")); title.Children.Add(Text("MacDock  WPF", 32)); title.Children.Add(Text("자주 쓰는 동작을, 손이 닿는 곳에.", 13, "#9CABBF")); header.Children.Add(title);
        var save = Button("변경 저장", Save); save.Background = DockWindow.Brush("#98D2CA"); save.Foreground = DockWindow.Brush("#14292B"); save.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(save,1); header.Children.Add(save);
        DockPanel.SetDock(header,Dock.Top); layout.Children.Add(header);
        var footer = new Border { Background = DockWindow.Brush("#17202E"), Child = status }; DockPanel.SetDock(footer,Dock.Bottom); layout.Children.Add(footer);
        body.Margin = new(34,0,34,30); layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Render();
    }
    static TextBlock Text(string text, double size = 13, string color = "#E8EDF6") => new() { Text = text, FontSize = size, Foreground = DockWindow.Brush(color), Margin = new(0,0,0,8), TextWrapping = TextWrapping.Wrap };
    static Button Button(string text, Action handler) { var button = new Button { Content = text }; button.Click += (_, _) => handler(); return button; }
    TextBox Input(string text, string hint, Action<string> change)
    {
        var input = new TextBox { Text = text, ToolTip = hint }; System.Windows.Automation.AutomationProperties.SetName(input, hint); input.TextChanged += (_, _) => change(input.Text); return input;
    }
    static ComboBox Choices(IEnumerable<(string Key, string Label)> options, string current, Action<string> change)
    {
        var combo = new ComboBox(); foreach (var (key,label) in options) combo.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem item) change((string)item.Tag); }; return combo;
    }
    void AddSlider(string title, double value, double min, double max, double tick, Action<double> change)
    {
        var row = new DockPanel(); var label = Text(title + "  " + value.ToString("0.#")); label.Width = 160; DockPanel.SetDock(label,Dock.Left); row.Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = tick, IsSnapToTickEnabled = true }; slider.ValueChanged += (_, _) => { change(slider.Value); label.Text = title + "  " + slider.Value.ToString("0.#"); }; row.Children.Add(slider); body.Children.Add(row);
    }
    void Render()
    {
        validators.Clear(); body.Children.Clear(); body.Children.Add(Text("독 모양과 위치",18));
        var monitors = new List<(string,string)> { ("", "기본 모니터") };
        monitors.AddRange(Forms.Screen.AllScreens.Select(s => (s.DeviceName, s.DeviceName + " · " + s.Bounds.Width + " × " + s.Bounds.Height)));
        if (!string.IsNullOrEmpty(draft.Monitor) && !monitors.Any(m => m.Item1 == draft.Monitor)) monitors.Add((draft.Monitor, draft.Monitor + " · 연결되지 않음 (기본 모니터 사용)"));
        body.Children.Add(Choices(monitors, draft.Monitor, value => draft.Monitor = value));
        body.Children.Add(Text("독 위치",13));
        body.Children.Add(Choices(new[] { ("bottom","하단"), ("top","상단"), ("left","왼쪽 (세로)"), ("right","오른쪽 (세로)") }, draft.Position, value => draft.Position = value));
        AddSlider("버튼 크기 (px)",draft.Size,32,80,1,value => draft.Size = value);
        AddSlider("확대 배율",draft.Zoom,1,2.2,.1,value => draft.Zoom = value);
        AddSlider("버튼 간격 (px)",draft.Gap,4,20,1,value => draft.Gap = value);
        AddSlider("가장자리 여백 (px)",draft.Bottom,0,100,1,value => draft.Bottom = value);
        var magnify = new CheckBox { Content = "마우스를 가까이 가져가면 확대", IsChecked = draft.Magnify }; magnify.Checked += (_,_) => draft.Magnify = true; magnify.Unchecked += (_,_) => draft.Magnify = false; body.Children.Add(magnify);
        body.Children.Add(Text("Windows 작업 표시줄을 피해서 배치합니다. 버튼이 많으면 독 위에서 휠 또는 방향 버튼으로 페이지를 전환합니다.",12,"#94A3B8"));
        var startup = new CheckBox { Content = "Windows 시작 시 자동 실행 (현재 사용자 로그인 시)", IsChecked = autoStart };
        startup.Checked += (_,_) => autoStart = true; startup.Unchecked += (_,_) => autoStart = false; body.Children.Add(startup);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0,14,0,14) };
        toolbar.Children.Add(Button("+ 버튼 추가", () => { if (draft.Buttons.Count >= 100) { status.Text = "버튼은 100개까지 가능합니다."; return; } draft.Buttons.Add(new() { Target = "https://www.google.com" }); Render(); }));
        toolbar.Children.Add(Button("설정 가져오기", Import));
        toolbar.Children.Add(Button("설정 내보내기", Export));
        body.Children.Add(toolbar);
        body.Children.Add(Text("빠른 추가",16));
        var quick = new WrapPanel { Margin = new(0,0,0,12) };
        quick.Children.Add(Button("📁 지정 폴더", () => {
            var dialog = new OpenFolderDialog { Title = "독 버튼으로 열 폴더 선택" };
            if (dialog.ShowDialog(this) == true) AddItem(new DockItem { Type = "folder", Target = dialog.FolderName, Label = DockItem.FolderName(dialog.FolderName), Icon = "▰", Color = "#D19A32" });
        }));
        for (int i = 0; i < Presets.Items.Length; i++) { int preset = i; var button = Button(Presets.Items[i].Name, () => AddItem(Presets.Create(preset))); button.Margin = new(0,0,6,6); quick.Children.Add(button); }
        quick.Children.Add(Button("직접 매크로", () => AddItem(new DockItem { Type = "macro", Label = "새 매크로", Target = "Ctrl+C\nwait 300\nCtrl+V", Icon = "⌨", Color = "#59687D" })));
        body.Children.Add(quick);
        body.Children.Add(Text("키 매크로: Ctrl+C, wait 300, Ctrl+V, text 안녕하세요를 한 줄씩 입력합니다. 독 클릭 전 사용 중이던 앱으로 입력됩니다.",12,"#94A3B8"));
        for (int i = 0; i < draft.Buttons.Count; i++) AddCard(draft.Buttons[i], i);
        body.Children.Add(Text("Electron 버전과 설정을 따로 저장합니다. 이 버전은 확장 기능을 사용하지 않습니다.",12,"#94A3B8"));
    }
    void AddItem(DockItem item)
    {
        if (draft.Buttons.Count >= 100) { status.Text = "버튼은 100개까지 가능합니다."; return; }
        draft.Buttons.Add(item); Render(); status.Text = item.Label + " 버튼을 추가했습니다. 변경 저장을 누르면 독에 적용됩니다.";
    }
    void AddCard(DockItem item, int index)
    {
        var content = new StackPanel(); var card = new Border { Child = content, Background = DockWindow.Brush("#181F2A"), BorderBrush = DockWindow.Brush("#344154"), BorderThickness = new(1), CornerRadius = new(12), Padding = new(14), Margin = new(0,0,0,12) }; body.Children.Add(card);
        var top = new Grid(); top.ColumnDefinitions.Add(new() { Width = new GridLength(1,GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = new GridLength(190) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var nameEditor = Input(item.Label,"버튼 이름 (폴더 버튼 하단에 표시)",value => item.Label = value);
        top.Children.Add(nameEditor);
        var type = Choices(Types,item.Type,value => { item.Type = value; item.Target = value switch { "clock" => "24h", "macro" => "Ctrl+C", "media" => "PLAYPAUSE", "volume" => "up", "url" => "https://www.google.com", _ => "" }; Render(); }); Grid.SetColumn(type,1); top.Children.Add(type);
        var order = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var up = Button("↑", () => { (draft.Buttons[index],draft.Buttons[index-1]) = (draft.Buttons[index-1],draft.Buttons[index]); Render(); }); up.IsEnabled = index > 0; order.Children.Add(up);
        var down = Button("↓", () => { (draft.Buttons[index],draft.Buttons[index+1]) = (draft.Buttons[index+1],draft.Buttons[index]); Render(); }); down.IsEnabled = index < draft.Buttons.Count-1; order.Children.Add(down);
        order.Children.Add(Button("×", () => { draft.Buttons.RemoveAt(index); Render(); })); Grid.SetColumn(order,2); top.Children.Add(order); content.Children.Add(top);
        if (item.Type == "volume") {
            content.Children.Add(Choices(new[] { ("up","볼륨 올리기"),("down","볼륨 내리기"),("mute","음소거 켜기 / 끄기") },item.Target,value => { item.Target = value; Render(); }));
            if (item.Target != "mute") {
                var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(Text("한 번 누를 때 바꿀 양 (%p) "));
                var step = Input(item.VolumeStep.ToString(),"볼륨 조절 폭 1~100%p",value => { if (int.TryParse(value,out int n)) item.VolumeStep = n; }); step.Width = 75; validators[step] = () => int.TryParse(step.Text,out int n) && n >= 1 && n <= 100; row.Children.Add(step); content.Children.Add(row);
                content.Children.Add(Text("예: 5%p로 설정하면 40% → 45%가 됩니다. Windows 기본 출력 장치를 제어합니다.",12,"#94A3B8"));
            }
        } else if (item.Type == "media") content.Children.Add(Choices(new[] { ("PLAYPAUSE","재생 / 일시정지"),("NEXT","다음 곡"),("PREVIOUS","이전 곡"),("STOP","정지"),("MUTE","음소거"),("VOLUMEUP","볼륨 증가 (Windows 기본 폭)"),("VOLUMEDOWN","볼륨 감소 (Windows 기본 폭)") },item.Target,value => item.Target = value));
        else if (item.Type == "usb-eject") content.Children.Add(Text("USB 메모리·외장 HDD/SSD를 모두 안전하게 제거합니다. 사용 중인 장치는 강제 제거하지 않으며, 완료·실패 결과를 표시합니다. 키보드·마우스·USB 허브는 제외됩니다.",12,"#98D2CA"));
        else if (item.Type == "clock") content.Children.Add(Choices(new[] { ("24h","24시간"),("12h","12시간") },item.Target,value => item.Target = value));
        else if (!new[] { "cpu","memory","gpu","gpu-temperature","network-up","network-down" }.Contains(item.Type)) {
            var target = Input(item.Target,"URL / 경로 / 매크로",value => {
                if (item.Type == "folder" || item.Type == "path") { item.SetFolderPath(value); nameEditor.Text = item.Label; }
                else item.Target = value;
            });
            if (item.Type == "macro") { target.AcceptsReturn = true; target.Height = 95; target.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            content.Children.Add(target);
            if (item.Type == "macro") {
                content.Children.Add(Text("한 줄 = 한 단계. 예: Ctrl+S (저장), wait 300 (0.3초 대기), text 안녕하세요 (문자 입력), Enter (엔터).\n실행할 앱을 먼저 클릭한 뒤 독의 매크로 버튼을 누르세요. 전체 단계는 위에서 아래로 실행됩니다.",12,"#98D2CA"));
                var examples = new WrapPanel();
                examples.Children.Add(Button("● 키 녹화", () => {
                    var recorder = new MacroRecorder { Owner = this };
                    if (recorder.ShowDialog() == true) { target.Text = recorder.Recorded; status.Text = "녹화한 키를 입력했습니다. 변경 저장을 누르면 적용됩니다."; }
                }));
                foreach (var (label, code) in new[] { ("저장 예제", "Ctrl+S"), ("복사 → 붙여넣기", "Ctrl+C\nwait 300\nCtrl+V"), ("문자 입력 예제", "text 안녕하세요\nEnter") }) { string sample = code; examples.Children.Add(Button(label, () => target.Text = sample)); }
                content.Children.Add(examples);
            }
            if (item.Type == "program" || item.Type == "path" || item.Type == "folder") {
                var browse = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0,0,0,8) };
                if (item.Type != "folder") browse.Children.Add(Button("파일 선택",() => { var dialog = new OpenFileDialog(); if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName; }));
                if (item.Type == "path" || item.Type == "folder") browse.Children.Add(Button("폴더 선택",() => { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) target.Text = dialog.FolderName; })); content.Children.Add(browse);
                if (item.Type == "path" || item.Type == "folder") {
                    content.Children.Add(Text("버튼 하단 이름: 위의 버튼 이름을 수정하세요. 처음 폴더를 지정하면 마지막 폴더명이 자동 입력됩니다.",12,"#98D2CA"));
                    content.Children.Add(Button("폴더명으로 이름 초기화", () => { item.Label = DockItem.FolderName(item.Target); nameEditor.Text = item.Label; }));
                }
            }
        }
        var look = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Input(item.Icon,"아이콘 문자",value => item.Icon = value); icon.Width = 65; look.Children.Add(icon);
        var color = Input(item.Color,"색상 #RRGGBB",value => item.Color = value); color.Width = 95; look.Children.Add(color);
        look.Children.Add(Button("색상",() => { using var dialog = new Forms.ColorDialog(); if (dialog.ShowDialog() == Forms.DialogResult.OK) color.Text = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2"); }));
        look.Children.Add(Button("내장 아이콘",() => { var picker = new IconPicker(item.Color) { Owner = this }; if (picker.ShowDialog() == true && picker.Selected is int selected) { item.Image = IconLibrary.DataUrl(selected); Render(); status.Text = IconLibrary.Items[selected].Name + " 아이콘을 선택했습니다. 저장하면 적용됩니다."; } }));
        look.Children.Add(Button("이미지",() => {
            var dialog = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.gif;*.bmp" };
            if (dialog.ShowDialog(this) != true) return;
            try { var bytes = File.ReadAllBytes(dialog.FileName); if (bytes.Length > 2_900_000) throw new Exception("이미지는 2.9MB 이하로 선택하세요."); string data = "data:image/" + Path.GetExtension(dialog.FileName)[1..] + ";base64," + Convert.ToBase64String(bytes); ImageFrames.Load(data); item.Image = data; status.Text = "이미지를 선택했습니다. 저장하면 적용됩니다."; Render(); } catch (Exception error) { status.Text = error.Message; }
        }));
        if (item.Image.Length > 0) look.Children.Add(Button("이미지 제거",() => { item.Image = ""; Render(); })); content.Children.Add(look);
        var palette = new WrapPanel { Margin = new Thickness(0,8,0,0) };
        foreach (string hex in IconLibrary.Colors) { string selectedColor = hex; var swatch = new Button { Width = 25, Height = 25, Padding = new Thickness(0), Margin = new Thickness(0,0,5,5), Background = DockWindow.Brush(hex), ToolTip = hex }; swatch.Click += (_,_) => color.Text = selectedColor; palette.Children.Add(swatch); }
        content.Children.Add(palette);
    }
    bool ValidFields()
    {
        foreach (var (box,check) in validators) if (!check()) { box.Focus(); status.Text = "볼륨 조절 폭은 1~100의 정수로 입력하세요."; return false; }
        return true;
    }
    void Save()
    {
        if (!ValidFields()) return;
        try { draft.Validate(); Store.Save(draft); if (autoStart || AutoStart.Enabled()) AutoStart.Set(autoStart); dock.Apply(draft.Clone()); status.Text = "저장했습니다. 독에 적용되었습니다."; }
        catch (Exception error) { status.Text = error.Message; }
    }
    void Import()
    {
        var dialog = new OpenFileDialog { Filter = "MacDock 설정|*.json", Title = "Electron 또는 WPF 설정 가져오기" };
        if (dialog.ShowDialog(this) != true) return;
        try { var result = Store.Import(dialog.FileName); draft = result.Config; Render(); status.Text = $"가져왔습니다. 저장하면 적용됩니다. 제외된 확장 버튼: {result.Skipped}"; }
        catch (Exception error) { status.Text = "가져오기 실패: " + error.Message; }
    }
    void Export()
    {
        if (!ValidFields()) return;
        var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "MacDock-WPF-settings.json" };
        try { draft.Validate(); if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName,System.Text.Json.JsonSerializer.Serialize(draft,Store.Json)); status.Text = "내보냈습니다."; } }
        catch (Exception error) { status.Text = error.Message; }
    }
}
