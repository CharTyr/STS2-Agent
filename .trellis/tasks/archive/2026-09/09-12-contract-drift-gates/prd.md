# 给"会静默漂移"的契约加门禁

## 背景

本轮调研确认了几处**改坏了不会被任何门禁发现**的契约，其中三处已经真实漂移：

1. **`docs/api.md:124` 的 `mod_version` 是裸事实**：文档自己写明"与 `Router.cs` 的 `ModVersion` 一致"
   （`:150`），但 api-doc 门禁只比对动作名集合
   （`scripts/check_verification_gates.py:342-355`），`check_release_metadata.py` 完全不读 docs。
   下次升版本号，文档会静默停在旧值。
2. **屏幕清单没有门禁**：代码侧由 `ScreenResolutionContractTests` 钉住 switch 表达式的 26 项，
   但**不含** `GameStateService.ResolveNonModalScreen` 里 `NUnlockScreen` / `CARD_SELECTION` /
   `CARDS_VIEW` / `MULTIPLAYER_LOBBY` / `COMBAT` 这些早退分支；`docs/api.md:70-95` 的屏幕枚举
   与 `skills/sts2-mcp-player/SKILL.md` 的 Screen Routing 全靠人工对齐。
3. **`mcp_server/README.md` 的工具清单已漂移**：`_LEGACY_ACTION_TOOLS` 共 54 项，
   README 的"当前工具"节实测缺 14 项（`switch_profile`、`continue_game_over`、
   `dismiss_game_over_wait`、`confirm_unlock`、`close_cards_view`、`unready`、
   `increase_ascension`、`decrease_ascension`、`host_multiplayer_lobby`、
   `join_multiplayer_lobby`、`ready_multiplayer_lobby`、`disconnect_multiplayer_lobby`、
   `invite_ai_teammate`、`confirm_selection`），而没有任何测试读 README。

另外一处流程缺口：`scripts/check_release_package.py --source-root .` 是**纯静态**检查
（只读源文件，不需要游戏），却只挂在本地 preflight 上，CI 不跑
（`.github/workflows/validate.yml` 只有 8 个步骤，均不含它）。

## 目标

1. `check_verification_gates.py` 增加一个 `api-facts` 门禁（纯文本、零依赖），至少覆盖：
   - `docs/api.md` 的 `"mod_version"` == `STS2AIAgent/mod_manifest.json` 的 `version`；
   - `docs/api.md` 屏幕枚举里的每个屏幕名都出现在代码里，且代码里 `ResolveNonModalScreen`
     产出的屏幕名集合**不超出**文档集合（新增屏幕必须同步文档）；
   - `docs/api.md` 的默认端口与 `HttpServer` 的默认值一致。
2. 修掉 `mcp_server/README.md` 的工具清单漂移，并加一条测试把 README 清单与
   `_LEGACY_ACTION_TOOLS` 绑定（改代码不改文档即红）。
3. 把 `check_release_package.py --source-root .` 加进 CI（`.github/workflows/validate.yml`）。
4. `scripts/test-verification-gates.ps1` 补上新门禁的自测用例（该脚本的存在意义就是"门禁自己也要被测"）。

## 验收标准

- [x] `python scripts/check_verification_gates.py` 输出新增 `[gate] api-facts: ok` 及其明细行。
- [x] 三条断言各自可否证：临时改坏一处（版本号 / 屏幕名 / 端口）→ 该 gate 变红并给出可读原因 → 还原。
- [x] `scripts/test-verification-gates.ps1` 对新门禁有正反用例，且整体仍通过。
- [x] `mcp_server/README.md` 的工具清单与 `_LEGACY_ACTION_TOOLS` 一致（测试断言双向集合）。
- [x] `.github/workflows/validate.yml` 增加 `check_release_package.py --source-root .` 步骤
      （本地复现该命令必须 exit 0）。
- [x] 既有门禁全部保持绿（`api-doc` 仍报 55 actions match）。

## 范围外

- 不给 `docs/api.md` 的全部字段表加门禁（20+ 张表，收益/成本不划算）。
- 不改 `docs/setup.md`、`docs/reverse-engineering.md` 里过期的游戏版本号
  （需要真实环境事实，属人工维护项，另行记录）。
- 不给 mod 主工程加 CI 编译（CI 无 `sts2.dll`/`GodotSharp.dll`；本机 preflight 已覆盖该步）。
