# 设计：不猜根目录 + 可注入测试

## 1. 根解析

```python
def _repo_root() -> Path | None:
    env = os.getenv("STS2_AGENT_REPO_ROOT")
    if env: return Path(env).expanduser().resolve()
    for parent in Path(__file__).resolve().parents:
        if (parent / "mcp_server" / "pyproject.toml").is_file(): return parent
    return None                      # 明确"不知道"，不再猜

def _default_knowledge_root() -> Path:
    env = os.getenv("STS2_AGENT_KNOWLEDGE_DIR")
    if env: return Path(env).expanduser().resolve()
    repo = _repo_root()
    if repo is not None: return repo / "agent_knowledge"
    logging.getLogger(__name__).warning(
        "STS2 knowledge root is not inside a repository checkout; using %s. "
        "Set STS2_AGENT_KNOWLEDGE_DIR to pin it.", fallback)
    return fallback                 # Path.cwd() / "agent_knowledge"
```

调用点全部随 `_repo_root()` 变可选而更新（`_default_knowledge_root`、
`build_planner_context` 与 `build_combat_context` 的 `reference_files` → `None` 时 `[]`）。
**禁止** `print`（stdio 传输协议）；用 `logging`。

## 2. `tests/test_knowledge.py`

全部 `Sts2KnowledgeBase(root_dir=<tempdir>)`；需要根解析本身时用 `patch.dict(os.environ)` +
`patch.object(knowledge, "__file__", ...)` 之类手段，或直接把 `_repo_root` 纳入测试
（可测性优先：可给 `_repo_root` 加一个可选 `start: Path | None = None` 参数用于注入起点）。

建议用例：

- `_combat_key([{"enemy_id":"Cultist"},{"enemy_id":"cultist"}]) == "cultist_x2"`；
  `_parse_combat_key_part("cultist_x3") == ("cultist", 3)`；
- `_append_section_line` 幂等（同内容重复追加不重复出现）、跨 `##` 边界行为；
- `resolve_combat_entry(create_if_missing=True)` 生成 `combat/global/solo/<key>.md`，
  追加后 `to_payload()["relative_path"]` 正确、内容含 `run_id=`/`floor=`/`screen=`；
- `resolve_event_entry_by_id("")` → `ValueError`；
- `_enumerate_paths` 分叉/环 防重。

## 3. `tests/test_handoff.py`

`Sts2HandoffService(Sts2KnowledgeBase(root_dir=tmp))`：

- planner 报文：`handoff_type == "planner"`、`reset_context is True`、instructions 条数为 5；
- combat 报文：无 combat 上下文 → `ValueError`；有 `combat.enemies` → `combat_key`/去重排序后的 `enemy_ids`；
- `complete_combat_handoff`：空 summary → `ValueError`；非空 → `knowledge_updates` 数量随可选 note 递增；
- `complete_event_handoff`：`option_index=1` 时写出的行含 `option_index=1`。

## 4. 文档

`mcp_server/README.md` 环境变量表补两行（`STS2_AGENT_REPO_ROOT`、`STS2_AGENT_KNOWLEDGE_DIR`），
并说明安装态默认落在当前工作目录的 `agent_knowledge/`。
