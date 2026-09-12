# 修好 knowledge 根目录解析，并给 knowledge / handoff 补测试

## 背景

`mcp_server/src/sts2_mcp/knowledge.py:12-30`：

```python
def _repo_root() -> Path:
    env = os.getenv("STS2_AGENT_REPO_ROOT")
    ...
    for parent in ...:                     # 找 mcp_server/pyproject.toml
        ...
    return current.parents[3]              # ← 兜底：纯猜测
```

wheel/pipx 安装态下 `__file__` 在 `<site-packages>/sts2_mcp/knowledge.py`，
往上找不到 `mcp_server/pyproject.toml`，于是 `parents[3]` 返回 **venv 或 Python 安装根**：

- `_default_knowledge_root()` → `<venv>/agent_knowledge`，写分支 `mkdir(parents=True, exist_ok=True)`
  会在 venv 里静默建目录（升级/重装即丢，且用户永远找不到）；
- `reference_files`（:334-339 / :366-371）指向 `<venv>/docs/game-knowledge/*.md`（不存在）。

`STS2_AGENT_REPO_ROOT` 全仓只出现在 knowledge.py:13，README 环境变量表也没有它。

同时 `knowledge.py`（约 540 行）与 `handoff.py`（约 210 行）**零测试**：
`rg -n 'knowledge|handoff' mcp_server/tests` 无命中。

## 目标

1. 去掉 `parents[3]` 猜测：找不到仓库标记时**不猜**，改用一个明确、可见、可写的回退目录，
   并在日志里说明（绝不写 stdout —— stdio 传输协议要干净）。
2. `reference_files` 在无仓库根时返回空列表，而不是指向 venv 里的假路径。
3. 给 `knowledge.py` 与 `handoff.py` 补离线测试（全部可注入 `root_dir` 或环境变量，
   绝不写仓库真实的 `agent_knowledge/`）。
4. README 环境变量表补 `STS2_AGENT_REPO_ROOT` / `STS2_AGENT_KNOWLEDGE_DIR`。

## 验收标准

- [x] `knowledge.py` 中不存在 `parents[3]` 这类无依据兜底。
- [x] 根解析矩阵有测试：env 覆盖优先 / 找到 marker 时用仓库根 / 都失败时回退到
      `Path.cwd()/agent_knowledge`（或 design 里确认的等价方案）且**不落在安装目录内**。
- [x] `reference_files` 在无仓库根时为空列表（有测试）。
- [x] 新测试覆盖：combat key 归一化与解析、section 追加幂等、战斗/事件条目生命周期
      （创建→追加→`to_payload()`）、空 `event_id`/`summary` 的 `ValueError`、
      handoff 报文结构（`handoff_type`/`reset_context`/instructions 条数/知识写入条数随可选参数递增）。
- [x] 测试只写 `tempfile` 目录；仓库 `agent_knowledge/` 保持只有 `run_logs/`。
- [x] `cd mcp_server; uv run --locked python -m unittest discover -s tests` 全绿。

## 范围外

- 不改知识文件的目录结构（`combat/global/{solo,groups}/<key>.md`、`events/global/<id>.md`）。
- 不改 `mcp_server/data/eng/` 的去留（用户决策项）。
- 不动 skill 侧 `agent_knowledge/run_logs/` 的约定。
