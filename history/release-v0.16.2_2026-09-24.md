# v0.16.2 发布记录（2026-09-24）

> 历史快照：仅记录本次发布时的证据。当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)，跨版本指纹见 [build-fingerprints.md](build-fingerprints.md)。

| 项 | 值 |
| --- | --- |
| 版本 | 0.16.2（五个版本文件一致；`preflight-release.ps1` 通过） |
| 发布 PR | [#201](https://github.com/CharTyr/STS2-Agent/pull/201)（dev → main），两组 `contracts` CI 通过；随后 dev 快进至 main |
| tag / 源码提交 | `v0.16.2` → `829ba230e79839ea26dcb9749bf93f268adff697`（PR 合并提交；两个包的指纹均为干净工作区） |
| GitHub Release | [v0.16.2](https://github.com/CharTyr/STS2-Agent/releases/tag/v0.16.2)，资产 `sts2-ai-agent-v0.16.2-windows.zip`，783680 字节，SHA256 `97EFF7FF6616145DD80103EE5B4F41EC62AD5B519CAC3AD9FA83014DBE786A3C` |
| Steam 工坊 | 现有公开物品 `3796486050`；`file_size` 1655271 与本地 `content/` 总字节数一致；`time_updated` 2026-09-24 15:28:17 +08:00；`hcontent_file` `6156005224561293621`；`visibility=0` |
| DLL SHA256 | `52AD458FD2922306ACBDEA8CBAF83A9F1DAF888A786C43DE5A947FE1C2458FD6`（GitHub 包与工坊包一致） |

## 内容

双层决策回退继承规划器目标；规划器状态保证可解析且极端大帧保留战斗骨架；逐回合 Jev 选项与概率证据、回退标记、面板计数；只限 loopback 的脱敏 `GET /diagnostics`；流式思考有界显示预览；截图绘制帧等待断开无效连接；覆盖层宽度诊断在异常数量变化时再报。详见 [CHANGELOG.md](../CHANGELOG.md) 的 v0.16.2 段。

## 验证与限制

- 离线：C# 882、Python 423、发布预检及 PR #201 两组 `contracts` CI 通过；GitHub 压缩包通过 `check_release_package.py`。发布前预检先发现 `scripts/test-api-schema.py` 对新增 `/diagnostics` 仍写死 17 条路由，补齐为 18 条后重跑通过。
- 隔离 Steam-off 游戏冒烟（发布版本号升级前的同一功能构建）：`GET /diagnostics` 返回脱敏诊断；单人模式邀请队友返回 409，`dual_launch_outcome=Rejected` 且原因可读；游戏退出后真档前后哈希一致。此冒烟并非最终 0.16.2 包实机加载验证，也未调用付费模型/Jev。
- **未实机**：新 Jev 回退统计、极端战斗骨架、长推理性能及截图超时的真实端到端路径；Steam-on 完整 AI 队友/联机路径；订阅目录中的本版加载。以上仅有离线测试或代码级验证，不冒充实机验收。

## 工坊上传

1. 从干净的 `829ba23` 打包现有公开物品。按本机经验暂关系统代理、重启 Steam 并等待新 `Logged On` 后调用 `ModUploader.exe upload -w <0.16.2 workspace> -i 3796486050`。
2. 工坊日志本物品成功行从 **28 → 29**；新增 `[2026-09-24 15:28:18] [AppID 2868840] Upload finished for workshop item 3796486050 : OK`，上传程序退出码 0。
3. `finally` 恢复代理 `ProxyEnable=1`（`127.0.0.1:10808`），**未在恢复后重启 Steam**。Steam Web API 返回 result=1，物品公开、字节数和本地包一致。

## 仍需手工

- 工坊**简体中文列表**仍需在网页端粘贴 [description.zh-CN.txt](../steam-workshop/description.zh-CN.txt)；ModUploader 无语言参数。
- 订阅加载、Steam-on 队友与付费模型/Jev 真机长局尚未做。
