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

public sealed partial class SettingsWindow : Window
{
    readonly TabControl tabs = new();
    readonly FileShareSettings share = new();
    bool selectingShare;
    internal async System.Threading.Tasks.Task ShowFileShare()
    {
        selectingShare = true;
        try { tabs.SelectedIndex = 1; } finally { selectingShare = false; }
        await share.Execute("start");
    }
    readonly DockWindow dock;
    DockConfig draft;
    readonly StackPanel body = new();
    readonly ScrollViewer dockScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    enum DockScreen { Preview, Add, Item }
    DockScreen screen;
    DockItem? selectedItem;
    int previewPage;
    readonly StackPanel appearance = new();
    readonly Viewbox preview = new() { Stretch = Stretch.Uniform };
    readonly ColumnDefinition inspector = new();
    ComboBox monitorChoice = null!;
    readonly TextBlock status = new() { Margin = new(30,10,30,10) };
    readonly Dictionary<TextBox, Func<bool>> validators = new();
    bool autoStart = AutoStart.Enabled();
    static readonly (string Key, string Label)[] Types = { ("url", "웹사이트"), ("program", "프로그램"), ("folder", "지정 폴더 열기"), ("path", "파일 / 폴더"), ("clock", "실시간 시계"), ("cpu", "CPU 사용량"), ("memory", "메모리 사용량"), ("gpu", "GPU 사용량 (NVIDIA)"), ("gpu-temperature", "GPU 온도 (NVIDIA)"), ("network-up", "이더넷 업로드"), ("network-down", "이더넷 다운로드"), ("macro", "키 매크로 / 텍스트"), ("media", "미디어 제어"), ("volume", "시스템 볼륨"), ("usb-eject", "USB 모두 안전하게 제거"), ("file-share", "파일 공유 서버"), ("shutdown-timer", "Windows 자동 종료"), ("sound","사운드 재생"), ("countdown","타이머") };
    public SettingsWindow(DockWindow dock)
    {
        this.dock = dock; previewPage = dock.CurrentPage; draft = dock.Config.Clone(); Title = "MacDock WPF 설정"; Icon = AppIcons.WindowIcon(); Width = 1120; Height = 800; MinWidth = 740; MinHeight = 550;
        Background = DockWindow.Brush("#11151D"); Foreground = DockWindow.Brush("#E8EDF6"); FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
        var layout = new DockPanel { Background = Background }; Content = layout;
        var header = new Grid { Margin = new(34,26,34,20) }; header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Text("MacDock 설정", 32));
        var save = Button("변경 저장", Save); save.Background = DockWindow.Brush("#98D2CA"); save.Foreground = DockWindow.Brush("#14292B"); save.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(save,1); header.Children.Add(save);
        DockPanel.SetDock(header,Dock.Top); layout.Children.Add(header);
        var footer = new Border { Background = DockWindow.Brush("#17202E"), Child = status }; DockPanel.SetDock(footer,Dock.Bottom); layout.Children.Add(footer);
        tabs.Background = Background; tabs.Foreground = Foreground;
        var workspace = new DockPanel { Margin = new(16,8,16,12) };
        DockPanel.SetDock(appearance,Dock.Top); workspace.Children.Add(appearance);
        BuildAppearance();
        var panes = new Grid(); panes.ColumnDefinitions.Add(new()); panes.ColumnDefinitions.Add(inspector);
        var left = new DockPanel { Margin = new(0,0,12,0) };
        var toolbar = new WrapPanel();
        toolbar.Children.Add(Button("+ 버튼 추가", () => { if (ValidFields()) Navigate(DockScreen.Add); }));
        toolbar.Children.Add(Button("설정 가져오기", Import)); toolbar.Children.Add(Button("설정 내보내기", Export));
        DockPanel.SetDock(toolbar,Dock.Bottom); left.Children.Add(toolbar);
        var title = Text("독 미리보기",18); DockPanel.SetDock(title,Dock.Top); left.Children.Add(title);
        var hint = Text("버튼을 클릭하면 편집하고, 다른 버튼 위로 끌면 순서를 바꿉니다. 독을 옮기려면 여백을 끄세요.",13,"#A8B8C9");
        DockPanel.SetDock(hint,Dock.Top); left.Children.Add(hint);
        left.Children.Add(preview); panes.Children.Add(left);
        dockScroll.Content = body; Grid.SetColumn(dockScroll,1); panes.Children.Add(dockScroll);
        workspace.Children.Add(panes);
        tabs.Items.Add(new TabItem { Header = "독 설정", Content = workspace });
        tabs.Items.Add(new TabItem { Header = "파일 공유 서버", Content = new ScrollViewer { Content = share, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.SelectionChanged += async (_, e) => {
            if (e.Source != tabs) return;
            save.Visibility = tabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Hidden;
            if (tabs.SelectedIndex == 1 && !selectingShare) await share.LoadSettings();
        };
        dock.Sampled += RefreshPreviewValues;
        Closed += (_,_) => { dock.Sampled -= RefreshPreviewValues; Soundboard.Stop("preview"); };
        layout.Children.Add(tabs); Render();
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
    void Render()
    {
        validators.Clear(); body.Children.Clear(); ShowDockPreview();
        bool open = screen != DockScreen.Preview;
        inspector.Width = open ? new GridLength(.9,GridUnitType.Star) : new GridLength(0);
        dockScroll.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!open) return;
        var header = new DockPanel();
        var close = Button("닫기", () => { if (ValidFields()) Navigate(DockScreen.Preview); });
        DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
        header.Children.Add(Text(screen == DockScreen.Add ? "버튼 추가" : "버튼 세부 설정",18));
        body.Children.Add(header);
        if (screen == DockScreen.Add) ShowAddButtons();
        else if (selectedItem != null) AddCard(selectedItem, draft.Buttons.IndexOf(selectedItem));
    }
    void BuildAppearance()
    {
        appearance.Children.Clear();
        appearance.Children.Add(Text("독 모양과 위치",18));
        var placement = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        void Field(string title, Control control) {
            var field = new StackPanel(); field.Children.Add(Text(title)); field.Children.Add(control); placement.Children.Add(field);
        }
        var monitors = new List<(string,string)> { ("", "기본 모니터") };
        monitors.AddRange(Forms.Screen.AllScreens.Select(s => (s.DeviceName, s.DeviceName + " · " + s.Bounds.Width + " × " + s.Bounds.Height)));
        if (!string.IsNullOrEmpty(draft.Monitor) && !monitors.Any(m => m.Item1 == draft.Monitor)) monitors.Add((draft.Monitor, draft.Monitor + " · 연결되지 않음 (기본 모니터 사용)"));
        monitorChoice = Choices(monitors,draft.Monitor,value => { draft.Monitor = value; ResetPlacement(); ShowDockPreview(); });
        Field("모니터",monitorChoice);
        Field("독 위치",Choices(new[] { ("bottom","하단"), ("top","상단"), ("left","왼쪽 (세로)"), ("right","오른쪽 (세로)") }, draft.Position, value => { draft.Position = value; ResetPlacement(); ShowDockPreview(); }));
        Field("줄 수", Choices(Enumerable.Range(1,6).Select(n => (n.ToString(), n + "줄")), draft.Rows.ToString(), value => { draft.Rows = int.Parse(value); Render(); }));
        var reset = Button("가장자리로 복귀", () => { ResetPlacement(); ShowDockPreview(); });
        reset.VerticalAlignment = VerticalAlignment.Center; placement.Children.Add(reset); appearance.Children.Add(placement);
        var sliders = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        appearance.Children.Add(sliders);
        void AddSlider(string title, double value, double min, double max, double tick, Action<double> change) {
            var field = new StackPanel();
            var label = Text(title + "  " + value.ToString("0.#")); field.Children.Add(label);
            var slider = new Slider { Minimum=min, Maximum=max, Value=value, TickFrequency=tick, IsSnapToTickEnabled=true, Margin=new(0,0,12,0) };
            slider.ValueChanged += (_,_) => { change(slider.Value); label.Text = title + "  " + slider.Value.ToString("0.#"); ShowDockPreview(); };
            field.Children.Add(slider); sliders.Children.Add(field);
        }
        AddSlider("버튼 크기 (px)",draft.Size,32,80,1,value => draft.Size = value);
        AddSlider("확대 배율",draft.Zoom,1,2.2,.1,value => draft.Zoom = value);
        AddSlider("버튼 간격 (px)",draft.Gap,4,20,1,value => draft.Gap = value);
        AddSlider("가장자리 여백 (px)",draft.Bottom,0,100,1,value => { draft.Bottom = value; ResetPlacement(); });
        var options = new WrapPanel(); appearance.Children.Add(options);
        var magnify = new CheckBox { Content = "마우스를 가까이 가져가면 확대", IsChecked = draft.Magnify }; magnify.Checked += (_,_) => draft.Magnify = true; magnify.Unchecked += (_,_) => draft.Magnify = false; options.Children.Add(magnify);
        var startup = new CheckBox { Content = "Windows 시작 시 자동 실행 (현재 사용자 로그인 시)", IsChecked = autoStart };
        startup.Checked += (_,_) => autoStart = true; startup.Unchecked += (_,_) => autoStart = false; options.Children.Add(startup);
    }
    void Navigate(DockScreen next, DockItem? item = null)
    {
        Soundboard.Stop("preview");
        screen = next; selectedItem = item;
        Render();
        dockScroll.ScrollToTop();
    }
    void ShowAddButtons()
    {
        body.Children.Add(Button("+ 직접 설정할 버튼", () => AddItem(new DockItem { Target = "https://www.google.com" })));
        body.Children.Add(Text("빠른 추가",16));
        var quick = new WrapPanel { Margin = new(0,0,0,12) };
        quick.Children.Add(Button("📁 지정 폴더", () => {
            var dialog = new OpenFolderDialog { Title = "독 버튼으로 열 폴더 선택" };
            if (dialog.ShowDialog(this) == true) AddItem(new DockItem { Type = "folder", Target = dialog.FolderName, Label = DockItem.FolderName(dialog.FolderName), Icon = "▰", Color = "#D19A32" });
        }));
        for (int i = 0; i < Presets.Items.Length; i++) { int preset = i; var button = Button(Presets.Items[i].Name, () => AddItem(Presets.Create(preset))); button.Margin = new(0,0,6,6); quick.Children.Add(button); }
        quick.Children.Add(Button("직접 매크로", () => AddItem(new DockItem { Type = "macro", Label = "새 매크로", Target = "Ctrl+C\nwait 300\nCtrl+V", Icon = "⌨", Color = "#59687D" })));
        body.Children.Add(quick);


    }
    internal void SyncDraggedPosition(DockConfig config)
    {
        draft.FreeX = config.FreeX; draft.FreeY = config.FreeY; draft.Monitor = config.Monitor;
        BuildAppearance(); ShowDockPreview();
    }

    internal void SyncDraggedButton(string source, string target)
    {
        DockWindow.MoveButton(draft,source,target);
        Render();
    }

    void ResetPlacement() { draft.FreeX = null; draft.FreeY = null; }

    void AddItem(DockItem item, int? index = null)
    {
        if (draft.Buttons.Count >= 100) { status.Text = "버튼은 100개까지 가능합니다."; return; }
        draft.Buttons.Insert(index ?? draft.Buttons.Count, item); Navigate(DockScreen.Item, item); status.Text = item.Label + " 버튼을 추가했습니다. 변경 저장을 누르면 독에 적용됩니다.";
    }
    void AddCard(DockItem item, int index)
    {
        var content = new StackPanel(); var card = new Border { Child = content, Background = DockWindow.Brush("#181F2A"), BorderBrush = DockWindow.Brush("#344154"), BorderThickness = new(1), CornerRadius = new(12), Padding = new(14), Margin = new(0,0,0,12) }; body.Children.Add(card);
        var top = new StackPanel();
        var nameEditor = Input(item.Label,"버튼 이름 (폴더 버튼 하단에 표시)",value => { item.Label = value; ShowDockPreview(); });
        top.Children.Add(nameEditor);
        var type = Choices(Types,item.Type,value => { item.Type = value; if (value == "sound") item.Sound = Soundboard.DefaultSound; else if (value == "countdown") item.Sound = Soundboard.DefaultAlarm; item.Target = value switch { "clock" => "24h", "macro" => "Ctrl+C", "media" => "PLAYPAUSE", "volume" => "up", "url" => "https://www.google.com", _ => "" }; Render(); }); top.Children.Add(type);
        var order = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var up = Button("↑", () => { (draft.Buttons[index],draft.Buttons[index-1]) = (draft.Buttons[index-1],draft.Buttons[index]); Render(); }); up.IsEnabled = index > 0; order.Children.Add(up);
        var down = Button("↓", () => { (draft.Buttons[index],draft.Buttons[index+1]) = (draft.Buttons[index+1],draft.Buttons[index]); Render(); }); down.IsEnabled = index < draft.Buttons.Count-1; order.Children.Add(down);
        order.Children.Add(Button("×", () => { draft.Buttons.RemoveAt(index); Navigate(DockScreen.Preview); })); top.Children.Add(order); content.Children.Add(top);
        var sizes = new List<(string,string)> { ("1x1","기본 · 1칸"), ("2x1","×2 · 가로 2칸") };
        if (draft.Rows >= 2 || item.BlockSize == "2x2") sizes.Add(("2x2", draft.Rows >= 2 ? "2×2 · 4칸" : "2×2 · 현재 1줄에서는 가로 2칸"));
        content.Children.Add(Text("블록 크기"));
        content.Children.Add(Choices(sizes,item.BlockSize,value => { item.BlockSize = value; ShowDockPreview(); }));
        if (item.Type is "sound" or "countdown") AddAudioSettings(content,item);
        else if (item.Type == "volume") {
            content.Children.Add(Choices(new[] { ("up","볼륨 올리기"),("down","볼륨 내리기"),("mute","음소거 켜기 / 끄기") },item.Target,value => { item.Target = value; Render(); }));
            if (item.Target != "mute") {
                var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(Text("한 번 누를 때 바꿀 양 (%p) "));
                var step = Input(item.VolumeStep.ToString(),"볼륨 조절 폭 1~100%p",value => { if (int.TryParse(value,out int n)) item.VolumeStep = n; }); step.Width = 75; validators[step] = () => int.TryParse(step.Text,out int n) && n >= 1 && n <= 100; row.Children.Add(step); content.Children.Add(row);
                content.Children.Add(Text("Windows 기본 출력 장치의 볼륨을 조절합니다.",12,"#94A3B8"));
            }
        } else if (item.Type == "media") content.Children.Add(Choices(new[] { ("PLAYPAUSE","재생 / 일시정지"),("NEXT","다음 곡"),("PREVIOUS","이전 곡"),("STOP","정지"),("MUTE","음소거"),("VOLUMEUP","볼륨 증가 (Windows 기본 폭)"),("VOLUMEDOWN","볼륨 감소 (Windows 기본 폭)") },item.Target,value => item.Target = value));
        else if (item.Type == "usb-eject") content.Children.Add(Text("USB 메모리·외장 HDD/SSD를 모두 안전하게 제거합니다. 사용 중인 장치는 강제 제거하지 않으며, 완료·실패 결과를 표시합니다. 키보드·마우스·USB 허브는 제외됩니다.",12,"#98D2CA"));
        else if (item.Type == "shutdown-timer") content.Children.Add(Text("종료까지 남은 시간과 분을 입력해 예약합니다. 예약 중에 버튼을 다시 누르면 즉시 취소됩니다.",12,"#98D2CA"));
        else if (item.Type == "file-share") content.Children.Add(Text("같은 Wi-Fi의 기기와 파일을 주고받습니다. MacDock 설정의 파일 공유 서버 탭에서 공유 폴더와 비밀번호를 관리합니다.",12,"#98D2CA"));
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
                var browse = new WrapPanel { Margin = new(0,0,0,8) };
                if (item.Type != "folder") browse.Children.Add(Button("파일 선택",() => { var dialog = new OpenFileDialog(); if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName; }));
                if (item.Type == "path" || item.Type == "folder") browse.Children.Add(Button("폴더 선택",() => { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) target.Text = dialog.FolderName; })); content.Children.Add(browse);
                if (item.Type == "path" || item.Type == "folder") {
                    content.Children.Add(Text("버튼 하단 이름: 위의 버튼 이름을 수정하세요. 처음 폴더를 지정하면 마지막 폴더명이 자동 입력됩니다.",12,"#98D2CA"));
                    content.Children.Add(Button("폴더명으로 이름 초기화", () => { item.Label = DockItem.FolderName(item.Target); nameEditor.Text = item.Label; }));
                }
            }
        }
        var look = new WrapPanel();
        var icon = Input(item.Icon,"아이콘 문자",value => { item.Icon = value; ShowDockPreview(); }); icon.Width = 65; look.Children.Add(icon);
        var color = Input(item.Color,"색상 #RRGGBB",value => { item.Color = value; ShowDockPreview(); }); color.Width = 95; look.Children.Add(color);
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
        var duplicate = Button("복제하기", () => AddItem(item.Duplicate(), index + 1));
        duplicate.HorizontalAlignment = HorizontalAlignment.Left;
        duplicate.Margin = new(0,4,0,0);
        content.Children.Add(duplicate);
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
        try { var result = Store.Import(dialog.FileName); draft = result.Config; BuildAppearance(); previewPage = 0; Navigate(DockScreen.Preview); status.Text = $"가져왔습니다. 저장하면 적용됩니다. 제외된 확장 버튼: {result.Skipped}"; }
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
