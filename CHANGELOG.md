# 更新日志

本文档记录 `STS2 AI Agent` 的重要发布变更。

## v0.6.1 - 2026-04-25

### 中文更新日志
- 新增 Mod `/data/*` 实时元数据导出接口，覆盖卡牌、遗物、敌人、药水、事件、能力与角色数据。
- MCP Server 改为通过 Mod API 按 collection 懒加载元数据，并在进程内缓存，避免运行时读取本地 fallback JSON。
- 修复游戏数据工具的错误处理与索引逻辑，未知 collection 和数据不可用时会返回结构化错误。
- 补充并通过相关单元测试、工具 profile 校验、Mod 加载验证、状态不变量验证与实机联调验证。

### 兼容性说明
- 已适配当前验证通过的最新游戏版本：`v0.103.2`。

### 完整变更
- [Full Changelog](https://github.com/CharTyr/STS2-Agent/compare/v0.6.0...v0.6.1)

