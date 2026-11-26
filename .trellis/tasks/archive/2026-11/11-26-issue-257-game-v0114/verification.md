# 验证记录

## 当前发布验收矩阵

Steam 正式版 v0.111.1（`cc1f9462`）是清单下限，public beta v0.114.0（`a7b9a9c0`）是此次目标。`build-mod.ps1 -GameRoot` 用两边实际的 `data_sts2_windows_x86_64` 构建 Release。日志中的 `sts2.dll` SHA-256 分别以 `3FB1FA1B` 和 `DDCEB879` 开头。

GitHub 和 Workshop 打包入口均通过发布门禁，两次构建的 DLL SHA-256 都是 `B45423D9A6BCD06ADAA60596A815E8E95CD1335B35170FA0A0902E9080FBF067`。每版均通过启动身份、主菜单、原始与紧凑战斗状态、敌人意图、原生 MCP、动作一致性，以及 `STRIKE_IRONCLAD` 一级升级预览的验收。正式版实际打出 `STRIKE_IRONCLAD`，测试版愚者打出 `WAH_CHO_KI` 后确认三个标签只计一次旋律出牌。两边 `/health` 均为 `ready`，29 个兼容探测均通过。生成的 GitHub ZIP 和 Workshop 内容目录中的 DLL 哈希与各自报告一致。

把 v0.113.1 作为正式版输入时，发布门禁在启动前报游戏版本不匹配。用错误哈希检查同一 GitHub ZIP 时，产物检查器返回失败。Windows 路径检查确认了 `-DataDir`、`STS2_DATA_DIR`、`-GameRoot` 的优先顺序。

Windows 发布预检用正式版 `-GameRoot` 和工作树内的测试临时目录通过。C# 核心测试 1,180 项通过，两个 `HttpListener` 测试因宿主不支持而跳过。Python MCP 测试 466 项和 12 道静态闸门通过。实机门禁另行验证了两边的 HTTP 和原生 MCP。

## 初次修复阶段证据

### 复现与引用核对

- v0.19.5 发布 DLL 在 v0.114.0 主菜单上复现 `GET /health` 200 和 `GET /state` 500。日志定位到 JIT 编译 `BuildCombatPayload` 时缺少 `MelodyCmd.MelodyCardsPlayedThisTurn(Player)`。反射健康检查的 28 项全部解析成功。
- 用 .NET 的 `Module.ResolveMember` 逐个解析发布 DLL 中声明类型直接引用 `sts2` 的 `MemberRef`。520 项中有三项失败，分别为旋律牌计数、旧箭矢损毁标记，以及 `CardModel.UpgradeInternal()`。此计数的统计单位为元数据行。
- 用 v0.114.0（`a7b9a9c0`）和 v0.113.1（`d09d48e5`）程序集分别重新构建 Release，均为 0 警告、0 错误。两份修复 DLL 各有 523 项同范围引用，在两代游戏上交叉解析均为 0 失败。

### 游戏验收

同一份 Release DLL（SHA-256 `3CAAA6CC8BAE0B656E13D5106C5247636CB20DD7503C806C48F8209FADCC934E`）分别放入两个隔离游戏目录。修复版使用 `--force-steam off` 和独立 clientId。

| 检查 | v0.114.0 | v0.113.1 |
| --- | --- | --- |
| `/health`、主菜单 `/state`、`/actions/available`、原生 MCP 状态 | 通过 | 通过 |
| `/data/cards` 返回 `FOOLISH_SHOT` 的可用一级升级预览 | 通过 | 通过 |
| 愚者战斗原始和紧凑状态、敌人攻击意图、MCP 局内摘要 | 通过 | 通过 |
| 打出 `WAH_CHO_KI` 得到 3 个标签和 1 次出牌，次回合计数归零 | 通过 | 通过 |
| 打出 `FOOLISH_SHOT` 后箭矢从 `retrieved` 变为 `away`，动作列表与状态一致 | 通过 | 通过 |

首次复现发布版时，启动参数未关闭 Steam，游戏自动启动了云存档同步流程。该次请求仅用来读取健康和状态，日志未出现存档写入。后续使用禁用 Steam 的隔离游戏会话。

### 自动检查

- C# 核心测试 1,176 项通过、2 项跳过。愚者资源回归测试在修改实现前失败，修改后通过。
- Python MCP 测试 462 项通过。
- `python scripts/check_verification_gates.py` 和 `git diff --check` 通过。
