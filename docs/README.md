# 文档目录

当前状态只看根目录的 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。旧规划、阶段快照、发布记录和实机截图证据都在 [history/](../history/README.md)，不代表当前状态。

## 玩家

- [setup.md](setup.md)：环境与运行准备
- [troubleshooting.md](troubleshooting.md)：常见问题
- [model-compatibility-matrix.md](model-compatibility-matrix.md)：OpenAI 兼容模型的兼容矩阵

## 接入方（HTTP API / MCP）

- [api.md](api.md)：HTTP API 参考，由 CI 与代码核对
- [openapi.json](openapi.json)：由脚本生成，勿手改
- [adr/](adr/)：架构决策记录

## 开发者

- [build-and-env.md](build-and-env.md)：构建、部署与环境变量
- [project-map.md](project-map.md)：代码地图
- [reverse-engineering.md](reverse-engineering.md)：逆向工程笔记
- [mechanic-coverage-matrix.md](mechanic-coverage-matrix.md)：机制覆盖矩阵（历史快照）
- [proactive-chat-review.md](proactive-chat-review.md)：主动发言实现评审
- [release-readiness.md](release-readiness.md)：发布验收入口

## 验证与评测

- [live-validation-checklist.md](live-validation-checklist.md)：实机验证清单
- [validation-severe-usability-2026-09-23.md](validation-severe-usability-2026-09-23.md)、[live-validation-2026-09-23-results.md](live-validation-2026-09-23-results.md)：2026-09-23 的验证计划与结果（历史快照）
- [phase-5-full-chain.md](phase-5-full-chain.md)、[phase-6-validation-template.md](phase-6-validation-template.md)：全链路说明与验证记录模板
- [decision-benchmark.md](decision-benchmark.md)：决策质量离线评测集

## 游戏知识库

- [game-knowledge/](game-knowledge/README.md)：卡牌、遗物、怪物、事件等参考资料

## 其他

根目录 `package.json` 只声明了一个开发期工具依赖（`@sammysnake/fast-context-mcp`），不参与构建与发布。
