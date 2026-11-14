# 验证记录

## 环境与范围

- 日期：2026-11-14；分支：codex/gameplay-convergence；基线：94ef838。
- 研究角色配置为 gpt-6-luna/max；实现和检查角色配置为 gpt-6.1-terra/max，派生接口已实时显示绑定。
- 实现代理完成了代码及多轮离线检查；主会话为控制继续扩展已停止代理，并直接核对留下的日志，不重复运行相同全套测试。
- 以下命令为本仓库标准复现入口；日志中的最终结果是实际执行证据，最终独立检查可补充具体参数或限制。

## 初轮日志（环境修复前）

| 检查 | 标准复现命令 | 结果 | 日志 |
| --- | --- | --- | --- |
| C# 核心/契约 | `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` | 1148 通过，2 失败；全套未全绿 | `build/gameplay-convergence-20261114/csharp-final.log` |
| Python MCP（修复前） | 在 `mcp_server/` 下 `uv run --locked python -m unittest discover -s tests -v` | 457 项全部通过；后续修复的重跑另列下方 | `build/gameplay-convergence-20261114/python-final.log` |
| 静态闸门 | `python scripts/check_verification_gates.py` | 12 项通过；相关文档及架构闸门在后续修改后另有复跑通过 | `build/gameplay-convergence-20261114/gates.log`、`gates-affected-rerun.log`、`gates-arch-final.log` |

C# 失败：`NativeMcp.OriginHttpPolicy`、`LoopbackListener.RealLoopbackListenerResponds`；实际日志为 HttpListener 的“句柄无效”。`build/live-2026-10-27/quality-recheck-notes.md` 与该目录 `csharp-recheck.log` 记载了同名测试、相同失败；两项监听器测试和监听实现未被本轮更改。不得把它们标成通过。新增流程契约及 MCP 初始化测试在最终全套日志通过。

## 独立检查后的聚焦复验

| 检查 | 结果 | 日志 |
| --- | --- | --- |
| C# 类型编译：`dotnet build STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release --no-restore` | 成功；0 错误、1 条既有 `McpStateSlimmerTests.cs:332` CS8613 可空性警告（该文件无本轮差异） | `build/gameplay-convergence-20261114/review-csharp-build.log` |
| C# 提示词、游戏内工具目录与屏幕片段、外部初始化聚焦检查 | 9/9 通过；临时 reflection harness 只执行相关现有测试，未重复全套 | `build/gameplay-convergence-20261114/review-csharp-focused.log` |
| Python 场景指导与 C# 映射：在 `mcp_server/` 下 `uv run --locked python -m unittest discover -s tests -p test_scene_guidance_alignment.py -v` | 8/8 通过 | `build/gameplay-convergence-20261114/review-python-scene.log` |
| Python 语法：`uv run --locked python -m py_compile src/sts2_mcp/server.py src/sts2_mcp/scene_guidance.py tests/test_scene_guidance_alignment.py` | 通过；仓库未配置独立 Python 类型检查命令 | — |
| `python scripts/check_verification_gates.py --only arch-facts --only doc-links --only packaged-links` 与 `git diff --check` | 通过 | `build/gameplay-convergence-20261114/review-gates.log` |
| Python 计划后续步骤丢失响应的离线复现 | 确认现有误分类：步骤 `status=outcome_unknown`、`plan.stop_reason=rejected`、`final_state` 为前一步；没有自动执行下一步 | `build/gameplay-convergence-20261114/review-plan-unknown-repro.log` |

上一轮独立检查仅复跑上述受影响项；本轮修复后的全套结果另见下方。Git 空白检查与静态闸门承担本仓库适用的 lint 检查；Python 字节码编译是语法检查，不冒称类型检查。

## 未知结果修复的回归记录

- 修复前，在 `mcp_server/` 下运行 `uv run --locked python -m unittest discover -s tests -p test_play_plans.py -v`：13 项中首步和后续步骤的两项新回归失败，均得到 `rejected` 而非 `outcome_unknown`；普通确定拒绝通过。测试使用真实 `Sts2Client` 和模拟 HTTP 传输，确认动作提交次数及单次状态对账。日志：`build/gameplay-convergence-20261114/python-unknown-red.log`。
- 修复后，同一计划测试命令 13/13 通过（`python-unknown-plan-green.log`）；在同目录运行 `uv run --locked python -m unittest discover -s tests -p test_action_replay_safety.py -v`，25/25 通过（`python-unknown-transport-green.log`）。两份日志均在 `build/gameplay-convergence-20261114/`。
- 修复后全套，在 `mcp_server/` 下执行 `uv run --locked python -m unittest discover -s tests -v`：460 项、456 通过、4 失败，日志 `build/gameplay-convergence-20261114/python-unknown-full.log`。四项均是 `test_knowledge` 对无 checkout 临时目录的判定；该环境中 Python 在系统 TEMP 创建文件报 `PermissionError`，`tempfile.gettempdir()` 回退到了仓库内的 `mcp_server/`，使测试夹具的父目录含真实 checkout 标记。仓库外的第二工作区路径也被 Python 拒绝写入；将仓库内临时目录映射为盘符后，`Path.resolve()` 仍返回仓库路径，无法形成隔离夹具。没有改动无关测试或掩盖失败；需要可由 Python 写入且不在 checkout 内的临时目录后重跑。
- 本轮改动的 `uv run --locked python -m py_compile src/sts2_mcp/play_tools.py tests/test_play_plans.py`（在 `mcp_server/`）、`python scripts/check_verification_gates.py --only doc-links`（508 个相对链接通过）及 `git diff --check` 均通过。日志分别为 `build/gameplay-convergence-20261114/python-unknown-compile.log`、`unknown-doc-links.log`、`unknown-diff-check.log`。仓库没有配置独立 Python 类型检查器；本轮未重跑未变更的 C# 套件。

