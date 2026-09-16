# ADR 0001：动作面只保留一份判断

状态：**已接受，未实施**（已用契约测试锁住，等一次能连实机的会话来做）
日期：2026-09-16

## 背景

`/state` 的 `available_actions` 由 `GameStateService.BuildAvailableActionNames` 产出；
`/actions/available` 的描述符由 `GameStateService.BuildAvailableActionsPayload` 产出。

两者是**同一个判断的两份手写实现**。2026-09-16 对源码的机械比对：

| | 行数 | 查询的 `Can*` 谓词 | 产出的动作名 |
| --- | --- | --- | --- |
| `BuildAvailableActionNames` | 301 | 50 | 55 |
| `BuildAvailableActionsPayload` | 609 | 50 | 55 |
| 交集 | — | **50（完全相同）** | **55（完全相同）** |

集合完全一致，只有发射顺序在第 28 项之后开始不同。也就是说：**910 行代码在回答同一个问题**，
而它们今天一致纯属人工维护得当，没有任何机制保证。

代价是具体的：

- 新增一个动作要改两处，顺序、谓词、参数都要对上。`AGENTS.md` 与
  `.trellis/spec/mod/game-actions.md` 都把「两处都要加」写成了步骤——**文档在教人维护重复**。
- 已经为此写过一次局部契约：`CombatGate.ActionsAskTheSharedGate` 存在的唯一理由，就是防止这两个
  方法在三个战斗谓词上走岔。那是在给重复打补丁，不是在消除重复。
- 一旦走岔，后果是客户端在一个端点上被告知某动作可用、在另一个端点上不可用。这正是 v0.12.4
  被迫同号重发的那类缺陷（当时是同一份响应内部自相矛盾）。

## 决定

把这个判断收敛成一处：一个按屏幕产出「动作 + 参数要求」序列的方法，
`available_actions` 取它的名字投影，`/actions/available` 取它的完整描述符。

## 为什么还没做

这 910 行决定了 agent 被允许做什么。改错了不是某个功能坏掉，而是**整个 agent 失去正确的动作面**，
而这个项目的实机验证只能在装了游戏的机器上人工进行——没有自动化实机回归。

在只有离线证据的情况下重写它，就是在重复 v0.12.4 的错误：那次也是一次「显然正确」的重构，
离线 408 条测试与九道闸门全绿，结果主菜单上每个 `/state` 都 500。

所以这件事的门槛不是「想清楚」，是**能连实机跑一遍**。

## 在那之前做了什么

`ActionSurface.SameActionsOnBothSurfaces` 与 `ActionSurface.SamePredicatesOnBothSurfaces`
两条源码契约把两个表面的动作名集合与谓词集合钉成相等。它**不消除重复，只是让重复变响**：
只给一个表面加动作，测试立刻转红并点名被遗漏的那个。

发射顺序**故意不比对**。两者今天就不同，也没有客户端依赖它（skill 与 `state-invariants`
都只判断成员资格）。把顺序钉死只会把一个巧合记录成契约。

## 实施时的做法

1. 先在实机上抓一份基线：逐屏（战斗 / 地图 / 商店 / 休息 / 事件 / 宝箱 / 选角 / 大厅 / 结算 /
   暂停页 / 时间线）记录 `available_actions` 与 `/actions/available` 的完整内容。
2. 抽出统一的枚举方法，两个表面都改为读它。
3. 在同一台机器上重放第 1 步，逐屏比对——集合必须一致；顺序变化要显式确认没有客户端依赖。
4. `ActionSurface.*` 两条契约随之改写成「两个表面出自同一个来源」，而不是「两份实现保持一致」。
5. `AGENTS.md` 与 `.trellis/spec/mod/game-actions.md` 的「新增动作」步骤从两处改成一处。

## 相关

- 体量棘轮：`STS2AIAgent.Tests/SourceShapeContractTests.cs`（两个文件占了本 mod C# 代码的 49%）
- 架构债记录：`.trellis/spec/mod/architecture.md` 的「Code shape and its known debts」一节
