# 查漏补缺交接简报

> **这不是任务板。** 仓库唯一的当前状态页仍是 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，
> 唯一的待办清单是它的 §4。本文件是 2026-09-18 一次系统性扫描的产出，**给接手的 agent 用完就删**
> （或把结论并入 §4 后删）。留着它不删，就是在制造第二块板，而 §5 规则 1 禁止这件事。

扫描范围：反射依赖、动作契约覆盖、依赖安全、验证脚本覆盖、文档引用方式、可观测性。
每条都附了**复现测量的方法**——不要采信下面的数字，自己跑一遍再动手。

---

## 先读这些（不可协商的项目惯例）

违反其中任何一条，改动都不该合入：

1. **新契约 / 新闸门一律先做破坏性验证。** 故意把代码或文档改坏 → 确认它转红**且报错点名具体
   对象** → 逐字节还原（用 `cmp` 验证）。PR 描述里逐条列出。只说"加了测试"不算。
2. **不许声称实机验证。** 除非真的驱动了运行中的游戏并留下证据文件。本项目最贵的一次事故
   （v0.12.4）就是"离线全绿 + 显然正确"的产物。没跑实机就写"没有实机复验"。
3. **分支**：功能分支 → PR → `dev`，**不直接 push `main`**；合并用 merge commit 不用 squash。
4. **收尾必须全量跑**，并把数字写进 PR：
   ```
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   cd mcp_server && uv run --locked python -m unittest discover -s tests
   python scripts/check_verification_gates.py
   powershell -ExecutionPolicy Bypass -File scripts/test-verification-gates.ps1
   powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1
   ```
   2026-09-18 的基线：C# 415 PASS / 0 FAIL、`mcp_server` 224 OK、11 道闸门、自测 41 条、preflight exit 0。
5. **改了 `docs/api.md` 描述的任何东西**（路由、载荷字段、错误码、事件类型），`api-facts` 闸门会双向核对。
   改了源文件行数，`arch-facts` 会核对 `.trellis/spec/mod/architecture.md` 的表。
6. **CI 会抓到本机抓不到的东西**：Windows runner 的 stdout 是 cp1252。脚本要打印非 ASCII 时，
   本地先 `PYTHONIOENCODING=cp1252` 跑一遍。
7. `scripts/*.ps1` 含非 ASCII 必须带 UTF-8 BOM；仓库 `core.autocrlf=true`，用脚本改写文件要保住
   原有行尾与 BOM。

---

## A. 上游破坏是盲区 —— 先做这个

**这是唯一一类会让玩家看到假数据的缺口。**

mod 编译期引用 `sts2.dll`（见 `STS2AIAgent/STS2AIAgent.csproj` 的 `Reference Include="sts2"`），
所以游戏**公开 API** 一改名，构建立刻红。唯一的例外是**按字符串反射的游戏私有成员**——
它们一个都没有防护。

STS2 是早期访问游戏。上游改一次私有字段名，玩家看到的不是报错，而是一个看起来很合理的假值。

### 测量（自己跑一遍）

```bash
# 被按名字反射的游戏私有字段
grep -rohE '"_[a-zA-Z][a-zA-Z0-9_]*"' --include='*.cs' \
  STS2AIAgent/Game STS2AIAgent/Multiplayer STS2AIAgent/Ui | sort -u
```
2026-09-18 结果：**21 种**，含 `_maxPlayers`、`_selectedCards`、`_entity`、`_devConsole`、
`_saveAndQuitButton`、`_unlockConfirmButton`、`_isAnimatingSummary`、`_prefs`、`_lobby`、
`_cards`、`_relics`、`_potions`、`_epoch`、`_character`、`_unlockedEpochs`、`_args`、
`_backButton`、`_standardButton`、`_longPressBar`、`_longPressDuration`、`_enabled`。

### 三个已确认的失效行为

