# STS2 AI Agent：当前状态页

> **当前发布 v0.19.6（2026-11-26）**。同一份发布 DLL 支持 Steam 正式版 `v0.111.1` 和公测版 `v0.114.0`，两版本均已通过隔离实机验收。[GitHub Release](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.19.6) · [Steam 工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050) · [发布记录](history/release-v0.19.6_2026-11-26.md) · [构建指纹索引](history/build-fingerprints.md)

早期路线图与历史交付记录已归档至 `history/`（[2026-11-26 状态页归档（v0.10.5–v0.16.2 / 10-27）](history/PRODUCT_PLAN_CURRENT_2026-11-26-archive.md)、[2026-10-26 归档](history/PRODUCT_PLAN_CURRENT_2026-10-26-archive.md)、[旧路线图](history/PRODUCT_ROADMAP_2026-09-05.md)、[旧计划](history/PRODUCT_PLAN_CURRENT_2026-09-07.md)、[旧联机交付](history/COOP_DELIVERY_2026-09-07.md)）。本页不继承历史归档中的旧审批、旧分支或旧测试约束。

## 1. 当前基线

### 已发布版本（v0.17.0 – v0.19.6）

详细变更见 [CHANGELOG.md](CHANGELOG.md)，跨渠道字节与 SHA-256 见 [history/build-fingerprints.md](history/build-fingerprints.md)。

| 版本 | 日期 | 核心交付 | 发布记录 |
| --- | --- | --- | --- |
| **v0.19.6** | 2026-11-26 | 同一份 Mod 兼容 Steam 正式版 `v0.111.1` 与 public beta `v0.114.0`（`GameApiCompatibility`、升级预览签名兼容、双版本实机打包门禁） | [release-v0.19.6](history/release-v0.19.6_2026-11-26.md) |
| v0.19.5 | 2026-11-22 | 愚者最后一张箭矢可用性与伤害重放修正、闪电球厄运优先级、Dread 轮换选角确认 | [release-v0.19.5](history/release-v0.19.5_2026-11-22.md) |
| v0.19.4 | 2026-11-20 | Windows HTTP.sys 错误 6 自动回落至回环 socket 传输；双传输均失败时保留单人悬浮窗 | [release-v0.19.4](history/release-v0.19.4_2026-11-20.md) |
| v0.19.3 | 2026-11-19 | 修复 `v0.113.1` 本地双开 `FastHost` 参数数量变化；外部 Agent 可经原生 MCP 驱动 AI 队友 | [release-v0.19.3](history/release-v0.19.3_2026-11-19.md) |
| v0.19.2 | 2026-11-17 | 双开队友恢复、本地上下文预算估算与截图链路加固 | [release-v0.19.2](history/release-v0.19.2_2026-11-17.md) |
| v0.19.1 | 2026-11-15 | 决策与 skill 流程收敛（精简游玩调用链） | [release-v0.19.1](history/release-v0.19.1_2026-11-15.md) |
| v0.19.0 | 2026-11-14 | 游戏 `v0.113.0` Dread & Blitz 适配（实机验证） | [release-v0.19.0](history/release-v0.19.0_2026-11-14.md) |
| v0.18.0 | 2026-11-09 | 体验大改造（compact v13、Turbo、Autopilot、整回合计划、战斗算术、HUD、战报、前缀缓存与节流） | [release-v0.18.0](history/release-v0.18.0_2026-11-09.md) |
| v0.17.0 | 2026-10-28 | 界面视觉与上手体验优化、快速判断多家服务与熔断、长选牌网格修复 | [release-v0.17.0](history/release-v0.17.0_2026-10-28.md) |

### 主线 `dev` 未发布变更

- 云主机 / Dev Container 开发环境套件（[docs/cloud-dev.md](docs/cloud-dev.md)、`.devcontainer/`、`scripts/*cloud-dev*` / `*game-assemblies*`）。
- 仓库卫生与可维护性整理：移出误跟踪的顶级 `bin/`、加固 `.gitignore` / `.gitattributes`、清零测试工程 `CS8613` 警告、归档已发布 Trellis 任务、刷新架构行数表与代码地图、补齐 Linux 离线 CI 校验。
- 五阶段 Mod 能力与工程升级（详见 [CHANGELOG.md](CHANGELOG.md) `Unreleased`）：多资源/充能球/`v0.114.0` `HitCount` 战斗数学与 Act 1 精英选路、降低 `TurnPlan` 误中断与扩展零权衡 `Autopilot`、离线 `sts2.dll` ABI/反射审计与 `/state` 按段容错、三档游玩预设/观战高亮停顿/跨局偏好/联机快捷战术指令/细化排障指引，以及 24 例决策基准与跨模型评测工具。

## 2. 验证体系与剩余边界

- **离线契约**：C# 核心单测（`STS2AIAgent.Tests`，1,192 项，含体量棘轮、反射注册表、离线 ABI 审计、动作面与代码地图新鲜度）、Python MCP 单元测试（`mcp_server/tests`，466 项，含原生/Python 工具双向对齐与脚本可移植性）、12 项静态验证闸门（`scripts/check_verification_gates.py`）。
- **发版双版本门禁**：`scripts/package-release.ps1` 与 `scripts/package-steam-workshop.ps1` 强制调用 `scripts/test-release-compatibility.ps1`，使用同一份最终 DLL 在最低支持版本与目标版本两个真实游戏副本上跑隔离冒烟。流程见 [docs/release-readiness.md](docs/release-readiness.md)。
- **已知后置与未覆盖边界**（不构成当前阻塞，后续按需补测，详情见 [docs/live-validation-checklist.md](docs/live-validation-checklist.md)）：
  1. 3–4 人真实在线联机局尚未实机测试。
  2. 双窗口联机叠加真实 Jev 快速判断服务的实机联调待补。
  3. 真实上游模型超长停流、真实模型在自然整局中的主动发言质量、以及完整浏览器页面 Origin 场景仍停留在离线/本机桩契约。
  4. Steam 创意工坊简体中文商店说明无法由 `ModUploader` 上传，每次更新后仍需在网页端手工核对粘贴 `steam-workshop/description.zh-CN.txt`。
  5. 英文界面文案仍建议做一次母语润色。

## 3. 后续维护规则

1. 只有 `PRODUCT_PLAN_CURRENT.md` 描述当前状态；`history/` 下的文件只作历史证据，不再发展为第二任务板。
2. 每项发布能力必须有可核对的 tag / Release / 实机证据记录，并登记到 `history/` 与 [history/build-fingerprints.md](history/build-fingerprints.md)。
3. 标签后提交单列在「主线 `dev` 未发布变更」，直到切出明确 release tag；玩家可见改动同步记入 [CHANGELOG.md](CHANGELOG.md) 的 `## Unreleased` 段。
4. 仓库架构入口见 [docs/project-map.md](docs/project-map.md)，开发与发版入口见 [docs/release-readiness.md](docs/release-readiness.md) 与 [docs/cloud-dev.md](docs/cloud-dev.md)。
5. README 入口持续指向本页；打包脚本依赖的 `./PRODUCT_PLAN_CURRENT.md` 相对目标字面保持不变。
6. 历史归档索引见 [history/README.md](history/README.md)；`docs/` 与根目录不再保留只剩跳转的空壳页。
