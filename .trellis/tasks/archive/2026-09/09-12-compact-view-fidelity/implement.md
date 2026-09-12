# 执行清单

1. 读 `GameStateService.cs:2973-3040`（compact 顶层）、`:3110-3168`（combat）、`:3190-3230`（run）、
   `:3480-3510`（modal / chest）、`:3540-3690`（卡牌 compact 构造器）、`:4722-4736`（unlock）、
   `:7580-7660`（raw 的 powers / players 摘要）、`:7750-7790`（raw 的 intents）。
   以**真实字段名**为准，不要照抄本文件的示例。
2. 按 design 落点一 / 二 / 三依次增量添加字段；不改既有字段（向后兼容）。
3. 新建 `STS2AIAgent.Tests/CompactViewFidelityTests.cs`，实现 4 个静态方法
   （`PowersReachTheCompactCombatView`、`IntentNumbersReachTheCompactCombatView`、
   `CardAndRelicIdsReachTheCompactViews`、`OverlayAndPartyReachTheCompactView`），
   全部用源码契约 + raw 侧同名对照。**不要改 TestRunner.cs / csproj**。
4. 自检可否证：逐个删字段跑测试看变红，再还原。
5. 同步文档：`docs/api.md` 里 agent_view 相关段落、`skills/sts2-mcp-player/SKILL.md` 里引用这些字段的段落
   （不动两个 SHARED PLAY CONTRACT marker）。
6. 验证：
   ```powershell
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   cd mcp_server; uv run --locked python -m unittest discover -s tests
   python scripts/check_verification_gates.py
   ```
7. 不提交、不推送；报告改动文件、字段清单（新增键 → 数据来源行号）、每条验收证据、以及
   "只能实机验证"的项（真实战斗里这些字段是否非空、数值是否正确）。
