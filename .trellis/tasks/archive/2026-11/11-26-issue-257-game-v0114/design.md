# 兼容设计

- 在 `GameStateService.Combat.cs` 保持原始状态构建入口和现有 DTO。紧凑视图继续投影同一快照。
- v0.113.1 的原生旋律牌计数从 `CombatManager.Instance.History.CardPlaysFinished` 读取，按 `HappenedThisTurn`、出牌玩家与任一 Wah/Cho/Ki 标签筛选。两代游戏都提供这些 API。在 Mod 侧重现这条计数语义。三种标签状态和不同旋律标签数继续使用仍存在的原生方法。
- 以 `GameApiCompatibility` 读取正式版和测试版间有差异的公开游戏 API。正式版缺少愚者与测试版专属模式。测试版的箭矢保留 `Status` 与 `IsAvailable`。沿用旧布尔损毁字段并返回 `false`。
- 卡牌升级预览缓存匹配已加载游戏的公开方法。v0.114.0 调用 `UpgradeInternal(bool)`、传入默认值 `false`，v0.111.1 调用 `UpgradeInternal()`。两种调用都使用分离的可变副本。签名缺失时返回 `upgrade_preview_failed`，同一个解析结果使健康检查降级并记录启动日志。
- 对发布 DLL 的 ABI 验证使用实际安装程序集。对照 issue 抽样，验证编译器能重绑定、旧 DLL 仍可能失败的调用，然后在新版本上启动同一轮验证。根据审计与测试结果选定可投放的编译产物。
- 修改发布前记录放入 `CHANGELOG.md` 的 `Unreleased`。状态字段的版本语义同步到 API 文档与项目地图。
- GitHub 与 Workshop 的打包脚本在复制 Mod 后、生成 ZIP 或 VDF 前执行同一门禁。门禁从发布目录读取最低版本、校验两个游戏目录、关闭 Steam 并启动隔离游戏，按实际 PID 和游戏版本报告两版的行为证据。ZIP 二次核对已测试的 DLL 哈希。
