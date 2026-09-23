# 设计：UI 重组（游玩/设置双 tab 与模式滑块）

## 现状（来自 UI 摸底报告，行号对应当前工作区）

- 6 tab：`dual`(AI队友,RefreshOnShow) / `chat`(对话) / `play`(游玩) / `settings`(设置) / `connect`(接入) / `decisions`(决策日志,RefreshOnShow)，定义在 `Ui/OverlayTabCatalog.cs:39-47`，分派在 `Ui/AgentOverlayHost.Tabs.cs:51-64`。
- 页面 builder 在 `Ui/AgentOverlayHost.Pages.cs`（chat:43 / play:99 / dual:167 / connect:282 / decisions:323）与 `Ui/AgentOverlayHost.Settings.cs:62`。
- 全局 chrome：header（状态 chip、API 行、隐藏按钮、拖动）、边缘「AI」按钮、chat footer（Pages.cs:66-88，仅 chat tab 可见，Tabs.cs:88 控制）。
- 刷新：单一 `RefreshDynamic`（Pages.cs:487-724）+ 800ms tick 对当前可见 tab 定向刷新（AgentOverlayHost.cs:824-888）+ `RefreshesOnShow`。
- 契约：`OverlayTabContractTests` 钉 tab 数=6、顺序、catalog↔builder 对应、builder 归属文件、`ShowTab("dual"/"play"/"settings")` 调用点在基文件；`CoopRouteTests.cs:168` 钉 tick 里 `IsTabVisible(OverlayTabCatalog.Dual)`；`CatalogLabelsResolveToEnglish` 钉 tab 标签英文条目。

## 目标结构

### OverlayTabCatalog（改为 2 项）

| id | 中文 label | RefreshOnShow | 说明 |
|---|---|---|---|
| `play` | 游玩 | true | 模式滑块 + 对话 + 决策日志 + Jev 面板（内容随无事件来源的状态变化，需入场重读） |
| `settings` | 设置 | false | 现有设置 + Jev 分区 + 接入子页 |

- 删除 `dual` / `chat` / `connect` / `decisions` 四个 id 常量与 catalog 条目。
- `BuildPageForTab` 收缩为两条 arm：`play => BuildPlayPage()`、`settings => BuildSettingsPage()`。
- 契约测试同步改写：tab 数=2、新顺序、`ShowTab` 调用点、builder 归属。

### 游玩页（BuildPlayPage 重写）

布局（440px 面板，纵向 Scroll）：

1. **模式滑块**：`UiFactory` 新增 `SegmentedSwitch(options, selectedIndex, onSelect)`（两个 pill 按钮互斥，选中态实心，复用 TabButton 样式）。选中持久化到 `AgentSettings.OverlayPlayMode`（`"solo"`/`"coop"`，默认 `"solo"`）。
2. **单人区**（`OverlayPlayMode == "solo"` 时可见）：
   - 控制条 card：开始/暂停 `_playToggle`、单步 `_stepButton`、三 metric tile（屏幕/最近动作/Token）——沿用现 play 页实现（Pages.cs:99-133）。
   - 对话 card：`_chatLog`（Rich，ScrollFollowing）+ 输入 + 发送/清空 + 三勾选（附带状态/截图/允许代打）——从 chat 页与 chat footer 并入；新增「显示思考内容」勾选 `_showThinking`（持久化 `AgentSettings.ShowThinkingInChat`，默认 false），开启时把 `AgentTurnResult.Reasoning` 渲染进对话流。
   - 决策渲染：把每次 act 的工具调用与结果以对话气泡形式追加进 `_chatLog`（来源标记 `agent_loop`/`jev`），使「对话即决策流」。
