# 执行清单

1. 读 `mcp_server/src/sts2_mcp/knowledge.py`（全文，约 540 行）与 `handoff.py`（约 210 行），
   确认每个 `_repo_root()` / `_default_knowledge_root()` 调用点。
2. 按 design 改根解析；`reference_files` 无仓库根时返回 `[]`。
3. 写 `tests/test_knowledge.py`、`tests/test_handoff.py`（stdlib `unittest`，`tempfile` 注入）。
4. 补 README 环境变量表。
5. 验证：
   ```bash
   cd mcp_server
   uv run --locked python -m unittest discover -s tests -v
   ```
   并确认 `git status` 里**没有** `agent_knowledge/` 下的新增文件。
6. 不提交、不推送；报告新增文件、测试计数与"没写进仓库"的证据。
