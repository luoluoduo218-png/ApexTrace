# ApexTrace 开发包 — 先读这里

本开发包用于让 Codex 从零实现可直接运行的 Windows 版 ApexTrace：一个面向《Le Mans Ultimate》的遥测采集、赛道走线回放、双圈比较、逐弯驾驶分析与基础调教建议软件。

## 交付物

- `CODEX_BUILD_SPEC.md`：Codex 的主工程说明书，优先读取。
- `ApexTrace_Codex_开发说明书.docx`：排版版说明书，包含全部 UI 参考图。
- `assets/ui/`：10 张 UI 页面参考图。
- `docs/UI_IMPLEMENTATION.md`：UI 页面逐项实现要求。
- `docs/DATA_CONTRACT.md`：数据结构和字段契约。
- `docs/ACCEPTANCE_CHECKLIST.md`：阶段验收清单。
- `reference/SOURCES.md`：官方文档、参考实现和依赖链接。

## 给 Codex 的第一条指令

请完整阅读 `CODEX_BUILD_SPEC.md`、`docs/` 与 `assets/ui/` 后再创建代码。不要先写一个单文件演示，不要只搭静态界面，不要使用旧的 rFactor 2 Shared Memory DLL。按文档中的阶段顺序实现，每完成一个阶段就运行测试、生成截图、更新验收清单并提交 Git commit。

## 不可妥协的原则

1. Windows 原生桌面应用，`.NET 10 + WPF`。
2. 只读 LMU 官方内置共享内存 `LMU_Data`；不注入、不修改游戏内存、不使用旧 rFactor 2 DLL。
3. UI 必须按 `assets/ui/` 重建为真实 WPF 控件，不得把整张参考图当背景。
4. 采集期间先写临时数据；结束时用户可以“保存到本地 / 导出文件 / 丢弃记录”。
5. 所有建议必须有数据依据、置信度和可验证方法；数据不足时显示“不生成建议”，不能猜。
6. 所有解析器都必须版本化；游戏更新导致结构不匹配时安全失败并给出诊断。
