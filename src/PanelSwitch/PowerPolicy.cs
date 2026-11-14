using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PanelSwitch;
public sealed class PowerPolicySnapshot {
    public int OwnerPid {get;set;}
    public long OwnerStarted {get;set;}
    public Guid Scheme {get;set;}
    public uint Ac {get;set;}
    public uint Dc {get;set;}
    public uint Applied {get;set;}
}
internal static class PowerPolicy {
    static Guid Subgroup=new("4f971e89-eebd-4455-a8de-9e59040e7347"),Button=new("7648efa3-dd9c-4e3e-b566-50f929386280");
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr key,out IntPtr scheme);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr key,ref Guid scheme,ref Guid subgroup,ref Guid setting,out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr key,ref Guid scheme,ref Guid subgroup,ref Guid setting,out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr key,ref Guid scheme,ref Guid subgroup,ref Guid setting,uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr key,ref Guid scheme,ref Guid subgroup,ref Guid setting,uint value);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr key,ref Guid scheme);
    static void Check(uint rc){if(rc!=0)throw new Win32Exception((int)rc,"读写电源键策略失败");}
    public static PowerPolicySnapshot Read() {
        Check(PowerGetActiveScheme(IntPtr.Zero,out IntPtr pointer));Guid scheme;
        try{scheme=Marshal.PtrToStructure<Guid>(pointer);}finally{LocalFree(pointer);}
        Check(PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref Subgroup,ref Button,out uint ac));
        Check(PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref Subgroup,ref Button,out uint dc));
        return new(){Scheme=scheme,Ac=ac,Dc=dc,Applied=0};
    }
    public static void Apply(PowerPolicySnapshot snapshot) {
        Guid scheme=snapshot.Scheme;
        Check(PowerWriteACValueIndex(IntPtr.Zero,ref scheme,ref Subgroup,ref Button,snapshot.Applied));
        try{Check(PowerWriteDCValueIndex(IntPtr.Zero,ref scheme,ref Subgroup,ref Button,snapshot.Applied));Check(PowerSetActiveScheme(IntPtr.Zero,ref scheme));}
        catch{Restore(snapshot);throw;}
    }
    public static bool ShouldRestore(uint current,uint applied)=>current==applied;
    public static void Restore(PowerPolicySnapshot snapshot) {
        Guid scheme=snapshot.Scheme;bool changed=false;
        Check(PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref Subgroup,ref Button,out uint ac));
        Check(PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref Subgroup,ref Button,out uint dc));
        if(ShouldRestore(ac,snapshot.Applied)){Check(PowerWriteACValueIndex(IntPtr.Zero,ref scheme,ref Subgroup,ref Button,snapshot.Ac));changed=true;}
        else Storage.Log("保留用户修改的接电策略",new{当前=ac,原始=snapshot.Ac});
        if(ShouldRestore(dc,snapshot.Applied)){Check(PowerWriteDCValueIndex(IntPtr.Zero,ref scheme,ref Subgroup,ref Button,snapshot.Dc));changed=true;}
        else Storage.Log("保留用户修改的电池策略",new{当前=dc,原始=snapshot.Dc});
        if(changed&&Read().Scheme==scheme)Check(PowerSetActiveScheme(IntPtr.Zero,ref scheme));
    }
}
