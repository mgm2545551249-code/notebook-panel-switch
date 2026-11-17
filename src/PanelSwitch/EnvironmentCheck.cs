using System;
using System.IO;
using System.Text;
using System.Text.Json;
namespace PanelSwitch;

// 只读检查：不关屏、不改缩放、不安装或重新加载驱动。
internal static class EnvironmentCheck {
    internal static string Summary(object parameters){
        using var doc=JsonDocument.Parse(JsonSerializer.Serialize(parameters,Storage.Json));
        var root=doc.RootElement;var text=new StringBuilder();int panels=0,virtuals=0;
        foreach(var p in root.GetProperty("路径").EnumerateArray()){
            var device=p.GetProperty("设备");bool internalPanel=device.GetProperty("Internal").GetBoolean();
            string name=device.GetProperty("Name").GetString()??"未知";
            if(internalPanel)panels++;
            bool virt=device.GetProperty("DevicePath").GetString()?.Contains("MTT",StringComparison.OrdinalIgnoreCase)==true||name.Contains("Virtual",StringComparison.OrdinalIgnoreCase)||name.Contains("VDD",StringComparison.OrdinalIgnoreCase);
            if(virt)virtuals++;
            string size="模式未知";
            var mode=p.GetProperty("桌面模式");if(mode.ValueKind==JsonValueKind.Object){var s=mode.GetProperty("Source");var position=s.GetProperty("Position");size=$"{s.GetProperty("Width")} × {s.GetProperty("Height")}，坐标 {position.GetProperty("X")}，{position.GetProperty("Y")}";}
            var refresh=p.GetProperty("目标").GetProperty("Refresh");uint denominator=refresh.GetProperty("Denominator").GetUInt32();
            string hz=denominator==0?"未知":((double)refresh.GetProperty("Numerator").GetUInt32()/denominator).ToString("0.##");
            text.AppendLine($"{(internalPanel?"内屏":virt?"虚拟屏":"外接屏")}：{name}；{size}；{hz} Hz");
        }
        foreach(var monitor in root.GetProperty("监视器").EnumerateArray()){
            int rc=monitor.GetProperty("Dpi结果").GetInt32();var work=monitor.GetProperty("工作区");
            string scale=rc==0?$"{monitor.GetProperty("DpiX").GetUInt32()*100.0/96:0.#}%":"读取失败";
            text.AppendLine($"逻辑桌面 {monitor.GetProperty("设备").GetString()}：缩放 {scale}；工作区 {work.GetProperty("Right").GetInt32()-work.GetProperty("Left").GetInt32()} × {work.GetProperty("Bottom").GetInt32()-work.GetProperty("Top").GetInt32()}");
        }
        if(panels!=1)text.AppendLine("需检查：当前未识别到唯一活动内屏。");
        if(virtuals==0)text.AppendLine("未发现活动虚拟屏；无物理外接屏时先准备虚拟显示驱动。");
        text.AppendLine("以上仅为当前连接状态。复制模式下共用参数，不代表独立虚拟屏参数一致。");
        return text.ToString();
    }
    internal static object Collect(){
        var current=DisplayNative.Parameters();string text=Summary(current);
        string before=Path.Combine(Storage.Root,"最近关屏前显示参数.json"),off=Path.Combine(Storage.Root,"最近关屏后显示参数.json");
        var previous=Storage.Load<JsonElement?>(before);var independent=Storage.Load<JsonElement?>(off);
        if(previous.HasValue&&independent.HasValue){
            text+="\n最近一次关屏前记录（历史样本，换机或改配置后须重新测试）：\n"+Summary(previous.Value);
            text+="\n最近一次关屏后记录：\n"+Summary(independent.Value);
        }else text+="\n独立关屏状态：尚无完整检测记录。请使用现有十五秒关屏测试，恢复后重新检测。";
        text+="\n实体电源键、真实停止发光及无闪动效果仍需现场确认；本检测不会自动修改电脑配置。";
        return new{时间=DateTimeOffset.Now,说明=text,当前参数=current,最近关屏前=previous,最近关屏后=independent};
    }
    internal static string Text(object report){using var doc=JsonDocument.Parse(JsonSerializer.Serialize(report,Storage.Json));return doc.RootElement.GetProperty("说明").GetString()??"";}
}
