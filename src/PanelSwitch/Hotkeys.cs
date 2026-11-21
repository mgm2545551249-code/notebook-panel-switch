using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace PanelSwitch;

internal readonly record struct HotkeyChord(uint Modifiers,uint Key);

internal sealed class Hotkeys:IDisposable {
    readonly Action<int,HotkeyChord> register;
    readonly Action<int> unregister;
    Dictionary<HotkeyChord,int> active=new();
    internal int? ToggleId{get;private set;}
    internal int? EmergencyId{get;private set;}
    internal Hotkeys(IntPtr hwnd):this((id,key)=>{
        if(!Native.RegisterHotKey(hwnd,id,key.Modifiers|0x4000,key.Key)){
            int error=Marshal.GetLastWin32Error();
            throw new Win32Exception(error,error==1409?"组合键已被其他程序占用。":$"Windows 拒绝注册（错误 {error}）。");
        }
    },id=>Native.UnregisterHotKey(hwnd,id)){}
    internal Hotkeys(Action<int,HotkeyChord> register,Action<int> unregister){this.register=register;this.unregister=unregister;}

    internal void Apply(HotkeyChord? toggle,HotkeyChord? emergency,Action? persist=null){
        if(toggle.HasValue&&toggle==emergency)throw new ArgumentException("开关内屏和紧急恢复不能使用同一个组合键。");
        var requested=new Dictionary<HotkeyChord,int>();
        var staged=new List<int>();
        int? Stage(HotkeyChord? option,string label){
            if(option is not HotkeyChord key)return null;
            if(key.Modifiers is <1 or >15||key.Key is <8 or >254)throw new ArgumentException($"{label}需要选择有效的修饰键和按键。");
            if(!active.TryGetValue(key,out int id)){
                id=Enumerable.Range(18,6).First(candidate=>!active.ContainsValue(candidate)&&!staged.Contains(candidate));
                try{register(id,key);}catch(Win32Exception e){throw new InvalidOperationException($"{label}：{e.Message}",e);}
                staged.Add(id);
            }
            requested.Add(key,id);
            return id;
        }
        try{
            // 旧键继续生效，直到两组新键和配置保存都成功；互换时复用已有注册。
            int? newEmergency=Stage(emergency,"紧急恢复");
            int? newToggle=Stage(toggle,"开关内屏");
            persist?.Invoke();
            foreach(var old in active.Where(x=>!requested.ContainsKey(x.Key)))unregister(old.Value);
            active=requested;ToggleId=newToggle;EmergencyId=newEmergency;
        }catch{foreach(int id in staged)unregister(id);throw;}
    }
    public void Dispose(){foreach(int id in active.Values)unregister(id);active.Clear();ToggleId=EmergencyId=null;}
}
