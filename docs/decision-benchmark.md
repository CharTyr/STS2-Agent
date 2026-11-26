# 决策质量离线评测集

`decision-benchmark.json` 是 v2 的**可复现实例集**：它评测一份候选动作记录是否遵守当前
状态、可用动作以及 playbook 已写明的高优先级约束。它不是战斗模拟器，也不宣称离线分数能够
证明某个模型会通关、会获得某个胜率，或在真实游戏里做出同样的下一状态。

这个边界是刻意的：每个可计分点都必须由同一份状态快照和现有策略文字支撑；没有足够事实的
卡牌强度、长期构筑、战斗结局一律不伪造分数。这样可先零成本固定评测格式，而将来从 debug
console 采集实机局面、或在得到用户许可后调用真实模型时，仍可复用同一份输入和报告格式。

## 运行方式（无游戏、无网络、无模型）

```powershell
# 仅验证基准夹具的结构、证据路径与版本
python scripts/decision_benchmark.py

# 对一份候选动作集计分；仓库提供的 example 是刻意的满分基线，不是模型结果
python scripts/decision_benchmark.py `
  --answers docs/decision-benchmark.answers.example.json

# 将确定性 JSON 报告写到 gitignored build/ 供本地比较
python scripts/decision_benchmark.py `
  --answers path/to/candidate-answers.json `
  --output build/decision-benchmark/candidate-report.json

# 离线模块契约测试
python scripts/test-decision-benchmark.py
```

如游戏已由操作者自行启动，下面的**显式只读**命令可抓取一份原始 `/state` 作为人工新增 case 的
候选材料；它不会发送 action 或模型请求，输出写入 gitignored `build/`，也不会自动把局面算作
通过：

```powershell
python scripts/decision_benchmark.py `
  --capture-base-url http://127.0.0.1:8080 `
  --output build/decision-benchmark/manual-state-capture.json