| 位置 | 字段消失时会怎样 |
| --- | --- |
| `StartRunLobbyMaxPlayersField` 的读取点（`STS2AIAgent/Game/GameStateService.cs`） | `…?.GetValue(lobby) is int parsed ? parsed : 0` → agent 读到 `max_players: 0`，**分不清"大厅零人"和"读不到了"** |
| `ReflectionMemberAccessor.TryGetValue`（`STS2AIAgent/Game/ReflectionMemberAccessor.cs`，独立类） | 成员不存在与成员值为 `null` **返回同一个 `null`** |
| 同上 | 成员找不到时**零日志**，没有任何信号 |
| `BuildHealthData`（`STS2AIAgent/Server/Router.cs`） | `status = "ready"` 是**硬编码字面量**，从不反映"还读不读得动这个游戏" |

### A1（P0）启动自检 + `/health` 暴露

- mod 加载时逐个解析那批反射成员，把**解析不到的**收集起来。
- `/health` 增加一个区块（建议 `compatibility`），列出缺失的成员名与它们影响的功能；
  `status` 由它派生（例如全部解析到 → `ready`，有缺失 → `degraded`），**不再写死**。
- 这样一来，游戏更新打断 mod 时，玩家和 agent 第一次调 `/health` 就知道，而不是拿到假数据。

**必须同步**：`docs/api.md` 的 `/health` 字段表（`api-facts` 的 `check_health_payload_docs`
会核对 `BuildHealthData` 的键），以及错误码表（如果新增了状态值）。

**破坏性验证**：把某个反射成员名改成不存在的字符串 → `/health` 必须报 `degraded` 并**点名**该成员。

### A2（P0）让缺失可分辨

`ReflectionMemberAccessor.TryGetValue` 要能区分"成员不存在"与"成员值为 null"
（例如返回 `bool` + `out object?`，或返回一个带 `Found` 的结构）。成员不存在时记一条日志。

**注意**：这个方法调用面很广，改签名会牵动很多处。先量：
```bash
grep -rc "TryGetValue(" --include='*.cs' STS2AIAgent | grep -v ':0'
```
可以保留旧重载不动、新增一个能报告"找没找到"的重载，只在会产生默认值的读取点改用新的——
**改动面小得多，风险也小得多**。

### A3（P1）防止清单自己腐烂

加一条源码契约测试：反射成员名的清单（A1 要用的那份）必须与代码里实际出现的
`"_xxx"` 字符串集合一致。否则有人加了新反射点、清单没跟上，自检就漏掉它。

写法参照 `STS2AIAgent.Tests/` 里现有的源码契约测试（它们读 `.cs` 源码文本，不实例化 Godot 类型），
辅助在 `AgentSourceFixture.cs`。**新测试要在 `TestRunner.cs` 里注册，否则不会被执行。**

> 读这批文件前先看 `.trellis/spec/mod/architecture.md`：`GameStateService` 与 `GameActionService`
> 各自是**一个 partial 类分布在多个文件**里，成员放错文件不会编译报错。源码契约要读整个类时用
> `AgentSourceFixture.ReadStateService()` / `ReadActionService()`，不要直接 `Read("...GameStateService.cs")`。

---

## B. 12 个动作没有行为契约

### 测量

对照 `GameActionService.cs` 分发表里的 `"name" => ExecuteXxxAsync(` 配对，再在
`STS2AIAgent.Tests/*.cs` 与 `mcp_server/tests/*.py` 里搜动作名和处理器名。
2026-09-18 结果：56 个动作，**31 个有针对处理器的断言，13 个只是被名字列到，12 个两者皆无**。

两者皆无的 12 个：

```
abandon_run                    choose_treasure_relic          close_shop_inventory
confirm_timeline_overlay       decrease_ascension             discard_potion
disconnect_multiplayer_lobby   dismiss_game_over_wait         increase_ascension
open_character_select          open_chest                     open_timeline
```

**说清楚它们不是完全没防护**：`GameTaskBounding.AllSites` 会逐文件扫无期限 `await`，
`api-doc` 闸门保证它们都在文档里，`ActionSurface.*` 保证动作面只有一处判断。
缺的是**各自的行为契约**——没有任何测试断言过它们做了什么。

### B1（P1）`abandon_run` 优先

它**销毁玩家进度**。先补它：前置条件校验、是否等待原生结算、失败时返回什么。
参照 `GameOverContractTests.cs` 的写法（它对 `continue_game_over` 做了同类的事）。

