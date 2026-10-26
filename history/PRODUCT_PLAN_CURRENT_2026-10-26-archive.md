# PRODUCT_PLAN_CURRENT 归档（2026-10-26 整理时移出）

> 历史，不代表当前。以下为当时 `PRODUCT_PLAN_CURRENT.md` 中已随 v0.13.0 发布的标签后清单与 2026-09-19 的发布渠道记录；当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

<details>
<summary>2026-09-19 的发布与渠道记录</summary>

> 本页是仓库唯一的当前状态入口。更新日期：2026-09-19（**v0.13.0 已发布**，GitHub 与 Steam 工坊同日、同源、各一次，未重切）。历史快照标记：本页为当前状态页，非历史快照。
> 发布代码基准：tag `v0.13.0` @ `78b085f`（PR #161 的 `dev → main` 合并）。GitHub Release 资产 `sts2-ai-agent-v0.13.0-windows.zip` 576033 字节、SHA256 `8CFC0F45…8733`。
> 工坊最新：物品 3796486050 已于 2026-09-19 11:32:25 更新至 `0.13.0`（公开，`file_size` 1252357 与本地 content 字节和相等，内容 id `3840768014407368680`，DLL SHA256 `6B9D90C8…FBE42`）。最终发布 DLL 已单独通过隔离实机短冒烟。见 [v0.13.0 发布记录](release-v0.13.0_2026-09-19.md)。
> 构建识别：跨版本指纹索引见 [history/build-fingerprints.md](build-fingerprints.md)；自本版起这些数字由打包脚本写入产物旁的 `build-fingerprint.json`，不再事后手工采集。
> 上一版 v0.12.5 的记录见 [v0.12.5 发布记录](release-v0.12.5_2026-09-17.md)。**工坊简体中文列表仍是旧版**（自 v0.11.0 起），待手工粘贴 `steam-workshop/description.zh-CN.txt`——`ModUploader` 没有语言参数，只能在工坊网页端做。

</details>

### 已随 v0.13.0 发布：v0.12.5 标签后第二批（2026-09-18）

按 §5 规则 3 单列。**不改任何随 mod 发布的运行时行为**：唯一动到的运行时代码是 `docs/api.md` 里
三条错误码说明，以及 Python sidecar 内部的一次纯位移。

上一轮补的是「代码在哪」和「链接指向哪」。这一轮问的是：**agent 依赖的契约面，还有哪些没人在看。**
`docs/api.md` 让客户端分支的东西有四类——能调哪些路由、会读到哪些字段、要处理哪些错误码、
能等哪些事件。**只有字段那一类被检查着。**

1. **三个错误码已经掉出契约表了**，而且都不冷门：`listener_error`（500，HTTP 监听循环自己抛异常）、
   `method_not_allowed`（405，用非 POST 请求 `/mcp`）、`payload_too_large`（413，`/mcp` 请求体超 1 MB）。
   agent 收到这三个，在文档里查不到任何东西，也就分不清「该改请求」还是「该重试」。三条已补文档。
2. **`api-facts` 新增三条双向检查**：错误码（含 HTTP 状态码比对，代码侧从 `ApiException` /
   `WriteErrorAsync` / `RestError` 三种机制抓）、事件类型、路由。事件与路由本来就是准的——
   照样检，理由和上一轮 `doc-links` 一样：**靠维护得当准，和靠机制保证准，不是一回事。**
3. **六个行号锚点指到了文件尾之外**（`client.py#L691`、`server.py#L1082` 等），全是上一轮和这一轮
   拆分造成的。带行号的链接**照样打得开**，只是落在别处——这是链接腐烂里最安静的一半。已重新指向，
   并让 `doc-links` 检查行号在文件范围内。
