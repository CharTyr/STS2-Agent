# v0.19.1 发布记录（2026-11-15）

> 历史快照：仅记录本次发布时的证据。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，跨版本指纹见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| 发布 PR / 提交 | [#240](https://github.com/CharTyr/STS2-Agent/pull/240) → dev；[#241](https://github.com/CharTyr/STS2-Agent/pull/241) dev → main；tag `v0.19.1` → `ec7765d0ce5989429cf5ea34a122637659d43474` |
| GitHub Release | [v0.19.1](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.19.1)；`sts2-ai-agent-v0.19.1-windows.zip`，972236 字节；SHA256 `135940AE0AFFAE2EEBA204F9FB9A9522ADBFD57571C0C116A1CF385D17EC4CCE` |
| Steam 工坊 | 物品 `3796486050`，公开；2026-11-15 00:11:40 新增 `Upload finished ... : OK`；Web API `file_size=2219667`，与本地 content 相同，`hcontent_file=4745456122012696730` |
| 共用 DLL | SHA256 `5AD7FD55AD2DC181BFC568D682AE7CB5EDFBFEBDBDED13828E934A432FEDF82C`；两个产物从干净的 `ec7765d` 打包，DLL 相同 |
| 构建指纹 | `build/release/sts2-ai-agent-v0.19.1-windows-fingerprint.json`（33 文件 / 2848187 字节）；`build/steam-workshop/sts2-ai-agent-v0.19.1/build-fingerprint.json`（5 文件 / 2219667 字节）。均为 gitignored 本机产物 |

## 变更与验证

- 合并游戏内与外部游玩指导，复用新鲜状态、按需查询元数据；修正 Python MCP 计划把未知结果误报拒绝的问题，不自动重放动作。
- 发布预检通过：Python 461 项通过；C# 1149 通过、2 项原生 HttpListener 测试因当前环境不支持而跳过；12 项静态闸门通过。预检使用仓库内隔离 TEMP。
- GitHub 两条 contracts 均通过。功能 PR 的 Sourcery 通过，发布 PR 的代码树与之相同；合并时发布 PR 的 Sourcery 仍待处理。
- 最终发布 DLL 在游戏 v0.113.0 的隔离无头实例加载，健康与主菜单状态一致性检查通过；28 个反射成员无缺失。该测试未调用模型，原安装文件已恢复并核对哈希。
- 发布前 CI 发现 Windows 临时目录短路径与长路径比较导致夹具误判；`5694977` 规范化路径后，相关测试及后续 CI 通过。

## 工坊上传与边界

- 首次上传器在 Steam 尚未完成登录时初始化失败，尚未提交上传；改用明确 `[Logged On,` 状态后重试成功，Steam 成功日志计数 5 → 6。系统代理 `ProxyEnable` 已恢复为 1。
- Steam 客户端随后报告下载任务完成；未单独验证订阅副本在游戏中加载。工坊简体中文列表仍需网页端手工粘贴 [description.zh-CN.txt](../steam-workshop/description.zh-CN.txt)。
- 本版未复验真实模型自动游玩、多人局和两项真实 HTTP 监听测试；没有以离线测试替代这些证据。
