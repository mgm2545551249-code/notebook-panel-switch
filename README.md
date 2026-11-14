# 笔记本内屏开关

适用于 Windows 11 的中文托盘工具，使用快捷键或笔记本实体电源键切换内屏开关。关屏后普通键鼠继续工作，不重新启用内屏。

本项目已在联想83LT上完成实机联调。用户确认实体电源键关屏、恢复正常，普通输入不会点亮内屏。其他硬件和驱动组合需要重新验证。

## 功能

- 默认快捷键 **Ctrl＋Alt＋F10**，可自定义并检查快捷键冲突。
- 实体电源键通过管理员代理监听系统 ACPI 按钮事件，退出后还原原电源策略。
- 有其他可用显示目标时，停用真实内屏路径；恢复时还原布局并尽力恢复窗口位置。
- 没有物理外接屏时，可安装虚拟显示驱动维持显示目标。
- 中文托盘、开机启动开关、诊断导出及运行日志。
- 独立恢复进程处理主程序异常退出、失联和测试超时。

软件不使用黑色遮罩、不屏蔽普通键鼠，也不反复发送关屏命令。

## 直接下载使用

无需自己编译，也不需要另外安装 .NET。

- **推荐：[下载完整便携包](https://github.com/mgm2545551249-code/notebook-panel-switch/releases/latest/download/PanelSwitch-win-x64.zip)**：包含程序、安装与恢复入口、可选虚拟显示驱动、中文说明和第三方许可。
- [单独下载可执行程序](https://github.com/mgm2545551249-code/notebook-panel-switch/releases/latest/download/PanelSwitch.exe)：适合已有可用外接屏或已经安装虚拟显示驱动的电脑。
- [查看版本发布页](https://github.com/mgm2545551249-code/notebook-panel-switch/releases/latest)。

完整便携包下载后，先解压整个文件夹，再运行 `PanelSwitch.exe`。需要开始菜单入口时运行 `安装.cmd`。不要在压缩包内直接运行程序。仅使用笔记本内屏时，可在设置中点击“安装虚拟显示驱动”；该步骤及实体电源键代理需要管理员权限。

适用于 Windows x64。不同电脑的固件、显卡驱动和按钮实现存在差异，首次使用应先执行十五秒关屏测试。

## 构建和运行

需要 Windows 及 .NET 8 SDK。在仓库根目录执行：

```powershell
dotnet build ./src/PanelSwitch/PanelSwitch.csproj -c Release
dotnet run --project ./src/PanelSwitch/PanelSwitch.csproj -c Release
```

生成包含运行环境的便携版本：

```powershell
./scripts/发布.ps1
```

输出位于 `artifacts/发行版`，不要求目标电脑预先安装 .NET。发布脚本默认不下载第三方驱动；需要驱动安装组件时执行：

```powershell
./scripts/发布.ps1 -IncludeVirtualDriver
```

第三方驱动从固定版本的官方发布页下载，安装前校验文件哈希及签名。安装需要管理员权限。

## 使用

运行 `PanelSwitch.exe` 后，按默认快捷键切换内屏开关，或在设置中启用实体电源键。没有物理外接屏时，需要虚拟显示器处于可用状态，并勾选允许使用虚拟显示器。

关闭窗口后程序保留在托盘。选择“退出并恢复”可恢复显示及电源键策略。开机启动默认关闭。

独立恢复命令：

```powershell
./artifacts/发行版/PanelSwitch.exe --recover
```

详细说明见[使用说明](docs/使用说明.md)、[实现说明](docs/实现说明.md)和[实机验证摘要](docs/实机验证摘要.md)。

## 验证情况

十三项自动检查通过；真实显示路径开关五十轮通过；快捷键、普通输入保持关屏、独立恢复及电源键现场联调通过。

物理外接屏插拔、实际电池供电切换、长时间音频和下载尚未完成对应场景实测。持续集成只验证构建；实机检查依赖交互式 Windows 会话及显示硬件。

## 目录

- `src/PanelSwitch`：应用、显示控制、按键代理及恢复进程。
- `scripts`：发布和可选虚拟显示驱动安装组件。
- `docs`：中文说明、验证摘要及第三方许可。

本仓库不包含用户配置、凭据、本机原始日志或预编译运行环境。第三方组件许可见 `docs/第三方许可`。