4. **`server.py` 1,103 → 707 行**：游戏数据的加载 / 缓存 / 索引 / 场景推导移入 `game_data.py`，
   预算 1,150 → 750。踩到 Python 特有的坑：`from X import f` 绑定的是**函数对象**，
   patch 另一个模块的同名属性不会报错、也不会生效——四个测试的 patch 目标必须跟着函数走。
5. **`AgentOverlayHost.cs`（1,829 行）决定不拆**，理由写进架构页并附测量：85 个实例字段里
   **78 个被多于一个方法碰**，平均 2.6 个。硬拆成 partial 只会把共享可变状态撒到多个文件里，
   为了文件小一点换来更难读、更易错。真要改是把某个页签连状态抽成独立类，那是设计工作不是机械拆分。

离线证据（本轮收口后全量重跑）：C# **415 PASS / 0 FAIL**；`mcp_server` **224 项 OK**；
**十一道闸门全绿**；**闸门自测 41 条全过**（本轮新增六条）；mod 构建 0 警告 0 错误。

**没有实机复验**——本批不含运行时行为改动。每条新检查都做了破坏性验证：删一行错误码、
把错误码状态改成 400、删一行事件类型、文档写一个不存在的端点、代码加一条没文档的路由、
锚点指向第 99999 行——全部点名转红，随后逐字节还原。

### 已随 v0.13.0 发布：v0.12.5 标签后主线变更（2026-09-17）

按 §5 规则 3 单列。**这一批不改任何随 mod 发布的运行时行为**：两次大文件拆分都是同一个 `partial`
类内部的成员位移，基文件的 diff 各只有一行真正新增（`partial` 关键字），编译产物的语义不变。
工坊上的 0.12.5 与 GitHub Release 资产都不受影响。

本轮的出发点不是找 bug，而是一个具体的发现：**`.trellis/spec/mod/architecture.md` 在骗人。**
它带着 ADR 0001 之前的度量数字，并且在「新增动作去哪儿写」一节里仍然写着「两个动作面都要加」——
而那份重复一个月前就被合成一次遍历了。ADR 0001 落地时同步改了 `AGENTS.md` 与 game-actions 规格，
**唯独漏了这一页，而且没有任何机制会发现**。

所以本轮的主线是：**把「没人在看」的地方补上看的人**。

1. **两个巨石文件拆开**（`GameStateService.cs` 8,295 行、`GameActionService.cs` 7,061 行，合计占
   mod 的一半）。状态服务交出紧凑 `agent_view` 改写与 60 个载荷类型声明；动作服务按房间拆成八个
   partial。**归属是算出来的不是分出来的**——一个成员只有在「所有引用都来自本组」时才归入该组，
   两组都够得着的留在基文件。动作服务 6,756 行成员里 5,697 行落入唯一房间，1,059 行是真共用。
   最大文件从 8,295 降到 5,872，八个房间文件有六个落在 1,000 行默认预算内、根本不需要条目。
2. **`arch-facts` 闸门**（第十道）：架构页的文件表必须与真实文件对上——每个文件存在、行数误差在
   5% 内、总数吻合，**且任何超过 1,000 行的源文件都必须出现在表里**。最后这条是关键：那张表就是
   「哪里重」的唯一去处，它漏掉的巨石就是没人在看的巨石。改标题会让闸门报红而不是悄悄失效。
3. **`doc-links` 闸门**（第十一道）：403 个入库页面里的 411 条相对链接必须都解析得到。此前只有
   打进发布包的那三个文档被检。价值全在于它挡住的事——而本轮正好动了四个文件的位置。
