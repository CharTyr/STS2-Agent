# 证据：场景字段集 vs 真实导出

## 两条独立消费链（走的是不同的表）

| 链 | 入口 | 字段表 | 数据来源 |
|---|---|---|---|
| 游戏内 Agent + 原生 MCP | `GameBridge.cs:130-142` → `GameDataFilter.ProjectRelevant` | C# `SceneFieldSets`（:12-30） | 游戏进程内 ModelDb |
| 外部 MCP（默认） | `server.py:758 get_relevant_game_data` → `_SCENE_FIELD_SETS`（server.py:150-219） | Python 表 | HTTP `GET /data/{collection}`（`Router.cs:196-212`，返回整集） |

Python 表逐字段核对后 **(a) 侧为空**（没有不存在的字段名）：combat/monsters 用的是
`min_hp,max_hp,moves,damage_values,block_values`，shop/potions 不请求 `target`。
本轮 bug 只在 C# 链。

## 静默性

C# `ProjectFields`（:125-137）与 Python `server.py:366` 都是
`if (key in item)` 才写入，缺字段不报错、不告警；错误分支只覆盖未知 collection 与数据不可用。

## 现有测试为何没抓住

- `mcp_server/tests/test_game_data_tools.py:114-172` 断言值来自
  `",".join(_SCENE_FIELD_SETS[...])` —— **自引用**，改表即跟改。
- `STS2AIAgent.Tests/AgentLoopTests.cs:8-32` 只断言 combat/cards 保留 `name`、丢弃 `flavor`。
- 两侧都没有 monsters / potions / relics 与真实导出 schema 的断言。

## 静态数据集不能当 schema

`mcp_server/data/eng/*.json`（20 文件，仅被打包、无代码读取）与运行时导出不一致：
`monsters.json` 含 `min_hp_ascension/max_hp_ascension/image_url`，
`potions.json` **没有** `usage/target_type`。不可作为字段依据。

## 已知缺口

`server.py` 的 `COMBAT_SCREEN_KEYWORDS` 等常量与 C# `DetectScene`（GameDataFilter.cs:32-51）
是否等价未逐条比对（超出本轮字段差集范围）。
