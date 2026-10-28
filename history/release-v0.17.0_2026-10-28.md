# v0.17.0 发布记录（2026-10-28）

> 历史快照：仅记录本次发布时的证据。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，跨版本指纹见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| 版本 | 0.17.0（五个版本文件一致；`check_release_metadata.py` 通过） |
| 发布 PR | [#229](https://github.com/CharTyr/STS2-Agent/pull/229)（dev → main）；该次合并的 `contracts` 因 `docs/api.md` 仍写 0.16.2 失败，随后由 [#230](https://github.com/CharTyr/STS2-Agent/pull/230) 修正 `docs/api.md` 与 `docs/openapi.json` 并合入；`fdc50fd` 的 Validate 通过，dev 已快进至 main。 |
| tag / 源码提交 | `v0.17.0` → `fdc50fd4d4db50f21065a04b9559d9aa8bc6d848`（PR #230 合并提交；两个包的指纹均为干净工作区） |
| GitHub Release | [v0.17.0](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.17.0)，资产 `sts2-ai-agent-v0.17.0-windows.zip`，832523 字节，SHA256 `0F45C9F5FB023B76DABA5145F832A820C659B7AA7551E5EB70371A327762F783` |
| Steam 工坊 | 现有公开物品 `3796486050`；`file_size` 1830547 与本地 `content/` 总字节数一致；`time_updated` 2026-10-28 15:35:52 +08:00；`hcontent_file` `6209544728986056386`；`visibility=0` |
| DLL SHA256 | `0381E3661508B4063A2822EC98284C3619550F33BDE2E158FEC8A8B35C926CBD`（GitHub 包与工坊包一致） |
| 构建指纹 | GitHub 包：30 个文件、2404656 字节；工坊 content：5 个文件、1830547 字节 |

## 内容

快速配置新增多家服务商（智谱 GLM、Kimi、通义千问、OpenRouter 等）与「获取 Key」、模型下拉；新装默认请求上限 1500；因预算/配置/队友退出停下时标题栏「需处理」、单人页「下一步」与「清零计数」；「重置统计」更名「清零计数」并移到「决策记录」卡片；屏幕名、动作名、来源改为中文；高级设置的思考选项改为中文说明而配置文件仍存英文 id。修复快速判断把「保存并退出」当候选、熔断到期提示不刷新、窄窗口长模型名与长错误撑宽裁切、设置保存失败静默切页、读档拦截按钮名。网关把流式工具参数截断成 `{"action":"play_card"}` 时，act 回合改用非流式重发一次（缓解，非根治）。详见 [CHANGELOG.md](../CHANGELOG.md) 的 0.17.0 段与第五轮实机报告。

## 验证与限制

- 离线：C# 1032/1032（含 2 项新增的流式参数回归）、Python 439/439、验证门禁 12/12；Release 构建 0 警告 0 错误；GitHub 压缩包通过 `check_release_package.py`。
- CI：见上表；`#229` 的失败是文档版本未同步，不是代码问题；`#230` 合入后 `fdc50fd` 的 Validate 通过。
- 第五轮实机（隔离档，`--clientId 2026102705`）：M1 预算停止/清零/继续、N1–N8 快速判断、M0 主模型、M3 密钥复检、O2 外部 Agent 地址、V9 主题、V10 窄宽高（1200×560/500）、V11 帮助与 emoji、英文界面、F8 均通过；N9 由 Char 亲手按键确认。
- **未完成**：M2 第二次 120 秒熔断与 O1 完整一局受当前网关截断工具参数阻塞（流式与非流式都只回 `{"action":"play_card"}`，连续 3 次后自动停止），因此没有整局请求数/Token 的校准数据；O3 因隔离档没有本地联机存档未做。以上不冒充实机通过。

## 工坊上传

1. 从干净的 `fdc50fd` 用 `package-steam-workshop.ps1 -PublishedFileId 3796486050 -Visibility public` 打包，得到 `build/steam-workshop/sts2-ai-agent-v0.17.0/`（content 1830547 字节，5 个文件）。
2. 按本机经验先关闭系统代理（原 `127.0.0.1:10808`）、重启 Steam，再执行 `ModUploader.exe upload -w <workspace> -i 3796486050`。
3. Steam 日志本物品成功行从 0 → 1（当前 `workshop_log.txt` 已不含更早记录），新增 `[2026-10-28 15:35:53] [AppID 2868840] Upload finished for workshop item 3796486050 : OK`；ManifestID `6209544728986056386`；上传程序退出码 0。
4. `finally` 恢复代理 `ProxyEnable=1`；Steam Web API 返回 result=1、物品公开、`file_size` 与本地 content 相等。

## 仍需手工

- 工坊**简体中文列表**仍需在网页端粘贴 [description.zh-CN.txt](../steam-workshop/description.zh-CN.txt)；ModUploader 无语言参数。
- 换用能稳定回传完整工具参数的模型后，补做 M2 第二次熔断与 O1 完整一局。
