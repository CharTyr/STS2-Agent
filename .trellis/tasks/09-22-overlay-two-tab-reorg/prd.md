# UI 重组：游玩/设置双 tab 与模式滑块

## Goal

把游戏内覆盖层从 6 个分散 tab（dual/chat/play/settings/connect/decisions）重组为 **游玩 / 设置** 两个顶级 tab。游玩页以对话为中心，内嵌模式滑块（单人⇄多人）、游玩控制、决策日志与 Jev 面板；设置页收纳模型/端点/预算/外观，并把接入（MCP）作为其子选项页。

## Requirements

### 顶级结构
- 顶级 tab 只剩两个：`play`（游玩）与 `settings`（设置）。`OverlayTabCatalog` 收缩为两项，`AgentOverlayHost.Tabs.cs` 的 `BuildPageForTab` 对应两条 arm。
- 默认落 tab：首次运行 → settings；否则 → play（companion 实例维持现状逻辑）。

### 游玩页（play）
- 顶部**模式滑块**（分段控件）：`单人` ⇄ `多人`，同一时刻只激活一种；切换即切换整页内容区。模式选择持久化（新增设置项）。
- **单人模式内容**：
  - 游玩控制条：开始/暂停自动游玩、单步、当前状态（屏幕/最近动作/Token 三 metric 沿用）。
  - **对话区**（从 chat tab 并入）：对话流中直接渲染模型决策信息与操作结果（LLM 工具调用、act 结果、回复文本）；提供「显示思考内容」开关（默认关）。
  - 对话输入框 + 发送 + 清空 + 「附带当前状态」「附带截图」「允许代打」三个勾选（沿用 chat footer 能力）。
  - 对话内可直接发消息影响决策、暂停/继续游玩。
- **多人模式内容**（从 dual tab 并入）：
  - 邀请 AI 队友 / 继续上次联机对局 / 暂停队友 / 继续游玩 / 禁用自动选角开关。
  - 组队状态卡（session headline/detail/next、队友实况 chip）。
  - 队伍交流（队友聊天记录 + 输入 + 发送）。
- **决策日志区**（从 decisions tab 并入）：用量 tile（本次会话/本局）+ 决策记录列表（最新在前）。可作为游玩页内的一个可折叠分区。
- **Jev 面板**：当当前模式的双层决策开关开启时，在对话区下方展示 Jev 输出（所选动作、置信度、概率分布、延迟）；开关关闭时隐藏。面板的数据挂点由子任务2提供，本任务只建 UI 容器与空态。
- **双层决策开关**：游玩页按当前模式显示对应开关（单人→`DualLayerSoloEnabled`，多人→`DualLayerCoopEnabled`），开关状态持久化；Jev 未配置时开关旁给出「去设置配置 Jev」提示。

### 设置页（settings）
- 保留现有：首次配置引导、外观（主题）、端点卡、模型卡、角色绑定、高级选项（热键/预算/主动发言/窗口位置）。
- 新增 **Jev 分区**：BaseUrl（默认 `https://api.typesafe.ai`）、API Key（secret）、Model（默认 `jev-latest`）、置信度阈值、「测试连接」按钮。分区锚点加入跳转栏。
- 新增 **接入子页**（从 connect tab 并入）：MCP 服务开关、状态、地址/配置复制、说明文案。以设置页内的子分区或子选项页形式呈现。

### 移除
- `chat` / `dual` / `connect` / `decisions` 四个顶级 tab 及其 catalog 条目、page builder arm 移除；其内容按上文并入 play / settings。

## Acceptance Criteria

- [ ] 覆盖层只有 游玩 / 设置 两个 tab；`OverlayTabCatalog.Tabs` 长度为 2。
- [ ] 游玩页顶部模式滑块切换单人/多人内容区，选择持久化，重启 overlay 后保持。
- [ ] 单人模式下：开始/暂停/单步可用；对话区能收发消息、展示决策与操作结果、可开关思考内容显示。
- [ ] 多人模式下：邀请/继续/暂停/恢复队友、队伍交流均可用（功能不回归）。
- [ ] 决策日志与用量在游玩页内可见且随 tick 刷新。
- [ ] 双层开关按模式独立持久化；Jev 未配置时有明确引导。
- [ ] 设置页含 Jev 分区与接入子页；MCP 开关/地址复制功能不回归。
- [ ] `OverlayTabContractTests`、`LocalizationTests`（含 `CatalogLabelsResolveToEnglish`）、`SourceShapeContractTests` 全部通过；被删 tab 的契约断言同步更新。
- [ ] 所有新 UI 文案走 `Loc.T` 且在 `Loc.Strings.Ui.cs` 有英文条目。
- [ ] 新设置项进入 `AgentSettings` + `SettingsClone` + `EnsureValidShape` + harvest 链路。

## Notes

- 技术约束详见 UI 摸底报告：tab 三处同步、440px 面板宽、动态文本必须 `UiFactory.Wrapped`、单一 `RefreshDynamic` + tick 兜底、文件行数 ratchet（新页面 builder 放 Pages.cs 或新 partial，不回基文件）。
- `CoopRouteTests.cs:168` 钉了 tick 里的 `IsTabVisible(OverlayTabCatalog.Dual)`，tab id 变更需同步。
- 本任务只做 UI 与设置挂点；Jev 引擎与 planner 工具在子任务2。
