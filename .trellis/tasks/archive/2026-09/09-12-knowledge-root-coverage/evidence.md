# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `mcp_server/src/sts2_mcp/knowledge.py` | `_repo_root()` → `Path \| None`（env 优先 → 向上找 `mcp_server/pyproject.toml` → 找不到**明确返回 None**，删除 `parents[3]` 猜测）；`_default_knowledge_root()` 无仓库根时回退 `Path.cwd()/agent_knowledge` 并用 `logging` 打 WARNING（不污染 stdio）；新增 `_reference_files()`，无仓库根时返回 `[]` |
| `mcp_server/tests/test_knowledge.py`（新） | 28 条用例 |
| `mcp_server/tests/test_handoff.py`（新） | 11 条用例 |
| `mcp_server/README.md` | 环境变量表补 `STS2_AGENT_REPO_ROOT` / `STS2_AGENT_KNOWLEDGE_DIR` |

## 验收

| 标准 | 证据 |
|---|---|
| 无 `parents[3]` 兜底 | `rg -n "parents\[3\]" knowledge.py` → 无输出（exit 1） |
| 根解析矩阵 | `RepoRootResolutionTests` 6 条：env 优先 / marker 命中 / 都无 → `None` / `KNOWLEDGE_DIR` 优先 / 检出态默认 / 安装态回退 cwd 且 `assertFalse(root.is_relative_to(install_dir))` + `assertLogs(...WARNING)` |
| `reference_files` 无根时空 | `ReferenceFileTests` 3 条（planner/combat `== []`，有根时指向 `docs/game-knowledge/*.md`） |
| 规定覆盖面 | combat key 归一化（`Cultist`+`cultist` → `cultist_x2`）、section 追加幂等与 `##` 边界、战斗/事件创建→追加→`to_payload()`、空 `event_id`/`summary`/`note` 的 `ValueError`、handoff 报文（`handoff_type`/`reset_context`/instructions 5 条/`knowledge_updates` 递增） |
| 只写 tempfile | 全部 `Sts2KnowledgeBase(root_dir=<tempdir>)`；`git status --porcelain agent_knowledge` 无输出，目录仍只有 `run_logs/` |

## 门禁

`cd mcp_server; uv run --locked python -m unittest discover -s tests` → **159 tests OK**（本任务 +39）。

## 已知偏离

- 顺带在 `_append_section_line` 加了最小去重（完全相同的 bullet 行不重复追加）；真实调用都带 UTC 时间戳，正常行为不变。
- design 里"去重排序的 `enemy_ids`"未改：`handoff.py` 的 `enemy_ids` 一直是原样透传（受"不改对外报文结构"约束），去重由 `combat_key` 与知识文件路径保证。