```

这四个命令只读入版本控制内的 JSON（最后一个 `--output` 例外会写本地报告），不会启动游戏、
访问 MCP、调用 HTTP 或使用任何模型/API 凭据。`scripts/preflight-release.ps1` 会执行结构验证与
模块契约测试，因而确保发布前 fixture 不会损坏；它不会把 example 的 100% 当成模型质量证据。

## 输入契约

### Suite：`decision-benchmark.json`

顶层固定为：

```json
{
  "schema_version": 2,
  "title": "...",
  "cases": []
}
```

每个 case 的字段是：

| 字段 | 约束 | 含义 |
| --- | --- | --- |
| `id` | 全局唯一字符串 | 稳定比较键；扩充基准时不得改写已发布 case 的 id |
| `kind` | `combat` / `event` / `map` / `rest` / `shop` / `reward` | 该决策所在屏类型 |
| `title` | 非空字符串 | 可读标题，不参与计分，也不得发送给被测模型 |
| `calculator` | 可选字符串（如 `combat_math`） | 标记需由 C# 算术单测交叉复算的共享夹具 |
| `state` | `/state` 或 compact 风格 object | 至少有 `screen` 和 `available_actions`；允许增加真实状态字段，避免格式因 mod 新字段变脆弱 |
| `expectations` | 非空数组 | 每条独立计分约束，且必须能被 fixture 本身验证 |
| `evidence` | 非空字符串数组 | 指向策略/playbook/状态事实的可读来源；不是模型理由的替代品 |

`screen` 必须等于 `kind` 的大写形式。原始状态**允许**未知字段，因为客户端要能读取较新 Mod
添加的字段；而 suite 自己的顶层、case、expectation 结构则是严格的，拼错字段会失败而不是静默
改变评分。

每条 expectation 有唯一 `id`、正数 `points`，再选下面一种 `kind`：

| expectation kind | 必填字段 | 得分语义 |
| --- | --- | --- |
| `action` | `action` | 候选动作名必须相同 |
| `option_action` | `action`, `option_index` | 动作和 index 都必须相同（支持卡牌、地图、事件、休息、商店/Blitz 商店条目 `buy_blitz_entry`、奖励） |
| `target_action` | `action`, `target_index` | 动作和敌人/玩家目标 index 都必须相同 |
| `forbid_action` | `action` | 候选动作不能等于该动作 |
| `plan` | `must_kill_enemy` 和/或 `max_damage_taken` | v2 起：验证单步可打出牌（`playable != false`）是否击杀指定敌人，或将回合末承伤控制在上限以内 |
| `state_path` | `path`, `equals` | **fixture 自检**：dotted path 在该 state 中必须等于值；回答某 case 时它计分，确保“有证据”的 case 不比普通 case 更容易满分；未回答 case 仍为 0 分 |

`state_path` 不读取模型输出；它是在 suite load 时校验“本 case 是否真的具有所声称的事实”。
此外 loader 会核验每条推荐的 action 确实在 `available_actions` 中，并将每种支持的 `option_action`
反查到其 snapshot 数组（卡牌、地图、事件、休息、商店及 `shop.blitz_entries`、奖励）；`target_action` 反查目标卡公开的
`valid_target_indices` / `targets`。`option_index` 是 benchmark 的中性候选选择位置（对 `play_card`，runner
应把它转成真实请求的 `card_index`）；答案同时保留真实请求的 `target_index`，因此目标选择不会被
当成普通 option。若未来要评测理由质量或多步 outcome，应再升 schema version 并用新 expectation
kind，不能暗中重解释既有字段。

### Answers：候选动作记录

```json
{
  "answers": [
    {
      "case_id": "map-low-health-avoid-elite",
      "action": "choose_map_node",
      "option_index": 1,
      "target_index": 0,
      "reason": "optional human/model rationale; current schema does not score it"
    }
  ]
}
```

`case_id` 只能出现一次，且必须引用 suite 内已有 case；`action` 必填，`option_index` 和
`target_index` 可选且只能是非负整数。遗漏一个 case 不会被从分母中抹掉：该 case 是 0 分并在报告中标记 `answered: false`。
此规则使不同模型/runner 的覆盖率显式可见，不能靠只交“会的题”得到虚高百分比。

## 评分与报告

分数是所有 expectation 的 `earned_points / available_points`。报告包含：suite 名称、答案覆盖数、
总分、百分比，以及每个 case 每条约束的 `passed` 和明细。JSON 经排序键、两格缩进并以换行结束，
所以同输入总会得到同字节输出。

- 参考答案文件是**人工构造的满分基线**，只证明 loader/scorer/fixtures 自洽；不可写入模型兼容矩阵，
  也不能说某供应商已通过。
- 没有游戏状态迁移、伤害模拟或奖励价值函数：报告明写 `scoring_scope`，防止下游把 100% 曲解为
  “会赢”。
- case 只以已暴露的数据评分。例如 shop case 可测 `enough_gold` / `on_sale`，但不会在缺少卡牌
  元数据与构筑上下文时宣称某张牌“更强”。

## 采集与实测的后续接口

1. **实机采集（仍待人工启动的游戏）**：开启 debug actions 后，通过
   `run_sts2_validation.py` 的现有 `run_debug_command()` 到固定 room/fight，再读取一次稳定 `/state`。
   可用 `decision_benchmark.py --capture-base-url ... --output build/...` 作一次明确的只读捕获；它不会
   自动导航、执行 action 或调用模型。人工审核状态、删去不宜提交的标识符后，再将该 state 连同可复核
   `evidence` 归档为一个新 case；不要把 save 或 token/密钥提交到仓库。
2. **候选模型评测与跨模型对比（`scripts/run_decision_benchmark_eval.py`，显式 opt-in）**：
   - 默认零网络开销：必须显式传入 `--allow-live-api` 才会请求模型，并受硬上限 `--max-requests` 保护；密钥仅从环境变量读取且绝不写入报告。
   - 离线 dry-run（零费用、检查 prompt 组装与请求数）：
     ```powershell
     python scripts/run_decision_benchmark_eval.py --dry-run --output build/decision-benchmark/dry-run.json
     ```
   - 在获得用户授权后评测单个 OpenAI 兼容模型：
     ```powershell
     python scripts/run_decision_benchmark_eval.py `
       --allow-live-api --max-requests 24 `
       --base-url https://api.openai.com/v1 --model gpt-4o `
       --api-key-env OPENAI_API_KEY `
       --output build/decision-benchmark/gpt-4o.json
     ```
   - 跨模型横向对比（输出 Markdown 得分/延迟/Token 汇总与逐题分歧）：
     ```powershell
     python scripts/run_decision_benchmark_eval.py `
       --compare build/decision-benchmark/model-a.json build/decision-benchmark/model-b.json `
       --markdown
     ```
3. **实机 outcome（独立的更强证据）**：若要声称“该模型在局面 X 成功”，记录初始 state、请求、
   action response、fresh state 和可观察的前后值，按项目现有 live-validation 规则验证。不要用离线分数替代该证据。

当前 v2 基准包含 24 个跨 combat/event/map/rest/shop/reward 的结构化与算术用例（含 `v0.114.0` Fool 零箭矢、Regent 星能斩杀、Defect 冰球被动格挡、流沙倒计时 1 与 Blitz 免费商店选择）。