4. **四处「在检查却没真检查」的东西被揪出来**，前三处都早于本轮的拆分，第四处是本轮新闸门自己的毛病：
   - `AgentSourceFixture.MethodBody` 用「最后一次出现」定位方法，跨 partial 类时会返回**调用点所在
     方法**的方法体——契约照常断言，只是断言错了对象，而且不报错。
   - `GameTaskBounding` 用硬编码文件表扫无期限 `await`，新房间文件里第一个裸 `await` 会看不见。
   - 闸门自测有三个用例报 PASS，实际闸门是以「missing required file」失败的——`Assert-Case` 只看
     退出码。现在每个用例必须声明它期望看到的报错文本，不声明即判失败。
   - **`doc-links` 与 `arch-facts` 第一版问的是文件系统**：本机全绿、CI 红。工作树里有 clone 没有的
     文件——`AGENTS.md` 刻意不入库、`extraction/decompiled/` 是本地反编译产物——36 条链接指着它们。
     也就是说这两道闸门的结论取决于**谁在跑**，而这正是它们上一层要防的那件事。现在都改成问 git。
     那 36 条是真断链不是误报：两处正文本来就写着「全新检出里不存在」。现在写成代码体——
     Markdown 链接是「点得开」的承诺。
5. **ADR 0002** 记录为什么保留两个 MCP 工具面（进程内原生面 + Python sidecar），以及什么条件下重评估。
   新增 MCP 工具要两边都加——**这与 ADR 0001 的「只加一处」正好相反**，所以写进了 `AGENTS.md` 的步骤里。
6. Python 侧 `client.py` 的 58 个逐动作包装移入 `client_actions.py`，两半都回到 700 行默认预算内，
   预算条目由棘轮自己要求撤下。

离线证据（本轮收口后全量重跑）：C# **415 PASS / 0 FAIL**；`mcp_server` **224 项 OK**；
`check_verification_gates.py` **十一道闸门全绿**；`test-verification-gates.ps1` **闸门自测 35 条全过**
（本轮新增五条：arch-facts 三条、doc-links 两条）；mod 构建 0 警告 0 错误。

**没有实机复验**——本批不含运行时行为改动。每一处新守卫都做了破坏性验证：改坏一处紧凑视图重命名、
移走新拆出的文件、在新房间文件里写一个裸 `await`、把架构表写回旧数字、删掉表里一行、改掉章节标题、
给自测用例一个闸门永远不会说的期望、在入库页面里加一条断链、链到一个磁盘上有但 git 不跟踪的
文件与目录——全部点名转红，随后逐字节还原。

### v0.12.4 标签后主线未发布变更（2026-09-16）

按 §5 规则 3 单列。**这一批只动文档、测试与脚本，不改任何随 mod 发布的运行时代码**，所以工坊上的
0.12.4 第三次构建与 GitHub Release 资产都不受影响；已发布的 DLL/PCK 未变。

本轮的出发点是同一件事：**连续四次同号重发**（v0.12.3 两次、v0.12.4 两次）里，第二次 v0.12.4 重发修的
是第一次重发自己引入的回归——战斗门禁在主菜单读 `RunManager.ActionExecutor`（那里没有 executor），
让每个 `/state` 都 500，mod 在战斗外完全不可用；而当时 408 条 C# 测试与九道闸门**一条都没拦住**。
所以这一轮做的是把「当时没人看着」的三处补上防回归机制，而不是加功能。

1. **门禁的战斗内守卫有了源码契约**（`CombatGate.QueueReadIsCombatOnly`）：`EvaluateCombatActionGate`
   对 `RunManager.ActionExecutor` / `ActionQueueSet` 的读取必须待在
   `if (combatState != null && CombatManager.Instance.IsInProgress)` 里面，且整个状态构建器里只有门禁
   这一处碰这两个成员——第二条防的是「换个调用点再犯一次」。两次破坏性验证：把读取上提到守卫之外转红、
   在文件别处加第二处无守卫读取也转红，随后逐字节还原。C# 测试 408 → **409 PASS / 0 FAIL**。
2. **`docs/api.md` 第一次写下 `/state` 的 `combat` 顶层字段**。`action_readiness`、`players[]`、
   `end_turn_will_kill_player`、`lethal_risks[]` 四个字段早已发布、且都通过 compact `agent_view` 发给
   每一个外部 agent，而文档里一个字都没有；其中 `action_readiness` 正是本版头条修复的对象，也是
   `state-invariants` 判定依据。新增三张表（三个载荷记录）与 `reason` 的全部 19 个取值，并写明每个取值
   对 agent 意味着「等」还是「先关弹窗」。
