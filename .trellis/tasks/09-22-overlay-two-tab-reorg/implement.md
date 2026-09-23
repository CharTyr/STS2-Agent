# 实施计划：UI 重组（游玩/设置双 tab 与模式滑块）

## 前置

- 分支：从 `dev` 切功能分支 `feature/overlay-two-tab-reorg`。
- 读 `.trellis/spec/mod/agent-and-ui.md` 与 `architecture.md` 的 UI/设置段（已读）。
- 构建前关闭游戏（DLL 锁）。

## 步骤

### 1. 设置项与持久化（Config 层，先行）
- [x] `AgentSettings.cs` 加 8 个字段（OverlayPlayMode/ShowThinkingInChat/DualLayerSoloEnabled/DualLayerCoopEnabled/JevBaseUrl/JevApiKey/JevModel/JevConfidenceThreshold）。
- [x] `SettingsClone.cs` 拷贝清单同步。
- [x] `AgentSettings.EnsureValidShape` 归一化新字段。
- [x] 单测：新字段 round-trip、clone 覆盖、threshold 钳制（`Settings.DualLayerFieldsNormalized` + 扩展 `SettingsClone.CarriesEditableFields`）。

### 2. Tab 目录收缩
- [x] `OverlayTabCatalog.cs`：删 dual/chat/connect/decisions，留 play(游玩,RefreshOnShow=true)/settings(设置)。
- [x] `AgentOverlayHost.Tabs.cs`：`BuildPageForTab` 收缩为两条 arm；移除 `_chatFooter.Visible` 控制。
- [x] `AgentOverlayHost.cs`：默认 tab 逻辑改为 首运行→settings 否则→play；`ShowTab` 调用点保留在基文件；tick 合并到 Play 可见性下。

### 3. UiFactory 新控件
- [x] `SegmentedSwitch`（模式滑块，`Ui/SegmentedSwitch.cs`）：互斥 pill，选中实心，回调 onSelect(index)，`SetSelected` 供刷新重绘。
- [x] 确认 `RepaintForPalette` 覆盖新控件类型（段按钮复用 TabButton=Button，走现有 Button arm；容器自身无样式）。

### 4. 游玩页重写（BuildPlayPage）
- [x] 顶部模式滑块 + 内容区容器（`_soloSection`/`_coopSection` 互斥可见）。
- [x] 单人区：迁移现 play 页控制条 + metric；并入对话 card（chat log + 输入 + 勾选 + 发送/清空）+「显示思考内容」勾选。
- [x] 多人区：迁移现 dual 页全部内容（`BuildCoopSection`，不再自带 scroll）。
- [x] Jev 面板 card：容器 + 空态（数据挂点留给子任务2）。
- [x] 决策日志 card：迁移 decisions 页内容（`BuildDecisionCard`），RefreshDecisionPage 逻辑留在 RefreshDynamic。
- [x] 双层开关：单人区绑 `DualLayerSoloEnabled`、多人区绑 `DualLayerCoopEnabled`。
- [x] 页面 builder 留在 Pages.cs（移动 BuildConnectSection 到 Settings.cs 后回落到 861 行，预算内）。

### 5. 设置页扩展
- [x] 跳转栏加 Jev、接入锚点。
- [x] Jev 分区表单（BaseUrl/ApiKey/Model/阈值/测试按钮 → `AgentRuntime.TestJevConnectionAsync` 配置校验桩，真实 ping 留给子任务2）。
- [x] 接入子页：迁移 connect 页内容（`BuildConnectSection` 移入 Settings.cs）。
- [x] harvest 链路回写新字段（Jev 四字段；模式/思考/双层开关走各自 Toggled 即时保存）。

### 6. 本地化
- [x] 所有新中文文案加 `Loc.T` + 英文条目（UI 串入 `Loc.Strings.Ui.cs`，AgentRuntime 串入 `Loc.Strings.Runtime.cs`）。

### 7. 契约/测试更新
- [x] `OverlayTabContractTests` 改写（tab 数=2、顺序、归属、RefreshOnShow、决策日志 tick 改 Play）。
- [x] `CoopRouteTests.cs:168` 的 tick 断言同步（Dual→Play）。
- [x] `CatalogLabelsResolveToEnglish` 核对（两 tab 标签均有英文）。
- [x] `OverlayLayoutContractTests` 同步（BuildDualPage→BuildCoopSection、BuildChatFooter→BuildChatCard、BuildDecisionPage→BuildDecisionCard、_mcpStatus 归属 Pages→Settings、CoopChatBox 改为「不再自带 scroll」）。
- [x] 设置相关测试补新字段（见步骤1）。
- [x] `Router.cs`/`NativeMcpServer.cs` 的 "Connect tab" 错误文案改为指向设置页 Connect 分区。

## 验证

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj   # C# 契约/单测
powershell -ExecutionPolicy Bypass -File "scripts/build-mod.ps1" -Configuration Release   # 构建（先关游戏）
```

- [x] C# 测试全绿（0 失败）。
- [x] `dotnet build STS2AIAgent.csproj` 0 警告 0 错误。
- [x] MCP Python 测试全绿（416 通过）。
- [ ] Release 构建 + DLL/PCK 部署（需先关游戏）。
- [ ] 实机：overlay 只有两 tab；模式滑块切换；单人/多人功能不回归；设置页 Jev 分区与接入子页可用。

## 回滚点

- 步骤 1 独立可回滚（纯设置字段，向后兼容）。
- 步骤 2-7 为一个整体 UI 变更，回滚 = revert 对应提交。

## 评审闸

- 步骤 1 后：设置层自测通过再继续。
- 步骤 7 后：trellis-check 全量。
