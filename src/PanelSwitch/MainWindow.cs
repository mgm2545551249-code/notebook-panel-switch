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
    readonly TextBox shortcut=new(){IsReadOnly=true,MinWidth=200};
    readonly CheckBox startup=new(){Content="登录后自动运行",Margin=new Thickness(0,8,0,0)};
    readonly CheckBox awake=new(){Content="关屏期间保持后台运行",Margin=new Thickness(0,8,0,0)};
    readonly CheckBox virtualDisplay=new(){Content="允许使用已安装的虚拟显示器维持单内屏关屏",Margin=new Thickness(0,8,0,0)};
    readonly CheckBox powerButton=new(){Content="启用实体电源键控制（需要管理员代理）",Margin=new Thickness(0,8,0,0),IsEnabled=false};
    readonly Border hero; readonly TextBlock stateBadge=new(){FontSize=12,FontWeight=FontWeights.SemiBold}; readonly Button toggle; Hotkey? hotkey;PowerButton? buttonInput;HwndSource? source;
    uint nextModifiers,nextKey;bool exiting,ticking;IntPtr powerNotification;DateTime lastButton;
    readonly bool smoke,background;PowerAgentClient? powerAgent;bool startingAgent;
    public MainWindow(bool smoke=false,bool background=false) {
        this.smoke=smoke;this.background=background;controller=new(settings);
        Title="内屏开关";Width=860;Height=900;MinWidth=700;MinHeight=650;
        Background=Brush("#F3F7FA");Foreground=Brush("#19384B");FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/PanelSwitch;component/Theme.xaml",UriKind.Relative)});
        Icon=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.png"));
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Content=scroll;
        var root=new StackPanel{Margin=new Thickness(28,22,28,20)};scroll.Content=root;
        var header=new Grid{Margin=new Thickness(0,0,0,22)};header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(new Image{Source=Icon,Width=46,Height=46,Margin=new Thickness(0,0,12,0)});
        var heading=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(heading,1);header.Children.Add(heading);
        heading.Children.Add(new TextBlock{Text="内屏开关",FontSize=23,FontWeight=FontWeights.SemiBold});
        heading.Children.Add(new TextBlock{Text="轻按一下，让屏幕休息",FontSize=12,Foreground=Brush("#728391"),Margin=new Thickness(0,3,0,0)});root.Children.Add(header);
        var heroContent=new StackPanel();
        var heroHeading=new Grid();heroHeading.ColumnDefinitions.Add(new ColumnDefinition());heroHeading.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        state.FontSize=30;state.Foreground=Brush("#163F47");heroHeading.Children.Add(state);
        var badge=new Border{Background=Brush("#D3F0E7"),CornerRadius=new CornerRadius(12),Padding=new Thickness(12,5,12,5),VerticalAlignment=VerticalAlignment.Center,Child=stateBadge};Grid.SetColumn(badge,1);heroHeading.Children.Add(badge);heroContent.Children.Add(heroHeading);
        status.Foreground=Brush("#52716F");status.Margin=new Thickness(0,10,0,18);heroContent.Children.Add(status);
        var actions=new WrapPanel();heroContent.Children.Add(actions);
        toggle=Button("关闭内屏",async()=>await controller.Toggle());toggle.Background=Brush("#128B86");toggle.Foreground=Brushes.White;toggle.BorderBrush=Brush("#128B86");toggle.MinWidth=130;actions.Children.Add(toggle);
        var test=Button("测试关屏 · 15 秒",async()=>await controller.Toggle(15));test.ToolTip="关闭内屏，并在十五秒后自动恢复";actions.Children.Add(test);
        actions.Children.Add(Button("恢复内屏",async()=>{await controller.Restore();Recovery.RestorePending();controller.Refresh();}));
        heroContent.Children.Add(new TextBlock{Text="也可以短按电源键，或使用下方快捷键",FontSize=12,Foreground=Brush("#64837F"),Margin=new Thickness(0,5,0,0)});
        hero=Card(heroContent,"#E6F4EF");root.Children.Add(hero);
        var settingsPanel=new StackPanel();settingsPanel.Children.Add(SectionTitle("控制设置","选择你习惯的开关方式"));
        var keys=new Grid{Margin=new Thickness(0,14,0,12)};keys.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});keys.ColumnDefinitions.Add(new ColumnDefinition());keys.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        keys.Children.Add(new TextBlock{Text="开关快捷键",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,18,0)});
        nextModifiers=settings.Modifiers;nextKey=settings.VirtualKey;shortcut.Text=DescribeKey(nextModifiers,nextKey);shortcut.Margin=new Thickness(0,0,12,0);shortcut.FontSize=15;
        shortcut.ToolTip="点击后按下新的组合键，再保存";shortcut.PreviewKeyDown+=CaptureShortcut;Grid.SetColumn(shortcut,1);keys.Children.Add(shortcut);
        var save=Button("保存快捷键",()=>{hotkey!.Set(nextModifiers,nextKey);settings.Modifiers=nextModifiers;settings.VirtualKey=nextKey;Save();return Task.CompletedTask;});save.Margin=new Thickness(0);Grid.SetColumn(save,2);keys.Children.Add(save);settingsPanel.Children.Add(keys);
        startup.IsChecked=Install.StartupEnabled();settings.StartWithWindows=startup.IsChecked==true;awake.IsChecked=settings.KeepAwake;virtualDisplay.IsChecked=settings.AllowVirtualDisplay;
        startup.Click+=(_,_)=>Run(()=>{Install.SetStartup(startup.IsChecked==true);settings.StartWithWindows=startup.IsChecked==true;Save();});
        awake.Click+=(_,_)=>Run(()=>{settings.KeepAwake=awake.IsChecked==true;Save();if(controller.HasRecovery)Native.Awake(settings.KeepAwake);});
        virtualDisplay.Click+=(_,_)=>Run(()=>{settings.AllowVirtualDisplay=virtualDisplay.IsChecked==true;Save();controller.Refresh();});
        powerButton.IsEnabled=true;
        powerButton.IsChecked=settings.EnablePowerButton;
        powerButton.Click+=async(_,_)=>await Execute(async()=>{if(powerButton.IsChecked==true)await StartPowerAgent();else if(powerAgent!=null){await powerAgent.DisposeAsync();powerAgent=null;}settings.EnablePowerButton=powerButton.IsChecked==true;Save();});
        AddSetting(settingsPanel,startup,"登录后自动运行","登录 Windows 后收进托盘，随时可用");
        AddSetting(settingsPanel,awake,"关屏时保持电脑运行","后台任务继续运行，避免空闲自动睡眠");
        AddSetting(settingsPanel,virtualDisplay,"单内屏模式","没有外接屏时，使用虚拟显示器维持关屏");
        AddSetting(settingsPanel,powerButton,"实体电源键控制","短按切换屏幕，需要管理员代理；退出后还原原设置");
        root.Children.Add(Card(settingsPanel));
        var devicePanel=new StackPanel();var deviceTitle=new Grid();deviceTitle.ColumnDefinitions.Add(new ColumnDefinition());deviceTitle.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        deviceTitle.Children.Add(SectionTitle("设备状态","显示连接与控制能力"));var refresh=Button("重新检测",RefreshDiagnostic);refresh.Margin=new Thickness(0);Grid.SetColumn(refresh,1);deviceTitle.Children.Add(refresh);devicePanel.Children.Add(deviceTitle);
        diagnostic.MinHeight=0;diagnostic.Height=94;diagnostic.BorderThickness=new Thickness(0);diagnostic.Background=Brushes.Transparent;diagnostic.Padding=new Thickness(0);diagnostic.Margin=new Thickness(0,14,0,0);diagnostic.FontSize=13;devicePanel.Children.Add(diagnostic);
        root.Children.Add(Card(devicePanel));
        var advanced=new StackPanel();var tools=new WrapPanel{Margin=new Thickness(0,14,0,0)};advanced.Children.Add(tools);
        tools.Children.Add(Button("导出诊断",ExportDiagnostic));
        tools.Children.Add(Button("常规接口对照测试",async()=>{if(MessageBox.Show(this,"该对照测试会关闭所有屏幕，普通键鼠可能唤醒。十五秒后自动恢复。继续测试？","对照测试",MessageBoxButton.YesNo)==MessageBoxResult.Yes)await controller.Toggle(15,new BaselineBackend());}));
        tools.Children.Add(Button("安装虚拟显示驱动",InstallVirtual));
        tools.Children.Add(Button("打开日志目录",()=>{Directory.CreateDirectory(Storage.Root);Process.Start(new ProcessStartInfo("explorer.exe",Storage.Root){UseShellExecute=true});return Task.CompletedTask;}));
        advanced.Children.Add(new TextBlock{Text="诊断、驱动安装与对照测试仅在排查问题时使用。",Foreground=Brush("#728391"),FontSize=12,TextWrapping=TextWrapping.Wrap});
        var expander=new Expander{Header="高级工具",Content=advanced,FontSize=14};root.Children.Add(Card(expander));
        root.Children.Add(new TextBlock{Text="关闭窗口后仍在托盘运行 · 退出时自动恢复内屏",Foreground=Brush("#7A8B99"),FontSize=12,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,5,0,0),TextWrapping=TextWrapping.Wrap});
        controller.Changed+=Update;
        using(var iconStream=typeof(MainWindow).Assembly.GetManifestResourceStream("PanelSwitch.Assets.app.ico")){tray.Icon=iconStream!=null?new System.Drawing.Icon(iconStream):System.Drawing.SystemIcons.Application;}tray.Text="内屏开关";
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("切换内屏",null,async(_,_)=>await Execute(()=>controller.Toggle()));menu.Items.Add("显示设置",null,(_,_)=>ShowSettings());menu.Items.Add("恢复内屏",null,async(_,_)=>await Execute(controller.Restore));menu.Items.Add("退出并恢复",null,async(_,_)=>await Exit());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ShowSettings();tray.Visible=true;
        SourceInitialized+=InitializedWindow;Loaded+=async(_,_)=>{if(background)Hide();await RefreshDiagnostic();scroll.ScrollToTop();if(!smoke&&settings.EnablePowerButton)await Execute(StartPowerAgent);if(smoke){Storage.Save(Path.Combine(Storage.Root,"界面冒烟测试.json"),new{结果="通过",标题=Title,快捷键=shortcut.Text,状态=controller.State.ToString(),时间=DateTimeOffset.Now});await Exit();}};
        timer.Tick+=async(_,_)=>{if(ticking)return;ticking=true;try{if(powerAgent!=null)await powerAgent.Pulse();await controller.Tick();}catch(Exception e){Storage.Log("恢复监视异常",e.ToString());}finally{ticking=false;}};timer.Start();
        Closing+=(_,e)=>{if(!exiting){e.Cancel=true;Hide();}};
        Closed+=(_,_)=>{timer.Stop();hotkey?.Dispose();buttonInput?.Dispose();if(powerNotification!=IntPtr.Zero)Native.UnregisterPowerSettingNotification(powerNotification);source?.RemoveHook(Hook);tray.Icon?.Dispose();tray.Dispose();};
    }
    static SolidColorBrush Brush(string color)=>(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    static Border Card(UIElement child,string background="#FFFFFF")=>new(){Child=child,Background=Brush(background),BorderBrush=Brush("#E2EAF0"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(22),Margin=new Thickness(0,0,0,16)};
    static StackPanel SectionTitle(string title,string subtitle){var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=title,FontSize=17,FontWeight=FontWeights.SemiBold});panel.Children.Add(new TextBlock{Text=subtitle,FontSize=12,Foreground=Brush("#7A8B99"),Margin=new Thickness(0,4,0,0)});return panel;}
    static void AddSetting(StackPanel parent,CheckBox setting,string title,string description){setting.Content=SectionTitle(title,description);setting.Margin=new Thickness(0,9,0,0);System.Windows.Automation.AutomationProperties.SetName(setting,title);parent.Children.Add(new Border{Child=setting,BorderBrush=Brush("#EDF2F6"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,4,0,4)});}
    void InitializedWindow(object? sender,EventArgs e) {
        source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source.AddHook(Hook);hotkey=new(source.Handle);
        try{hotkey.Set(settings.Modifiers,settings.VirtualKey);}catch(Exception error){diagnostic.Text=error.Message;}
        buttonInput=new(source.Handle);buttonInput.Pressed+=async()=>{
            powerButton.IsEnabled=true;
            if(!settings.EnablePowerButton)return;
            await PhysicalButton();
        };
        Guid guid=new("6fe69556-704a-47a0-8f24-c28d936fda47");powerNotification=Native.RegisterPowerSettingNotification(source.Handle,ref guid,0);
    }
    IntPtr Hook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled) {
        if(msg==0x312&&w.ToInt32()==18){_=Execute(()=>controller.Toggle());handled=true;}
        if(msg==0xff)buttonInput?.Input(l);
        if(msg==0x7e||msg==0x219)Dispatcher.BeginInvoke(async()=>await Execute(controller.EnvironmentChanged));
        if(msg==0x218){int evt=w.ToInt32();if(evt==0x8013&&l!=IntPtr.Zero)Storage.Log("控制台显示状态通知",Marshal.ReadInt32(l,20));if(evt==4)_=Execute(controller.Restore);if(evt==7||evt==18)Dispatcher.BeginInvoke(async()=>await Execute(controller.EnvironmentChanged));}
        if(msg==0x11){_=Exit();handled=false;}
        if(msg==Native.ExitMessage){_=Exit();handled=true;}
        return IntPtr.Zero;
    }
    void CaptureShortcut(object sender,KeyEventArgs e) {
        e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key==Key.LeftAlt||key==Key.RightAlt||key==Key.LeftCtrl||key==Key.RightCtrl||key==Key.LeftShift||key==Key.RightShift||key==Key.LWin||key==Key.RWin)return;
        var modifiers=Keyboard.Modifiers;nextModifiers=0;
        if(modifiers.HasFlag(ModifierKeys.Alt))nextModifiers|=1;if(modifiers.HasFlag(ModifierKeys.Control))nextModifiers|=2;if(modifiers.HasFlag(ModifierKeys.Shift))nextModifiers|=4;if(modifiers.HasFlag(ModifierKeys.Windows))nextModifiers|=8;
        nextKey=(uint)KeyInterop.VirtualKeyFromKey(key);shortcut.Text=DescribeKey(nextModifiers,nextKey);
    }
    internal static string DescribeKey(uint m,uint k)=>((m&2)!=0?"Ctrl＋":"")+((m&1)!=0?"Alt＋":"")+((m&4)!=0?"Shift＋":"")+((m&8)!=0?"Win＋":"")+KeyInterop.KeyFromVirtualKey((int)k);
    static Button Button(string text,Func<Task> action){var b=new Button{Content=text,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,8)};b.Click+=async(_,_)=>{try{await action();}catch(Exception e){Storage.Log("界面操作失败",e.ToString());MessageBox.Show(e.Message,"内屏开关");}};return b;}
    async Task Execute(Func<Task> action){try{await action();}catch(Exception e){Storage.Log("操作失败",e.ToString());status.Text=e.Message;}}
    void Run(Action action){try{action();}catch(Exception e){Storage.Log("设置失败",e.ToString());MessageBox.Show(this,e.Message,"设置未保存");startup.IsChecked=settings.StartWithWindows;}}
    void Save()=>Storage.Save(Storage.SettingsPath,settings);
    void Update(){state.Text="内屏"+controller.State;status.Text=controller.Message;toggle.IsEnabled=!controller.IsBusy;toggle.Content=controller.HasRecovery?"开启内屏":"关闭内屏";stateBadge.Text=controller.State==PanelState.开启?"●  已开启":controller.State==PanelState.关闭?"●  已关闭":"●  "+controller.State;hero.Background=Brush(controller.State==PanelState.关闭?"#E8EEF5":controller.State==PanelState.故障?"#FFF0E8":"#E6F4EF");tray.Text="内屏开关 · "+controller.State;}
    void ShowSettings(){Show();WindowState=WindowState.Normal;Activate();}
    async Task RefreshDiagnostic(){diagnostic.Text="正在读取本机显示能力…";try{controller.Refresh();var result=await Task.Run(()=>Diagnostics.Collect());var displays=DisplayNative.Query().Paths.Select(DisplayNative.Describe).ToArray();diagnostic.Text="内置屏幕："+string.Join("、",displays.Where(x=>x.Internal).Select(x=>x.Name))+Environment.NewLine+"物理外接屏："+displays.Count(x=>!x.Internal&&!Controller.IsVirtual(x))+" 台"+Environment.NewLine+"虚拟显示器："+(displays.Any(Controller.IsVirtual)?"已就绪":"未检测到")+Environment.NewLine+"完整设备信息和运行记录可通过“导出诊断”保存。";Storage.Save(Path.Combine(Storage.Root,"最新诊断.json"),result);}catch(Exception e){diagnostic.Text="检测未完成："+e.Message;}}
    async Task ExportDiagnostic(){var dialog=new SaveFileDialog{FileName="内屏开关诊断.json",Filter="诊断文件|*.json"};if(dialog.ShowDialog(this)==true){var result=await Task.Run(()=>Diagnostics.Collect());Storage.Save(dialog.FileName,result);}}
    async Task InstallVirtual(){await VddInstaller.LaunchInstall();await RefreshDiagnostic();}
    async Task PhysicalButton(){if(DateTime.UtcNow-lastButton<TimeSpan.FromMilliseconds(700))return;lastButton=DateTime.UtcNow;await Execute(()=>controller.Toggle());}
    async Task StartPowerAgent(){if(powerAgent?.Ready==true||startingAgent)return;startingAgent=true;try{powerAgent=await PowerAgentClient.Start();powerAgent.Pressed+=()=>Dispatcher.BeginInvoke(async()=>await PhysicalButton());Storage.Log("电源键代理已连接");}catch{powerButton.IsChecked=false;settings.EnablePowerButton=false;throw;}finally{startingAgent=false;}}
    async Task Exit(){if(exiting)return;await Execute(controller.Restore);if(controller.HasRecovery){MessageBox.Show(this,"内屏恢复尚未成功，请先使用独立恢复工具。","暂未退出");return;}if(powerAgent!=null){await powerAgent.DisposeAsync();powerAgent=null;}exiting=true;Close();}
}