3. **`api-facts` 闸门把这三张表钉在产生它们的记录上**：`CombatPayload` / `CombatActionReadinessPayload`
   / `CombatLethalRiskPayload` 逐字段，外加 `EvaluateCombatActionGate` 能给出的每个 `reason`。少写一个
   字段、多写一个代码里没有的字段、漏掉一个 reason，都会点名报错。`test-verification-gates.ps1` 新增三条
   破坏性用例（此前 api-facts 只有三条，覆盖版本号 / 屏幕名 / 端口）。
4. **打包产出构建指纹**（`scripts/lib-build-fingerprint.ps1`，两个打包脚本各调一次）：逐文件 SHA256、
   Steam 用作 `file_size` 的字节总和、构建所用提交与 working tree 是否干净，写成产物旁的
   `build-fingerprint.json`。同号重发时这是唯一能区分构建的东西，而此前每次都是上传完再手工补抓。
   跨版本索引新建在 [history/build-fingerprints.md](build-fingerprints.md)，并给出「玩家报 bug 时
   怎么确认他跑的是哪个构建」的四步做法。
5. **`CHANGELOG.md` 建立 `## Unreleased` 段的约定**，写进 `.github/CONTRIBUTING.md`、`AGENTS.md` 与
   `.trellis/spec/operations/validation-and-release.md`。四次重发全都始于「tag 之后来了个小修复、
   变更日志里没地方写」——有地方写的改动才等得到下一个版本号。
6. **游玩 skill 补上 `action_readiness` 的读法**：`COMBAT` 上没有 `play_card` 是门禁的某个 reason，
   不是丢了回合；reason 说明该关弹窗、该等、还是该让开一个被人暂停的对局。游戏内 agent 读同一份契约
   （SKILL.md 是嵌入资源，`Skill.McpPlayerContract` 钉住两者相等）。

7. **`docs/api.md` 第一次完整描述 `/state`**。盘点发现**七个子结构整段没有文档**——`session`、
   `multiplayer`、`multiplayer_lobby`、`character_select`、`timeline`、`modal`、`game_over`——
   顶层字段表还漏了 9 个字段，合计 **91 个字段在文档里搜不到任何痕迹**。其中三块是 agent 必须亲自
   驱动的屏（选角 / 联机大厅 / 结算），而 `session` 正是游玩 skill 要求「路由的第一依据」的那个块。
   现已全部补齐，盘点脚本复测 91 → 0。
8. **compact `agent_view` 的改名表**。MCP `get_game_state` 默认返回 compact，而它**改了 43 个键名**
   （`can_embark`→`embark`、`enough_gold`→`affordable`、所有 `index`→`i`……）。这套映射此前只存在于
   `BuildAgent*Payload` 方法里，文档零字：外部客户端照 `/state` 名去取会读到 `undefined` 而不是报错。
9. **修掉一处文档写错**（不是缺，是错）：`shop.cards[]` / `relics[]` / `potions[]` 被写成有 `available`
   字段，这三个记录从来没有过它——只有 `shop.card_removal` 有。照旧文档分支的 agent 分不清「卖光了」
   和「买得起」。现改为记录 `is_stocked` 与 `enough_gold`，并写明哪一个才是「现在能不能买」。
   同时修掉 `run` 字段表被空行截断成两个表格的渲染缺陷。
10. **`api-facts` 闸门随之扩容**：16 个载荷记录逐字段对表、`GET /health` 的 21 个键对表、
    改名表对 `BuildAgent*Payload` 的实际改名、外加一张兜底网——**每个 `/state` 载荷记录的每个字段
    都必须在 `docs/api.md` 里被提到**（现覆盖 56 个记录 488 个字段）。闸门自测从 22 条增到 28 条。
    顺带修掉闸门自己的一个提取缺陷：C# 用 `@` 转义的关键字标识符（`public EventPayload? @event`）
    此前会被误判成「文档里有、代码里没有」。

