# 发布验收入口

2026-04-30 的 v0.7.0 清单已归档至 [历史原文](../history/release-readiness_v0.7.0_2026-04-30.md)。旧版本清单不再作为当前发布门槛。

发布前按以下入口核对：

1. [当前状态与验收边界](../PRODUCT_PLAN_CURRENT.md)：确认候选版本、已有证据和剩余缺口，不能将历史测试结果视为新候选已通过。
2. [发布与验证规范](../.trellis/spec/operations/validation-and-release.md)：核对版本来源、命令及副作用。
3. [发布预检](../scripts/preflight-release.ps1)：运行静态门槛；预检通过不等于实机加载或游玩通过。
4. [发布打包](../scripts/package-release.ps1)：通过两版游戏兼容门禁后生成发布 ZIP。
5. [创意工坊说明](../steam-workshop/README.md)：按目标渠道检查物品、可见性、订阅版本和实际加载；上传成功不能替代订阅加载验收。

实机验收至少区分 Mod 加载、目标单人或多人流程、原生结算与存档，以及安装渠道验证；逐项记录候选版本、证据和未通过项，具体范围以当前状态页为准。

## 发布包兼容门禁

提供 Steam 正式版和 public beta 的两个 Windows 游戏目录。正式版版本从产物清单的 `min_game_version` 读取，public beta 通过 `-TargetGameVersion` 指定。两个目录各自的 `release_info.json` 必须与要求的版本一致。

```powershell
./scripts/package-release.ps1 -Configuration Release `
    -MinimumGameRoot '<minimum-game-directory>' `
    -TargetGameRoot '<target-game-directory>' -TargetGameVersion '<target-version>'
```

也可以用 [test-release-compatibility.ps1](../scripts/test-release-compatibility.ps1) 重验已有的 `mod/` 或 Workshop `content/` 目录。同一份 DLL 按 SHA-256 核对后放入两套临时游戏副本，启动时关闭 Steam 并使用单独的测试档。门禁检查主菜单与战斗状态、原生 MCP、合法动作一致性、卡牌升级预览和一次实际出牌。战斗优先使用该版本有提供的愚者，并核对三标签牌只计一次旋律出牌。没有愚者的版本使用铁甲战士。成功报告记录两版游戏、启动 PID 和 DLL 哈希。GitHub ZIP 的 DLL 还会再次核对哈希。

## 历史实机证据

要判断「某件事以前是怎么验的」时，先看这些定点记录。它们各自属于当时那个构建，不代表当前状态：

- [原生 GAME_OVER 结算与解锁屏分类](../history/evidence/native-game-over-progression/README.md)：2026-08-31 的真机回归（PR #53），含存档哈希与当时的屏幕分类结论。
- [PR #56 原生档案切换实机测试](../history/evidence/pr56-live-test/README.md)：2026-09-05 的档案切换验收。