### B2（P2）其余 11 个

按风险排。`discard_potion` / `choose_treasure_relic` 会改变对局状态，优先于
`open_timeline` / `open_chest` 这类只开界面的。

---

## C. 需要环境或需要人（本轮做不了，别假装做了）

| | |
| --- | --- |
| **C1** | 工坊简体中文列表仍是 v0.11.0 之前的。`ModUploader` **没有语言参数**，只能由项目所有者在工坊网页端粘贴 `steam-workshop/description.zh-CN.txt`。**agent 做不了。** |
| **C2** | v0.11.0 的英文界面/状态文案是机器翻译，未经母语者复核。**需要人。** |
| **C3** | 真实上游超长停流的端到端。证据止于生产超时契约与本机 loopback 回归。 |
| **C4** | 真实浏览器页面加载的 Origin 场景。证据止于 MCP Origin 离线契约与本机探测。 |

---

## D. 发布与可观测

### D1（P1）攒着的提交发版前需要一次实机冒烟

`dev` 领先 `main` 25 个提交。其中：

- **ADR 0001（PR #147）已做过实机回放**：2346 样本基线 vs 45 样本回放，两表面零分歧。
  它改了 `available_actions` 的**发射顺序**（集合不变）——发版记录必须写上这条 agent 可见的变化。
- **PR #148 / #150 的拆分没有实机证据**。源码级已证明是纯位移（基文件 diff 只有 `partial`
  一个词是新增，被删的非空行逐字出现在新文件里），且已核实**没有静态字段初始化互相依赖**
  （partial 拆分唯一能改变行为的那条路径）。
- 工坊包只装 DLL + PCK + `mod_id.json` + `README.md` + LICENSE，**`docs/api.md` 不进包**。

**结论**：这批对玩家是零可见变化，不值得单独发一个版本号。**等下一个玩家能感知的修复**，
届时那次修复本来就要做实机验证，顺带覆盖这批位移。

### D2（P2）`/state` 耗时没人看

`STS2AIAgent/Server/Router.cs` 里每请求耗时只写进日志（`Completed {statusCode} in {ms}ms`），**没有阈值、不进任何载荷**。
agent 每一步都要拉 `/state`，它变慢了没有任何机制会发现。

离线做不了性能门禁（要真游戏）。可行的第一步：把耗时放进 `/health` 的滚动统计
（最近 N 次的 p50/p95），这样实机验证时至少能读到数字。

---

## E. 文档引用方式（P2）

规格里还有 **35 个行号锚点**（`client.py#L265` 这类）。`doc-links` 闸门现在只能保证
"这一行存在"，保证不了"这一行还是那一行"。

2026-09-18 已经修过一次：六个锚点因为 `client.py` / `server.py` 被拆而指到文件尾之外。
**带行号的链接照样打得开，只是落在别处——这是链接腐烂里最安静的一半。**

治本是改成按符号引用（写 `Sts2Client._request` 而不是 `client.py#L265`）。
复现测量：扫所有入库 `.md` 里形如 `#L<数字>` 的链接。

---

## 我没有验证的事

写下来，免得被当成已知事实：

- **没有跑过实机。** 上面所有关于运行时行为的判断都来自读源码，不是观察。
- A 节里"字段消失时会怎样"是**读代码推断的**，不是真删掉字段跑出来的。动手前先用
  A1 的破坏性验证确认一遍。
- B 节的 12 个动作，我只确认了"没有测试引用它们的处理器"，**没有逐个读它们的实现**判断
  各自该有什么契约。
- D2 的性能问题我只确认了"没有阈值也不进载荷"，**没有测过 `/state` 实际有多慢**。

---

## 建议顺序

**A1 → A2 → A3 → B1 → D1 →（其余按 P2）**

A 是唯一会让玩家拿到假数据的一类，且三条互相衔接（A1 需要 A2 才能分辨缺失，A3 防止 A1 的清单腐烂）。
B1 次之，因为它涉及销毁玩家进度。C 整节别碰——不是不做，是 agent 做不了。