11. **全局架构体检与防屎山机制**。量出来的事实：82 个 C# 源文件共 31,632 行，其中
    `GameStateService.cs`（8,559 行）与 `GameActionService.cs`（7,008 行）**两个文件占 49%**。
    两者不是同一种「大」：前者是 318 个方法挤在一个 7,290 行的类里、三种关注点（原始载荷构建 /
    compact 改写 / 动作可用性）纠缠在一起；后者是同一个模式（`Execute*` + `WaitFor*`）重复 60 遍，
    结构清晰、只是都在一个文件里——所以按房间拆它是机械动作，不需要设计。
12. **找到本项目最大的结构性负债**：`BuildAvailableActionNames`（301 行）与
    `BuildAvailableActionsPayload`（609 行）是**同一个判断的两份手写实现**——逐项比对确认查询
    **相同的 50 个谓词**、产出**相同的 55 个动作名**（仅发射顺序自第 28 项起不同）。新增一个动作
    要改两处，而项目文档一直把「两处都要改」写成正常步骤；已经为此写过一次局部契约
    （`CombatGate.ActionsAskTheSharedGate` 存在的唯一理由就是防它们在三个战斗谓词上走岔）。
    **本轮不动它**：这 910 行决定 agent 被允许做什么，在只有离线证据的条件下重写，正是 v0.12.4
    回归的成因。改为加两条源码契约把集合钉死（只给一个表面加动作即转红点名），并写下
    [ADR 0001](../docs/adr/0001-single-action-surface.md) 记录收敛方案与它等待的实机验证门槛。
13. **体量棘轮**（C# + Python 两侧）：每个文件都有行数预算，最大的四个 C# 文件与两个 Python 模块
    各自列名，其余走默认上限；**预算只降不升**。第二条检查会在预算远高于实际时转红——文件瘦下来
    棘轮就跟着收紧，不留回涨的空间。两侧各做了破坏性验证。
14. **架构债写进规范**：`.trellis/spec/mod/architecture.md` 新增「Code shape and its known debts」
    一节，带上述测量数据、两个巨型文件的区别、以及「新代码该放哪」。顺带修掉 `AGENTS.md`
    「新增动作」步骤里指向一个**不存在的方法**（`BuildAvailableActionDescriptors`）。

**CI 抓到本轮引入的一个真实缺陷，已修**：闸门的提示与错误消息会引用 `docs/api.md` 的中文章节标题，
而 Windows CI 的 stdout 是 cp1252——`print` 直接抛 `UnicodeEncodeError`，于是**判断通过的闸门仍然 exit 1**；
更糟的是闸门真失败时，真实原因会被编码 traceback 顶掉。现在闸门统一以 UTF-8 输出（`errors="replace"`），
自测新增一条在 `PYTHONIOENCODING=cp1252` 下跑全套的用例，撤掉修复即转红。

**闸门自测在这一轮真的拦下了一次**：新增的 `/health` 检查要读 `Router.cs`，而 CI 用的 fixture 里
没有它——基线用例立刻转红，在合并前就暴露了「本机能跑、CI 会挂」。

离线证据（本轮收口后全量重跑）：C# **409 PASS / 0 FAIL**；`mcp_server` **222 项 OK**；
`check_verification_gates.py` **九道闸门全绿**；`test-verification-gates.ps1` **闸门自测 29 条全过**
（本轮新增七条，此前 22 条）；`check_release_metadata.py` 五处版本号一致；`preflight-release.ps1` exit 0。
**没有实机复验**——本批不含运行时代码改动，实机结论沿用 0.12.4 第三次构建那次。

