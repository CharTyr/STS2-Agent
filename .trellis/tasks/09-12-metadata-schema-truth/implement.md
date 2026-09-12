# 执行清单

1. 读 `STS2AIAgent/Agent/GameDataFilter.cs`、`mcp_server/src/sts2_mcp/server.py:140-230`、
   `mcp_server/tests/test_game_data_tools.py:100-270`、
   `mcp_server/tests/test_legacy_action_coverage.py:15-35`（仓库根探测范式）、
   `STS2AIAgent/Agent/GameDataExportSchema.cs`、`mcp_server/data/README.md`。
2. 先写 `test_scene_field_alignment.py` 并跑出**红色**（记录每个差集）。
3. 按差集修 `server.py` 的 `_SCENE_FIELD_SETS`，直到测试绿。
4. 改 `test_game_data_tools.py` 的自引用断言为独立期望值。
5. 改 `mcp_server/data/README.md`，加 `test_packaged_game_data.py`。
6. 反证自检：只在 C# 加一个字段 → 红；只在 Python 加一个字段 → 红；删一个 JSON 的 id → 红；还原。
7. 验证：
   ```bash
   cd mcp_server; uv run --locked python -m unittest discover -s tests
   python scripts/check_verification_gates.py
   python scripts/check_release_package.py --source-root .
   ```
8. 不提交、不推送；报告改动文件、差集修复清单（前后对比）、每条验收证据、以及
   "data/eng 去留待用户拍板"的现状说明。
