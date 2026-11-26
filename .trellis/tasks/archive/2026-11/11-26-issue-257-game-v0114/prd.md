# 修复 STS2 v0.114.0 状态接口兼容问题

## 目标

让游戏 v0.114.0 的玩家能通过状态 HTTP 接口和 MCP 读取状态并继续使用现有操作。[GitHub issue #257](https://github.com/CharTyr/STS2-Agent/issues/257)

## 已确认

- 当前修复工作树基于 `origin/dev` 的 v0.19.5。Steam 本机安装的游戏为 v0.114.0（`a7b9a9c0`）。
- v0.19.5 源码用真实 v0.114.0 程序集编译时，`GameStateService.Combat.cs:394` 读取的 `ArrowSlot.DestroyAfterImpaled` 与第 405 行调用的 `MelodyCmd.MelodyCardsPlayedThisTurn(Player)` 均已移除。
- 对 v0.19.5 发布 DLL 的直接游戏成员引用做 CLR 解析，520 条中发现三处无法绑定。第三处为卡牌预览所用的 `CardModel.UpgradeInternal()`，它在新版增加了可选参数，因此新版源码编译不会暴露跨版本的二进制断点。

## 要求

- 修复 v0.114.0 中菜单、战斗和 MCP 读取状态失败的问题。
- 保留原始和紧凑状态已有的箭矢和旋律字段。旋律牌数量按本玩家、本回合的完成记录计数。每条记录只计一次。
- 保留两个游戏版本的卡牌升级预览，继续在独立副本上计算。
- 支持 Steam 当前正式版 v0.111.1 和 public beta v0.114.0。正式版成为清单下限。两个打包脚本都必须验收实际待分发的同一 DLL。
- Windows 构建记录并显式传入实际游戏程序集路径。升级签名解析失败应在启动日志与 `/health` 中可见，并能用可执行的离线替身测试覆盖。

## 验收

- 分别用当前正式版和测试版实际程序集构建 Release，确认构建引用路径、版本和哈希。
- 在独立的两套游戏实例中核对同一个发布 DLL 的 `/health`、主菜单与战斗的 `/state`、`/actions/available`、原生 MCP、出牌及升级预览。测试版验证愚者三标签只计一次出牌。
- 版本或 DLL 哈希不匹配时阻断打包。
- 回归测试、C# 核心测试和静态闸门通过。记录实际运行结果与覆盖范围。

## 范围

修复和验证安排在独立工作树。Steam 正式安装、用户存档、模型或 Jev 服务、Workshop 和 GitHub 发布均在本次修改范围之外。