3. **多人区**（`OverlayPlayMode == "coop"` 时可见）：现 dual 页内容整体迁入（邀请/继续/暂停/恢复/自动选角/组队状态/队伍交流，Pages.cs:167-261）。
4. **Jev 面板 card**（当前模式双层开关开启时可见）：`_jevStatus`（连接/配置状态）、`_jevLastChoice`（所选动作 + 置信度 + 延迟）、`_jevProbabilities`（概率分布，Wrapped/Rich）。数据来自 `AgentRuntime` 暴露的最近 Jev 决策快照（子任务2提供 `AgentRuntime.LastJevDecision` 之类的只读属性；本任务先建容器与「未开启双层决策」空态）。
5. **决策日志 card**（可折叠）：用量两 tile + `_decisionLog` 列表——从 decisions 页迁入（Pages.cs:323-412 的 RefreshDecisionPage 逻辑并入 RefreshDynamic）。

### 设置页（BuildSettingsPage 扩展）

- 跳转栏加分区锚点：`Jev`、`接入`。
- **Jev 分区**：`JevBaseUrl`（Line，默认 `https://api.typesafe.ai`）、`JevApiKey`（Line secret）、`JevModel`（Line，默认 `jev-latest`）、`JevConfidenceThreshold`（Line，double 0-1，默认 0.35，harvest 时校验范围）、「测试 Jev 连接」按钮（调 `AgentRuntime.TestJevConnectionAsync`，子任务2提供；本任务可先挂占位禁用态）。
- **接入子页**：现 connect 页内容（Pages.cs:282-318）整体迁入为设置页内一个 card 组：MCP 开关 `_mcpToggle`、状态 `_mcpStatus`、地址/配置复制（仅 McpRunning 可见）。

### chat footer 的处理

现 chat footer 是全局 chrome、仅 chat tab 可见（Tabs.cs:88）。chat tab 删除后，footer 的输入区并入游玩页对话 card 内部（不再用全局 footer），`_chatFooter.Visible` 逻辑移除。

## 设置项新增（AgentSettings）

| 字段 | 类型 | 默认 | 用途 |
|---|---|---|---|
| `OverlayPlayMode` | string | `"solo"` | 游玩页模式滑块 |
| `ShowThinkingInChat` | bool | false | 对话是否渲染思考内容 |
| `DualLayerSoloEnabled` | bool | false | 单人双层开关（子任务2消费） |
| `DualLayerCoopEnabled` | bool | false | 多人双层开关（子任务2消费） |
| `JevBaseUrl` | string | `https://api.typesafe.ai` | Jev endpoint |
| `JevApiKey` | string | `""` | Jev 密钥（secret） |
| `JevModel` | string | `jev-latest` | Jev 模型 |
| `JevConfidenceThreshold` | double | 0.35 | 置信度门控阈值 |

每个字段都要：`SettingsClone.Clone` 拷贝 + `EnsureValidShape` 归一化（threshold 钳到 0..1、model 非空、url 去尾斜杠）+ harvest 回写 + Watch 标记 dirty。

## 契约/闸门同步清单

- `OverlayTabContractTests`：tab 数 6→2、顺序、catalog↔builder、`ShowTab` 调用点、builder 归属文件。
- `CatalogLabelsResolveToEnglish`：新标签「游玩」「设置」的英文条目（沿用既有即可，确认存在）。
- `CoopRouteTests.cs:168`：tick 里 `IsTabVisible(OverlayTabCatalog.Dual)` 改为对 play 页多人区的等价判断（或按新 id）。
- `LocalizationTests.EveryCallSiteHasAnEnglishEntry`：所有新 `Loc.T` 中文文案补英文。
- `SourceShapeContractTests`：新文件默认 1000 行预算；游玩页 builder 若超，拆 `AgentOverlayHost.PlayPage.cs` 新 partial（设置页是先例）。
- `SettingsExperienceRegressionTests` / `PlayerExperienceTests`：新设置项的 round-trip 与克隆覆盖。

## 兼容与回滚

- 纯 UI/设置改动，不动 API 与决策链路；旧设置文件无新字段时 `EnsureValidShape` 补默认，向后兼容。
- 回滚 = revert 本任务提交；无持久化格式破坏性变更（新增字段可空/有默认）。

## 明确不做（避免范围蔓延）

- 不实现 Jev 引擎与 planner（子任务2）。
- 不实现对话 session 持久化到磁盘（子任务3）；本任务只把对话 UI 并入游玩页。
- 不改 MCP 工具面。
