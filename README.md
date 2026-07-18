# ApexTrace

ApexTrace 是面向 **Le Mans Ultimate** 的 Windows 桌面遥测工具。当前仓库是第一个可运行版本：使用 .NET 10 + WPF，能只读检查官方 `LMU_Data` 接口、导入 LMU 原生 DuckDB、显示真实轨迹与车辆数据、回放片段，并保存/导出 `.apextrace` 会话包。

## 直接运行

```powershell
.\scripts\run.ps1
```

首次运行脚本会使用仓库内 `.dotnet` SDK；若缺失，会下载微软官方 .NET 10 SDK。默认检查的 LMU 目录为：

```text
D:\SteamLibrary\steamapps\common\Le Mans Ultimate
```

游戏未运行时，程序会只读导入 `UserData\Telemetry` 下最新的非空官方 DuckDB。游戏运行且官方插件已启用、`LMU_Data` 可用时，首页的“开始记录”才会启用。

## 构建与验证

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\capture-ui.ps1
.\scripts\publish.ps1
```

发布输出位于 `artifacts\ApexTrace-win-x64`。UI 自动截图位于 `screenshots`。

## 安全边界

- 只使用 LMU 官方 Header、官方 `LMU_Data` 共享内存和合法可读的游戏遥测/元数据。
- 共享内存始终以 `MemoryMappedFileRights.Read` 打开。
- 不注入游戏、不 Hook、不写游戏内存，不使用旧 rFactor 2 共享内存 DLL。
- 不解密受保护的 MAS；缺少可访问官方赛道几何时，由真实遥测轨迹降级重建，并显示其为片段/未知精度。
- 数据不足时不生成调教建议，也不把残圈标成完整圈。

## 当前已知限制

- 本机可用 LMU 遥测只有一个 45.69 秒的 Spa 排位片段，没有完整圈；因此完整分圈、双圈 Delta、弯道边界和数值调教建议尚未用真实完整圈验证。
- 本轮验证时 LMU 未运行，官方共享内存的布局与只读访问路径已验证，但实际实时事件流仍需在游戏运行中验证。
- 第一版没有安装器；提供可直接运行的自包含 win-x64 发布目录。

完整验证结果见 [docs/VERIFICATION.md](docs/VERIFICATION.md)。
