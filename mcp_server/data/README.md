# 游戏数据文件夹

## 来源仓库
https://github.com/ptrlrd/spire-codex

## 定位：打包快照，不是运行时数据源

本目录（`eng/*.json`，20 个文件，约 1.2 MiB）是**某一版本游戏的打包快照**，
随 wheel/sdist 一起分发。三件事必须说清楚：

1. **它是快照**：这些 JSON 是构建期的静态副本，随包发布
   （`pyproject.toml` 的 force-include、`scripts/package-release.ps1`、
   `scripts/check_release_package.py`）。
2. **运行时唯一数据源是 Mod**：MCP 工具读取的游戏元数据全部来自 Mod 的
   `GET /data/{collection}`（见 `src/sts2_mcp/client.py` 的
   `get_game_data_collection`）。当前代码库中没有任何路径读取本目录的 JSON 文件。
3. **schema 与导出 schema 不一致**：本快照来自 spire-codex，字段集与 Mod 导出的字段集
   不同。例如 `potions.json` 没有 `usage` / `target_type`，
   `relics.json` 没有 `is_melted`，`cards.json` 则多出
   `image_url` / `beta_image_url` 等导出中不存在的字段。
   **不要用本目录推断字段名或 schema**；字段的权威来源是
   `STS2AIAgent/Agent/GameDataExportSchema.cs` 与
   `GET /data/{collection}` 的实际返回。

## 内容
- 游戏元数据的 JSON 格式存储（当前仅保留英文 `eng`）
- 不参与 MCP 工具的数据查询与 AI 决策，仅作为历史快照保留

## 去留未决
本目录约 1.2 MiB 且无代码读取，**是否继续随包发布尚未决定**（保留或删除由维护者拍板）。
在决定之前，`tests/test_packaged_game_data.py` 只做浅校验
（文件可解析、条目 id 非空且唯一）；目录不存在时该测试自动跳过。

## 版本
1.0.3