## 规模（LF 规范化后的字符数，不是 token）

- 主 SKILL：23126 → 21574 字符；272 → 254 行。最终值按 UTF-8 文本读取、CRLF/CR 归一为 LF 后计数。
- 嵌入共享合同：11046 → 11021 字符。最终值取两个 `SHARED PLAY CONTRACT` 标记之间的内容，归一行尾并去掉两端空白，和 `PlayPrompt.ExtractSharedContract` 一致。
- 公开工具集合、参数和动作执行逻辑保持兼容。

## 未验证

未调用真实游戏或模型服务商，未部署 Mod，未测量真实请求数、延迟或胜率；这些结果不能由文字收敛和离线套件推出。

## 追加环境修复复验（2026-11-14）

- 根因、独立原生探针、测试改动和命令的详细记录见 [environment-fix.md](environment-fix.md)。仓库内临时目录下 Python 全套：461 通过、0 失败、0 跳过；日志 `build/gameplay-convergence-20261114/environment/python-full.log`。
- C# 全套：1148 通过、1 失败、2 跳过；失败项为 `Playbook.StaticPromptBudget`（15075 字符，预算 15000），两项跳过为原生监听不可用时的真实 HTTP 验证。内存 Origin 策略测试和故意断言失败仍记作失败的 runner 回归通过；日志 `build/gameplay-convergence-20261114/environment/csharp-full.log`。实际监听与 HTTP 回环尚未验证。
- `python scripts/check_verification_gates.py`：12 项静态闸门全部通过；`git diff --check` 通过。日志分别为 `build/gameplay-convergence-20261114/environment/gates.log`、`build/gameplay-convergence-20261114/environment/diff-check-final.log`。全套后仅补充 Python 夹具辅助函数返回类型注解，聚焦知识测试 28/28 再次通过（`environment/python-knowledge-typed.log`）。

## 主会话集成收尾（环境修复前）

- 已同步 `docs/project-map.md`，移除修复前未知结果可能标为 rejected 的过时说明；现为 `outcome_unknown` 优先于通用 error，并保留 final_state 可能较早的提醒。
- 主会话曾尝试创建仓库外独立 TEMP，并在该进程中设置 TEMP/TMP/TMPDIR 后复跑 Python 全套。执行工具在创建进程前拒绝整条命令，仅返回 `blocked by policy`，命令未运行；不能将这次尝试写成复验通过。没有申请额外权限或绕过执行策略。
- 当时代码检查结果：新增计划 13/13、动作传输安全 25/25；Python 全套 456/460（4 个临时目录环境失败）；C# 全套 1148/1150（2 个旧监听器环境失败），后续相关 C# 聚焦 9/9、Python 场景聚焦 8/8 通过。环境修复及最终结果另见本记录。
- 项目地图同步后，再次运行 `python scripts/check_verification_gates.py --only doc-links --only arch-facts --only packaged-links` 与 `git diff --check`，全部通过。

## 最终预算修复和复验（2026-11-14）

- 精简 `PlayPrompt.PlaySystem` 的重复措辞，保留共享合同、新鲜状态、按需查询、未知步骤检查和禁止重放规则。系统提示减少 152 个字符；由修改前失败日志的 COMBAT 静态块 15075 字符减去这一差值，得到 14923 字符，低于原有 15000 字符预算。主 SKILL 和共享合同内容未改，规范化字符数仍分别为 21574 和 11021；字符不是 token。
- 首次复跑未显式设置临时目录，结果为 1082 通过、67 失败、2 跳过；其中 43 条直接报告系统 TEMP 路径访问被拒绝，其余 24 条为缺文件或断言等失败，`Playbook.StaticPromptBudget` 已通过。失败记录保留在 `build/gameplay-convergence-20261114/environment/csharp-budget-final.log`，不作为最终通过结果。
- 将 `TEMP`、`TMP` 和 `TMPDIR` 设置为仓库内 `build/gameplay-convergence-20261114/environment/tmp` 后，执行 `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release`：1149 通过、0 失败、2 跳过，退出码 0；日志 `build/gameplay-convergence-20261114/environment/csharp-budget-final-isolated.log`。其中 `Playbook.StaticPromptBudget` 与 runner 的失败/跳过分类回归通过；`NativeMcp.OriginHttpPolicy` 和 `LoopbackListener.RealLoopbackListenerResponds` 因独立原生探针确认 `HttpListener.IsSupported=False` 且 Windows 错误码为 6，明确记为 SKIP，真实 HTTP 路径仍未验证。编译包含在该命令中。
- 环境夹具复核：仅屏蔽夹具外祖先的 checkout 标记，夹具内标记及其他真实文件仍可见；回归覆盖这两侧边界。runner 仅捕获专用跳过异常，故意触发的断言失败仍记为 FAIL。
- Python 代码本轮未变，沿用环境修复后的全套 461 项通过、0 失败、0 跳过（`environment/python-full.log`）；随后只补充夹具类型注解，聚焦知识测试 28/28 再次通过（`environment/python-knowledge-typed.log`）。
- `python scripts/check_verification_gates.py --only doc-links --only arch-facts --only packaged-links` 通过，日志 `build/gameplay-convergence-20261114/environment/gates-budget-docs-final.log`；此前环境修复后 12 项全量静态闸门通过。`git diff --check` 通过（仅行尾转换提示）。仓库未配置独立 Python 类型检查器；未运行游戏、部署或调用模型。
