# v0.19.2 发布记录（2026-11-17）

> 历史快照：仅记录本次发布证据。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，跨版本指纹见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| PR / 提交 | [#243](https://github.com/CharTyr/STS2-Agent/pull/243) → dev；[#244](https://github.com/CharTyr/STS2-Agent/pull/244) → main；tag `v0.19.2` → `ad6aa1477fa6fa8689e7efcd954d5d925d3144e8` |
| GitHub Release | [v0.19.2](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.19.2)；`sts2-ai-agent-v0.19.2-windows.zip`，980927 字节；SHA256 `8D051D31872816144FA248EC0173274D58C2E05FB2AE89BEE513B5E81E527295` |
| Steam 工坊 | [3796486050](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050)，公开；12:29:59 新增上传 OK；Web API `file_size=2243207` 与本地相同，`hcontent_file=5182684242015483018`，`time_updated=1794889798` |
| 共用 DLL | SHA256 `47F358AA3EBD73B6BBD5249C7D11F46D3F9746F3F081AFD56577E4CAF0506BF6`；两份产物均来自干净 `ad6aa14` |
| 指纹 | `build/release/sts2-ai-agent-v0.19.2-windows-fingerprint.json`（33 文件 / 2873859 字节）；`build/steam-workshop/sts2-ai-agent-v0.19.2/build-fingerprint.json`（5 文件 / 2243207 字节） |

## 改动与验证

- 联机回到根菜单后再次邀请会正常回收旧队友，过期连接不再隐藏入口；选角指引尊重指定角色。
- 内置游玩/聊天逐轮检查上下文估算、固定最近决策记忆，并导出有界分项用量；保留稳定前缀缓存设计。估算不是精确 tokenizer。
- 完整发布预检通过：Release 0 警告/0 错误，C# 1161 通过/2 环境跳过（HttpListener 独立探测 Windows error 6），Python 462 通过，12 静态门通过。两条 PR 的 contracts 检查均通过，Sourcery 跳过。
- 发布候选在游戏 v0.113.1 独立可见实例加载，`/health` 返回 0.19.2，28 反射成员无缺失，主菜单状态一致性通过。未调用付费模型，未实测 token 节省或连续两局完整邀请。
- 真实截图为单人面板、队友邀请和快速设置；独立测试档使用空密钥、无私人聊天。三张 JPEG 均小于 1 MB，已加入中英文 README 和工坊附加预览。
- 测试窗口正常退出，原本地 Mod 三文件已恢复；未改真实玩家存档。未单独验收订阅下载副本的游戏加载。

## 上传边界

- 首次 Steam 登录等待超时，尚未提交上传。第二次重新登录后上传成功；成功依据是新增 Workshop OK 日志、上传器成功输出及 Web API 内容大小复核。
- 系统代理 `ProxyEnable` 已恢复为 1。没有重新创建工坊物品；沿用既有公开物品。
- 工坊简体中文列表的独立翻译文案仍未通过网页编辑更新；本次新增预览图与英文默认说明由上传器提交。
