# 第一可运行版本验证记录

验证日期：2026-07-18（Asia/Shanghai）

## 已实际验证

- 读取 LMU 安装目录 `D:\SteamLibrary\steamapps\common\Le Mans Ultimate`。
- 官方共享内存 Header 的 SHA-256 与代码固定值一致：
  - `InternalsPlugin.hpp`: `9B6EE8CF610FA5049B18DF580A9A9BC9EBB91346FC466584D576A6442ABCF68F`
  - `PluginObjects.hpp`: `F65F1D2226AF1ACB277F8337FB10D8955DB384118FC36EEF95EA446BE058E247`
  - `SharedMemoryInterface.hpp`: `194FF1AB39030BC811540931C8B9817258727252C9A4B35FA4734BBAA16D4DDC`
- x64、`#pragma pack(4)`、一字节 bool 合约及根结构大小 324,820 字节通过测试。
- 只读导入真实 LMU Spa DuckDB：4,570 个 GPS Time 样本、100 Hz、45.69 秒、最大圈内距离大于 1,400 m。
- 真实数据只形成一个不完整圈；导入器没有将其误报为完整圈，也没有生成无证据建议。
- `.apextrace` 导出及重新打开通过；包内含 manifest、JSON、CSV、Parquet、SVG 与 PNG。
- Spa 1.29 MFT 可读并识别 Studio 397；未尝试打开或解密受保护 MAS，轨迹降级到运行时重建。
- WPF 应用实际启动，自动导入真实数据并生成 10 张页面截图；视觉检查后修复标题前景色。
- 16 项自动化测试通过。
- Release 自包含 win-x64 发布成功，并从发布目录启动后再次生成 10 张 UI 截图。

## 本轮不能验证

- 验证时 LMU 未运行，因此 `LMU_Data` 的实时事件计数、玩家车辆解析与长时间实时记录未进行游戏内实测。
- 现有官方 DuckDB 不含完整圈，因此自动分圈的真实完整圈、双圈 Delta、弯道切分和基于多圈证据的建议仍未实测。
- 未执行 60 分钟耐久、100%/125%/150% DPI 全矩阵、干净 Windows 机器和安装/卸载测试。

这些项目保持未完成状态，不作为第一可运行版本的已完成功能报告。

## 已知构建警告

`SkiaSharp.Views.WPF 3.119.4` 的 NuGet 元数据仍以传统 .NET Framework WPF 资产为兼容目标，因此 .NET 10 还原会报告 `NU1701`（并涉及其 OpenTK WPF 传递依赖）。本轮没有隐藏该警告；Debug、Release 自包含启动和全部截图均已验证画布可正常运行。
