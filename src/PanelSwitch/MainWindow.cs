using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace PanelSwitch;
internal sealed class MainWindow:Window {
    readonly Settings settings=Settings.Read(); readonly Controller controller;
    readonly Forms.NotifyIcon tray=new();readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly TextBlock state=new(){FontSize=28,FontWeight=FontWeights.SemiBold};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12),Foreground=Brushes.DimGray};
    readonly TextBox diagnostic=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=130};
    readonly ComboBox toggleModifiers=new(),toggleKey=new(),emergencyModifiers=new(),emergencyKey=new();
    readonly TextBlock driverFeedback=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Brush("#526779")};
    readonly Button driverButton;
    readonly TextBlock hotkeyFeedback=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    readonly TextBlock currentShortcut=new(){FontSize=15,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    readonly TextBlock normalHotkeyHint=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Brush("#526779")};
    readonly TextBlock emergencyHint=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)};
    readonly TextBox environment=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,Height=190,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly CheckBox startup=new(){Content="登录后自动运行",Margin=new Thickness(0,8,0,0)};
    readonly CheckBox awake=new(){Content="关屏期间保持后台运行",Margin=new Thickness(0,8,0,0)};
    readonly CheckBox virtualDisplay=new(){Content="允许使用已安装的虚拟显示器维持单内屏关屏",Margin=new Thickness(0,8,0,0)};
    readonly CheckBox powerButton=new(){Content="启用实体电源键控制（需要管理员代理）",Margin=new Thickness(0,8,0,0),IsEnabled=false};
    readonly Border hero; readonly TextBlock stateBadge=new(){FontSize=12,FontWeight=FontWeights.SemiBold}; readonly Button toggle; Hotkeys? hotkeys;PowerButton? buttonInput;HwndSource? source;
    bool exiting,ticking;IntPtr powerNotification;DateTime lastButton;
    readonly bool smoke,background;readonly string? previewDirectory;PowerAgentClient? powerAgent;bool startingAgent;
    public MainWindow(bool smoke=false,bool background=false,string? previewDirectory=null) {
        this.smoke=smoke;this.background=background;this.previewDirectory=previewDirectory;controller=new(settings);
        Title="内屏开关";Width=760;Height=640;MinWidth=540;MinHeight=490;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;UseLayoutRounding=true;
        Background=Brush("#F1F5F8");Foreground=Brush("#172E40");FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/PanelSwitch;component/Theme.xaml",UriKind.Relative)});
        Icon=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.png"));
        var frame=new Grid{Background=Background};Content=frame;
        var shell=new DockPanel{Margin=new Thickness(22,18,22,16)};frame.Children.Add(shell);
        var header=new Grid{Margin=new Thickness(1,0,1,15)};
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        header.Children.Add(new Image{Source=Icon,Width=38,Height=38,Margin=new Thickness(0,0,10,0)});
        var heading=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(heading,1);header.Children.Add(heading);
        heading.Children.Add(new TextBlock{Text="内屏开关",FontSize=21,FontWeight=FontWeights.SemiBold});
        heading.Children.Add(new TextBlock{Text="屏幕控制台",FontSize=12,Foreground=Brush("#526779")});
        var version=Label("版本 "+BuildVersion,12,"#40566A");var versionBadge=new Border{Child=version,Background=Brush("#E7EDF3"),CornerRadius=new CornerRadius(8),Padding=new Thickness(9,5,9,5),VerticalAlignment=VerticalAlignment.Center};
        Grid.SetColumn(versionBadge,2);header.Children.Add(versionBadge);DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);

        var rescueText=new StackPanel{Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
        rescueText.Children.Add(new TextBlock{Text="应急入口",FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Brush("#74451F")});
        emergencyHint.FontSize=12;emergencyHint.Foreground=Brush("#74451F");rescueText.Children.Add(emergencyHint);
        var rescue=new Grid();rescue.ColumnDefinitions.Add(new ColumnDefinition());rescue.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});rescue.Children.Add(rescueText);
        var emergency=Button("紧急恢复内屏",controller.ForceRestore);emergency.Margin=new Thickness(0);emergency.VerticalAlignment=VerticalAlignment.Center;emergency.Background=Brush("#FFFBF5");emergency.Foreground=Brush("#74451F");emergency.BorderBrush=Brush("#DFC6A8");emergency.ToolTip="切到仅内屏；外接屏会暂时退出显示。";
        Grid.SetColumn(emergency,1);rescue.Children.Add(emergency);
        var rescueCard=Card(rescue,"#FFF8ED");rescueCard.BorderBrush=Brush("#ECD6B9");rescueCard.Margin=new Thickness(0,10,0,0);rescueCard.Padding=new Thickness(14,9,14,9);DockPanel.SetDock(rescueCard,Dock.Bottom);shell.Children.Add(rescueCard);

        var tabs=new TabControl{Padding=new Thickness(0),BorderThickness=new Thickness(0),Background=Brushes.Transparent};shell.Children.Add(tabs);
        var overview=new StackPanel();var settingsPanel=new StackPanel();var diagnosticsPanel=new StackPanel();
        tabs.Items.Add(Page("控制",overview));tabs.Items.Add(Page("设置",settingsPanel));tabs.Items.Add(Page("诊断",diagnosticsPanel));

        var heroContent=new StackPanel();
        var heroHeading=new Grid();heroHeading.ColumnDefinitions.Add(new ColumnDefinition());heroHeading.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var monitorGlyph=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 5,4 L 35,4 Q 38,4 38,7 L 38,24 Q 38,27 35,27 L 5,27 Q 2,27 2,24 L 2,7 Q 2,4 5,4 Z M 20,27 L 20,33 M 12,34 L 28,34"),Stroke=Brush("#287D73"),StrokeThickness=1.8,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Stretch=Stretch.Uniform,Width=34,Height=34};
        var monitorTile=new Border{Child=monitorGlyph,Width=58,Height=58,Margin=new Thickness(14,0,0,0),CornerRadius=new CornerRadius(15),Background=Brush("#FFFFFF"),BorderBrush=Brush("#D1E7E2"),BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Center};
        // 图形只标明设备，不表示开关状态；状态始终以文字为准。
        Grid.SetColumn(monitorTile,1);heroHeading.Children.Add(monitorTile);heroContent.Children.Add(heroHeading);
        var stateText=new StackPanel();heroHeading.Children.Add(stateText);
        stateText.Children.Add(new Border{Background=Brush("#FFFFFF"),CornerRadius=new CornerRadius(7),Padding=new Thickness(8,4,8,4),HorizontalAlignment=HorizontalAlignment.Left,Child=stateBadge});
        state.FontSize=32;state.Margin=new Thickness(0,6,0,0);stateText.Children.Add(state);
        status.Foreground=Brush("#3A5564");status.Margin=new Thickness(0,5,0,14);stateText.Children.Add(status);
        var actions=new WrapPanel();heroContent.Children.Add(actions);
        toggle=Button("关闭内屏",()=>controller.Toggle());toggle.Background=Brush("#0B6F66");toggle.Foreground=Brushes.White;toggle.BorderBrush=Brush("#0B6F66");toggle.MinWidth=132;actions.Children.Add(toggle);
        var test=Button("15 秒关屏测试",()=>controller.Toggle(15));test.ToolTip="关闭内屏，并在十五秒后自动恢复。";actions.Children.Add(test);
        hero=Card(heroContent,"#E6F4EF");overview.Children.Add(hero);

        var usage=new Grid();usage.ColumnDefinitions.Add(new ColumnDefinition());usage.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var usageText=new StackPanel{Margin=new Thickness(0,0,12,0)};usage.Children.Add(usageText);
        usageText.Children.Add(SectionTitle("开关快捷键","关闭窗口后，程序仍在托盘运行。"));
        currentShortcut.FontSize=13;currentShortcut.Foreground=Brush("#15594F");
        usageText.Children.Add(new Border{Child=currentShortcut,HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(6),Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,8,0,4),Background=Brush("#E9F4F1")});
        usageText.Children.Add(normalHotkeyHint);
        var shortcuts=Button("设置快捷键",()=>{tabs.SelectedIndex=1;Dispatcher.BeginInvoke(()=>toggleModifiers.Focus(),DispatcherPriority.Loaded);return Task.CompletedTask;});shortcuts.VerticalAlignment=VerticalAlignment.Center;shortcuts.Margin=new Thickness(0);Grid.SetColumn(shortcuts,1);usage.Children.Add(shortcuts);overview.Children.Add(Card(usage));
        UpdateShortcutLabel();

        var keySettings=new StackPanel();keySettings.Children.Add(SectionTitle("快捷键","分别选择修饰键和按键，再保存。两个操作必须使用不同组合。"));
        keySettings.Children.Add(HotkeyRow("开关内屏",toggleModifiers,toggleKey,settings.Modifiers,settings.VirtualKey));
        keySettings.Children.Add(HotkeyRow("紧急恢复",emergencyModifiers,emergencyKey,settings.EmergencyModifiers,settings.EmergencyVirtualKey));
        var keyActions=new WrapPanel{Margin=new Thickness(0,12,0,0)};
        var save=Button("保存快捷键",SaveHotkeys);save.Background=Brush("#0B6F66");save.Foreground=Brushes.White;save.BorderBrush=Brush("#0B6F66");keyActions.Children.Add(save);
        keyActions.Children.Add(Button("填入默认值",()=>{
            toggleModifiers.SelectedValue=3u;toggleKey.SelectedValue=0x79u;
            emergencyModifiers.SelectedValue=3u;emergencyKey.SelectedValue=0x7Au;
            hotkeyFeedback.Foreground=Brush("#526779");hotkeyFeedback.Text="已填入默认值，点保存后生效。";return Task.CompletedTask;
        }));
        var feedbackStyle=new Style(typeof(TextBlock));feedbackStyle.Triggers.Add(new Trigger{Property=TextBlock.TextProperty,Value="",Setters={new Setter(VisibilityProperty,Visibility.Collapsed)}});
        hotkeyFeedback.Style=feedbackStyle;keySettings.Children.Add(keyActions);keySettings.Children.Add(hotkeyFeedback);settingsPanel.Children.Add(Card(keySettings));
        var behavior=new StackPanel();behavior.Children.Add(SectionTitle("运行与设备","调整后台运行及内屏控制方式。"));
        startup.IsChecked=Install.StartupEnabled();settings.StartWithWindows=startup.IsChecked==true;awake.IsChecked=settings.KeepAwake;virtualDisplay.IsChecked=settings.AllowVirtualDisplay;
        startup.Click+=(_,_)=>Run(()=>{Install.SetStartup(startup.IsChecked==true);settings.StartWithWindows=startup.IsChecked==true;Save();});
        awake.Click+=(_,_)=>Run(()=>{settings.KeepAwake=awake.IsChecked==true;Save();if(controller.HasRecovery)Native.Awake(settings.KeepAwake);});
        virtualDisplay.Click+=(_,_)=>Run(()=>{settings.AllowVirtualDisplay=virtualDisplay.IsChecked==true;Save();controller.Refresh();});
        powerButton.IsEnabled=true;
        powerButton.IsChecked=settings.EnablePowerButton;
        powerButton.Click+=async(_,_)=>await Execute(async()=>{if(powerButton.IsChecked==true)await StartPowerAgent();else if(powerAgent!=null){await powerAgent.DisposeAsync();powerAgent=null;}settings.EnablePowerButton=powerButton.IsChecked==true;Save();});
        AddSetting(behavior,startup,"登录后自动运行","登录 Windows 后收进托盘，随时可用");
        AddSetting(behavior,awake,"关屏时保持电脑运行","后台任务继续运行，避免空闲自动睡眠");
        AddSetting(behavior,virtualDisplay,"单内屏模式","没有外接屏时，使用虚拟显示器维持关屏");
        AddSetting(behavior,powerButton,"实体电源键控制","短按切换屏幕，需要管理员代理；退出后还原原设置");
        settingsPanel.Children.Add(Card(behavior));
        var devicePanel=new StackPanel();var deviceTitle=new Grid();deviceTitle.ColumnDefinitions.Add(new ColumnDefinition());deviceTitle.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        deviceTitle.Children.Add(SectionTitle("设备状态","显示连接与控制能力"));var refresh=Button("重新检测",RefreshDiagnostic);refresh.Margin=new Thickness(0);Grid.SetColumn(refresh,1);deviceTitle.Children.Add(refresh);devicePanel.Children.Add(deviceTitle);
        diagnostic.MinHeight=0;diagnostic.Height=94;diagnostic.BorderThickness=new Thickness(0);diagnostic.Background=Brushes.Transparent;diagnostic.Padding=new Thickness(0);diagnostic.Margin=new Thickness(0,14,0,0);diagnostic.FontSize=13;devicePanel.Children.Add(diagnostic);
        diagnosticsPanel.Children.Add(Card(devicePanel));
        var environmentPanel=new StackPanel();var environmentTitle=new Grid();environmentTitle.ColumnDefinitions.Add(new ColumnDefinition());environmentTitle.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        environmentTitle.Children.Add(SectionTitle("显示环境检测","自动读取参数；不会关屏或修改系统配置"));
        var inspect=Button("重新读取",RefreshEnvironment);inspect.Margin=new Thickness(0);Grid.SetColumn(inspect,1);environmentTitle.Children.Add(inspect);environmentPanel.Children.Add(environmentTitle);
        environment.Margin=new Thickness(0,10,0,0);environmentPanel.Children.Add(environment);diagnosticsPanel.Children.Add(Card(environmentPanel));
        var advanced=new StackPanel();var tools=new WrapPanel{Margin=new Thickness(0,14,0,0)};advanced.Children.Add(tools);
        tools.Children.Add(Button("导出诊断",ExportDiagnostic));
        tools.Children.Add(Button("常规接口对照测试",async()=>{if(MessageBox.Show(this,"该对照测试会关闭所有屏幕，普通键鼠可能唤醒。十五秒后自动恢复。继续测试？","对照测试",MessageBoxButton.YesNo)==MessageBoxResult.Yes)await controller.Toggle(15,new BaselineBackend());}));
        driverButton=Button("检查／安装虚拟显示驱动",InstallVirtual);tools.Children.Add(driverButton);
        tools.Children.Add(Button("打开日志目录",()=>{Directory.CreateDirectory(Storage.Root);Process.Start(new ProcessStartInfo("explorer.exe",Storage.Root){UseShellExecute=true});return Task.CompletedTask;}));
        advanced.Children.Add(driverFeedback);
        advanced.Children.Add(new TextBlock{Text="诊断、驱动安装与对照测试仅在排查问题时使用。",Foreground=Brush("#728391"),FontSize=12,TextWrapping=TextWrapping.Wrap});
        var expander=new Expander{Header="高级工具",Content=advanced,FontSize=14};diagnosticsPanel.Children.Add(Card(expander));
        controller.Changed+=Update;
        using(var iconStream=typeof(MainWindow).Assembly.GetManifestResourceStream("PanelSwitch.Assets.app.ico")){tray.Icon=iconStream!=null?new System.Drawing.Icon(iconStream):System.Drawing.SystemIcons.Application;}tray.Text="内屏开关";
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("切换内屏",null,async(_,_)=>await Execute(()=>controller.Toggle()));menu.Items.Add("显示设置",null,(_,_)=>ShowSettings());menu.Items.Add("恢复内屏",null,async(_,_)=>await Execute(controller.Restore));menu.Items.Add("退出并恢复",null,async(_,_)=>await Exit());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ShowSettings();tray.Visible=true;
        menu.Items.Insert(3,new Forms.ToolStripMenuItem("紧急恢复（仅内屏）",null,async(_,_)=>await Execute(controller.ForceRestore)));
        SourceInitialized+=InitializedWindow;Loaded+=async(_,_)=>{if(background)Hide();if(!smoke)try{DisplayNative.HideIdleVirtual();}catch(Exception e){Storage.Log("停用空闲虚拟屏失败",e.Message);}await RefreshEnvironment();await RefreshDiagnostic();if(!smoke&&settings.EnablePowerButton)await Execute(StartPowerAgent);if(smoke){Storage.Save(Path.Combine(Storage.Root,"界面冒烟测试.json"),new{结果="通过",标题=Title,快捷键=DescribeKey(settings.Modifiers,settings.VirtualKey),状态=controller.State.ToString(),版本=BuildVersion,紧急键已注册=hotkeys?.EmergencyId!=null,普通键已注册=hotkeys?.ToggleId!=null,紧急快捷键=DescribeKey(settings.EmergencyModifiers,settings.EmergencyVirtualKey),页面数=tabs.Items.Count,时间=DateTimeOffset.Now});if(previewDirectory!=null)await SaveUiPreview(tabs,previewDirectory);await Exit();}};
        timer.Tick+=async(_,_)=>{if(ticking)return;ticking=true;try{if(powerAgent!=null)await powerAgent.Pulse();await controller.Tick();}catch(Exception e){Storage.Log("恢复监视异常",e.ToString());}finally{ticking=false;}};timer.Start();
        Closing+=(_,e)=>{if(!exiting){e.Cancel=true;Hide();}};
        Closed+=(_,_)=>{timer.Stop();hotkeys?.Dispose();buttonInput?.Dispose();if(powerNotification!=IntPtr.Zero)Native.UnregisterPowerSettingNotification(powerNotification);source?.RemoveHook(Hook);tray.Icon?.Dispose();tray.Dispose();};
    }
    internal static string BuildVersion=>
        (typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion
            ??typeof(MainWindow).Assembly.GetName().Version?.ToString()??"未知").Split('+')[0];
    static TextBlock Label(string text,double size,string color)=>new(){Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap};
    static TabItem Page(string title,StackPanel page){
        page.Margin=new Thickness(0,12,0,0);
        return new TabItem{Header=title,Content=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
    }
    sealed record KeyOption(uint Value,string Text);
    static Grid HotkeyRow(string title,ComboBox mods,ComboBox key,uint currentMods,uint currentKey){
        var row=new Grid{Margin=new Thickness(0,10,0,0)};
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(92)});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        row.Children.Add(new TextBlock{Text=title,VerticalAlignment=VerticalAlignment.Center});
        var modifierList=Enumerable.Range(1,15).Select(x=>new KeyOption((uint)x,ModifierLabel((uint)x).TrimEnd('＋')));
        var keys=Enumerable.Range(0x70,24).Concat(Enumerable.Range(0x41,26)).Concat(Enumerable.Range(0x30,10))
            .Concat(new[]{0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27,0x28,0x2D,0x2E}).Select(k=>(uint)k)
            .Concat(currentKey is >=8 and <=254?new[]{currentKey}:Array.Empty<uint>()).Distinct();
        void Setup(ComboBox box,System.Collections.Generic.IEnumerable<KeyOption> items,uint selected,int column,string label){
            box.ItemsSource=items.ToArray();box.DisplayMemberPath=nameof(KeyOption.Text);box.SelectedValuePath=nameof(KeyOption.Value);box.SelectedValue=selected;
            box.Padding=new Thickness(8,4,8,4);box.MinHeight=32;
            System.Windows.Automation.AutomationProperties.SetName(box,title+label);
            Grid.SetColumn(box,column);row.Children.Add(box);
        }
        mods.Margin=new Thickness(0,0,8,0);
        Setup(mods,modifierList,currentMods,1,"修饰键");
        Setup(key,keys.Select(k=>new KeyOption(k,KeyLabel(k))),currentKey,2,"按键");
        return row;
    }
    static string ModifierLabel(uint m)=>((m&2)!=0?"Ctrl＋":"")+((m&1)!=0?"Alt＋":"")+((m&4)!=0?"Shift＋":"")+((m&8)!=0?"Win＋":"");
    static string KeyLabel(uint k)=>k switch{
        0x20=>"空格",0x21=>"上翻页",0x22=>"下翻页",0x23=>"End",0x24=>"Home",
        0x25=>"左方向键",0x26=>"上方向键",0x27=>"右方向键",0x28=>"下方向键",0x2D=>"Insert",0x2E=>"Delete",
        _=>KeyInterop.KeyFromVirtualKey((int)k) is var key&&key!=Key.None?key.ToString():$"键码 {k:X2}"
    };
    void UpdateShortcutLabel(){
        currentShortcut.Text=DescribeKey(settings.Modifiers,settings.VirtualKey);
        normalHotkeyHint.Text=hotkeys?.ToggleId!=null?"切换内屏开关。可在设置中修改。":"尚未注册普通快捷键；仍可用窗口或托盘开关。";
        string key=DescribeKey(settings.EmergencyModifiers,settings.EmergencyVirtualKey);
        emergencyHint.Text=hotkeys?.EmergencyId!=null?$"紧急恢复：{key}。切到仅内屏（外接屏将暂时退出显示）。":
            $"紧急键 {key} 未注册，请用紧急恢复按钮，或按 Win＋P 选择“仅电脑屏幕”。";
    }
    Task SaveHotkeys()=>SaveHotkeys(Storage.SettingsPath);
    Task SaveHotkeys(string settingsPath){
        try{
            if(toggleModifiers.SelectedValue is not uint tm||toggleKey.SelectedValue is not uint tk||
                emergencyModifiers.SelectedValue is not uint em||emergencyKey.SelectedValue is not uint ek)
                throw new ArgumentException("请为两个操作分别选择修饰键和按键。");
            var toggle=new HotkeyChord(tm,tk);var emergency=new HotkeyChord(em,ek);
            var next=settings.WithHotkeys(toggle,emergency);
            (hotkeys??throw new InvalidOperationException("窗口快捷键尚未就绪。")).Apply(toggle,emergency,()=>Storage.Save(settingsPath,next));
            settings.Modifiers=tm;settings.VirtualKey=tk;settings.EmergencyModifiers=em;settings.EmergencyVirtualKey=ek;
            UpdateShortcutLabel();
            hotkeyFeedback.Foreground=Brush("#10584E");hotkeyFeedback.Text="两组快捷键已保存并立即生效。";
            Storage.Log("快捷键设置已保存",new{开关=DescribeKey(tm,tk),紧急=DescribeKey(em,ek)});
        }catch(Exception e){
            hotkeyFeedback.Foreground=Brush("#883C23");hotkeyFeedback.Text=$"{e.Message} 原来生效的快捷键已保留。";
            Storage.Log("快捷键设置未保存",e.Message);
        }
        return Task.CompletedTask;
    }
    async Task SaveUiPreview(TabControl tabs,string directory){
        Directory.CreateDirectory(directory);
        if(VddInstaller.IsReady()){
            await InstallVirtual();
            if(driverFeedback.Text!=VddInstaller.ReadyMessage)throw new InvalidOperationException("已安装虚拟驱动未正确提示。");
            Storage.Save(Path.Combine(directory,"驱动按钮验证.json"),new{已安装=true,显示文案=driverFeedback.Text});
        }
        tabs.SelectedIndex=1;
        await Dispatcher.InvokeAsync(()=>UpdateLayout(),DispatcherPriority.ApplicationIdle);
        toggleKey.IsDropDownOpen=true;
        try{
            await Dispatcher.InvokeAsync(()=>UpdateLayout(),DispatcherPriority.ApplicationIdle);
            if(toggleKey.Template.FindName("PART_Popup",toggleKey) is not System.Windows.Controls.Primitives.Popup popup||
                !popup.IsOpen||popup.Child is not FrameworkElement popupContent||popupContent.ActualHeight<=0)
                throw new InvalidOperationException("按键选择列表未正确展开。");
            Storage.Save(Path.Combine(directory,"下拉框验证.json"),new{正常展开=true,选项数=toggleKey.Items.Count});
        }finally{toggleKey.IsDropDownOpen=false;}
        if(hotkeys?.ToggleId!=null&&hotkeys.EmergencyId!=null){
            string sample=Path.Combine(directory,"快捷键试存.json");
            uint tm=settings.Modifiers,tk=settings.VirtualKey,em=settings.EmergencyModifiers,ek=settings.EmergencyVirtualKey;
            void Select(uint a,uint b,uint c,uint d){toggleModifiers.SelectedValue=a;toggleKey.SelectedValue=b;emergencyModifiers.SelectedValue=c;emergencyKey.SelectedValue=d;}
            try{
                Select(em,ek,tm,tk);await SaveHotkeys(sample);
                var saved=Storage.Load<Settings>(sample);
                if(saved?.Modifiers!=em||saved.VirtualKey!=ek||saved.EmergencyModifiers!=tm||saved.EmergencyVirtualKey!=tk)
                    throw new InvalidOperationException("双快捷键界面试存失败。");
            }finally{
                hotkeys.Apply(new(tm,tk),new(em,ek));
                settings.Modifiers=tm;settings.VirtualKey=tk;settings.EmergencyModifiers=em;settings.EmergencyVirtualKey=ek;
                Select(tm,tk,em,ek);UpdateShortcutLabel();hotkeyFeedback.Text="";
            }
        }
        var checks=new System.Collections.Generic.List<object>();
        foreach(var tab in tabs.Items.OfType<TabItem>()){
            tabs.SelectedItem=tab;
            await Dispatcher.InvokeAsync(()=>{tab.Focus();UpdateLayout();},DispatcherPriority.ApplicationIdle);
            var element=(FrameworkElement)Content;
            var surface=(FrameworkElement)tab.Template.FindName("Surface",tab);
            var clip=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(tab);
            if(clip!=null&&!clip.Bounds.Contains(new System.Windows.Rect(0,0,surface.ActualWidth,surface.ActualHeight)))throw new InvalidOperationException($"页签边框被裁切：{tab.Header}");
            var dpi=VisualTreeHelper.GetDpi(element);
            checks.Add(new{页签=tab.Header,焦点=tab.IsKeyboardFocused,缩放=dpi.DpiScaleX,边框宽度=surface.ActualWidth});
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(element.ActualHeight*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
            bitmap.Render(element);
            var png=new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output=File.Create(Path.Combine(directory,$"{tab.Header}.png"));
            png.Save(output);
        }
        Storage.Save(Path.Combine(directory,"页签验证.json"),checks);
    }
    static SolidColorBrush Brush(string color)=>(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    static Border Card(UIElement child,string background="#FFFFFF")=>new(){Child=child,Background=Brush(background),BorderBrush=Brush("#E2EAF0"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(18),Margin=new Thickness(0,0,0,12)};
    static StackPanel SectionTitle(string title,string subtitle){var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=title,FontSize=17,FontWeight=FontWeights.SemiBold});panel.Children.Add(new TextBlock{Text=subtitle,FontSize=12,Foreground=Brush("#526779"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)});return panel;}
    static void AddSetting(StackPanel parent,CheckBox setting,string title,string description){setting.Content=SectionTitle(title,description);setting.Margin=new Thickness(0,2,0,0);System.Windows.Automation.AutomationProperties.SetName(setting,title);parent.Children.Add(new Border{Child=setting,BorderBrush=Brush("#EDF2F6"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,4,0,4)});}
    void InitializedWindow(object? sender,EventArgs args) {
        source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source.AddHook(Hook);hotkeys=new(source.Handle);
        var emergency=new HotkeyChord(settings.EmergencyModifiers,settings.EmergencyVirtualKey);
        var toggle=new HotkeyChord(settings.Modifiers,settings.VirtualKey);
        try{hotkeys.Apply(null,emergency);}catch(Exception e){hotkeyFeedback.Text=e.Message;Storage.Log("紧急快捷键注册失败",e.Message);}
        try{hotkeys.Apply(toggle,hotkeys.EmergencyId!=null?emergency:null);}catch(Exception e){hotkeyFeedback.Text+= " "+e.Message;Storage.Log("普通快捷键注册失败",e.Message);}
        UpdateShortcutLabel();
        Storage.Log("紧急恢复快捷键注册",new{快捷键=DescribeKey(emergency.Modifiers,emergency.Key),成功=hotkeys.EmergencyId!=null});
        buttonInput=new(source.Handle);buttonInput.Pressed+=async()=>{
            powerButton.IsEnabled=true;
            if(!settings.EnablePowerButton)return;
            await PhysicalButton();
        };
        Guid guid=new("6fe69556-704a-47a0-8f24-c28d936fda47");powerNotification=Native.RegisterPowerSettingNotification(source.Handle,ref guid,0);
    }
    IntPtr Hook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled) {
        if(msg==0x312&&w.ToInt32()==hotkeys?.ToggleId){_=Execute(()=>controller.Toggle());handled=true;}
        if(msg==0x312&&w.ToInt32()==hotkeys?.EmergencyId){_=Execute(controller.ForceRestore);handled=true;}
        if(msg==0xff)buttonInput?.Input(l);
        if(msg==0x7e||msg==0x219)Dispatcher.BeginInvoke(async()=>await Execute(controller.EnvironmentChanged));
        if(msg==0x218){int evt=w.ToInt32();if(evt==0x8013&&l!=IntPtr.Zero)Storage.Log("控制台显示状态通知",Marshal.ReadInt32(l,20));if(evt==4)_=Execute(controller.Restore);if(evt==7||evt==18)Dispatcher.BeginInvoke(async()=>await Execute(controller.EnvironmentChanged));}
        if(msg==0x11){_=Exit();handled=false;}
        if(msg==Native.ExitMessage){_=Exit();handled=true;}
        return IntPtr.Zero;
    }
    internal static string DescribeKey(uint m,uint k)=>ModifierLabel(m)+KeyLabel(k);
    static Button Button(string text,Func<Task> action){var b=new Button{Content=text,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,8)};b.Click+=async(_,_)=>{try{await action();}catch(Exception e){Storage.Log("界面操作失败",e.ToString());MessageBox.Show(e.Message,"内屏开关");}};return b;}
    async Task Execute(Func<Task> action){try{await action();}catch(Exception e){Storage.Log("操作失败",e.ToString());status.Text=e.Message;}}
    void Run(Action action){try{action();}catch(Exception e){Storage.Log("设置失败",e.ToString());MessageBox.Show(this,e.Message,"设置未保存");startup.IsChecked=settings.StartWithWindows;}}
    void Save()=>Storage.Save(Storage.SettingsPath,settings);
    void Update(){state.Text=controller.State switch{PanelState.故障=>"需要检查内屏",PanelState.正在关闭=>"正在关闭内屏",PanelState.正在恢复=>"正在恢复内屏",_=>"内屏已"+controller.State};status.Text=controller.Message;toggle.IsEnabled=!controller.IsBusy;toggle.Content=controller.HasRecovery?"开启内屏":"关闭内屏";stateBadge.Text=controller.State switch{PanelState.开启=>"●  正常",PanelState.关闭=>"●  关屏中",PanelState.故障=>"●  待检查",_=>"●  处理中"};hero.Background=Brush(controller.State==PanelState.关闭?"#E8EEF5":controller.State==PanelState.故障?"#FFF0E8":"#E6F4EF");tray.Text="内屏开关 · "+controller.State;}
    void ShowSettings(){Show();WindowState=WindowState.Normal;Activate();}
    async Task RefreshEnvironment(){
        environment.Text="正在检测显示环境…";
        try{var report=await Task.Run(EnvironmentCheck.Collect);environment.Text=EnvironmentCheck.Text(report);Storage.Save(Path.Combine(Storage.Root,"显示环境检测.json"),report);}
        catch(Exception e){environment.Text="检测未完成："+e.Message;}
    }
    async Task RefreshDiagnostic(){diagnostic.Text="正在读取本机显示能力…";try{controller.Refresh();var result=await Task.Run(()=>Diagnostics.Collect());var displays=DisplayNative.Query().Paths.Select(DisplayNative.Describe).ToArray();diagnostic.Text="内置屏幕："+string.Join("、",displays.Where(x=>x.Internal).Select(x=>x.Name))+Environment.NewLine+"物理外接屏："+displays.Count(x=>!x.Internal&&!Controller.IsVirtual(x))+" 台"+Environment.NewLine+"虚拟显示器："+(DisplayNative.Query(true).Paths.Select(DisplayNative.Describe).Any(x=>x.Available&&Controller.IsVirtual(x))?"可用（按需启用）":"未检测到")+Environment.NewLine+"完整设备信息和运行记录可通过“导出诊断”保存。";Storage.Save(Path.Combine(Storage.Root,"最新诊断.json"),result);}catch(Exception e){diagnostic.Text="检测未完成："+e.Message;}}
    async Task ExportDiagnostic(){var dialog=new SaveFileDialog{FileName="内屏开关诊断.json",Filter="诊断文件|*.json"};if(dialog.ShowDialog(this)==true){var result=await Task.Run(()=>Diagnostics.Collect());Storage.Save(dialog.FileName,result);}}
    async Task InstallVirtual(){
        if(!driverButton.IsEnabled)return;
        driverButton.IsEnabled=false;driverFeedback.Foreground=Brush("#526779");driverFeedback.Text="正在检查虚拟显示驱动…";
        try{driverFeedback.Text=await VddInstaller.LaunchInstall();await RefreshDiagnostic();}
        catch(Exception e){driverFeedback.Foreground=Brush("#883C23");driverFeedback.Text=e.Message;Storage.Log("虚拟驱动按钮未完成",e.ToString());}
        finally{driverButton.IsEnabled=true;}
    }
    async Task PhysicalButton(){if(DateTime.UtcNow-lastButton<TimeSpan.FromMilliseconds(700))return;lastButton=DateTime.UtcNow;await Execute(()=>controller.Toggle());}
    async Task StartPowerAgent(){if(powerAgent?.Ready==true||startingAgent)return;startingAgent=true;try{powerAgent=await PowerAgentClient.Start();powerAgent.Pressed+=()=>Dispatcher.BeginInvoke(async()=>await PhysicalButton());Storage.Log("电源键代理已连接");}catch{powerButton.IsChecked=false;settings.EnablePowerButton=false;throw;}finally{startingAgent=false;}}
    async Task Exit(){if(exiting)return;await Execute(controller.Restore);if(controller.HasRecovery){MessageBox.Show(this,"内屏恢复尚未成功，请先使用独立恢复工具。","暂未退出");return;}if(powerAgent!=null){await powerAgent.DisposeAsync();powerAgent=null;}exiting=true;Close();}
}
