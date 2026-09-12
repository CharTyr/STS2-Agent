# 执行清单

1. 读 `GameActionService.cs` 的 :520-620（open_character_select / open_timeline）、
   :700-800（confirm_timeline_overlay）、:1600-1650（crystal_set_tool）、
   :2028-2130（select_deck_card）、:4924-5020（run_console_command）、
   :5600-5900（三个等待助手）；读 `GameStateService` 的 `TrySetCrystalSphereTool` 与
   `GetCrystalSphereMinigame` 实现，确认"可观测证据"具体是什么。
2. 先扩 `MenuTransitionPolicy`（纯策略）并补测试，再改等待助手。
3. 依次收口四个处理器；每处都留下"证据从哪来"的注释。
4. 补源码契约断言，自检可否证。
5. 验证：
   ```powershell
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   python scripts/check_verification_gates.py
   ```
6. 不提交、不推送；报告每个处理器的最终判定依据（观测点 + 失败时的返回）。对无法离线证明的
   部分（真实浮层可达性）明确标注"需实机验证"，不要写成已验证。
