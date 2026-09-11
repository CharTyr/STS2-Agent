# v0.11.0 发布与工坊上传记录（2026-09-12）

## GitHub Release

- 提交：`84631b9`（feat(i18n): follow the game language in the overlay and the state payload），已推送到 `main`。
- CI：Validate run `34629717120` → success。
- Tag：`v0.11.0`（指向 `84631b9`）。
- Release：https://github.com/CharTyr/STS2-Agent/releases/tag/v0.11.0 ，资产 `sts2-ai-agent-v0.11.0-windows.zip`（677015 字节）。
- 版本号五处同步（`mod_manifest.json` / `mod_id.json` / `Router.cs` / `pyproject.toml` / `uv.lock`），`check_release_metadata.py` 通过。

## 工坊上传

物品：`3796486050`（STS2 AI Agent）。工作区：`build/steam-workshop/sts2-ai-agent-v0.11.0/`，内容版本 `0.11.0`，`STS2AIAgent.dll` 1096704 字节。

**结果：未完成。** 卡在 `k_EItemUpdateStatusPreparingConfig`，超过 6 分钟无进展、无报错，手动结束进程。物品仍是 v0.10.7，没有发生半成品覆盖（Steam 在 CommittingChanges 之前不会改物品）。

### 观察到的阶段推进

| 尝试 | 环境 | 结果 |
| --- | --- | --- |
| 1 | 无代理环境变量 | 卡在 `PreparingContent`，3 分钟无进展 |
| 2 | `HTTP_PROXY`/`HTTPS_PROXY`/`ALL_PROXY` | 越过 `PreparingContent`，卡在 `PreparingConfig` |
| 3 | 同上，且先重启 Steam（代理已是系统代理） | 越过 `PreparingContent`，卡在 `PreparingConfig` |
| 4 | 只留 `HTTP_PROXY`/`HTTPS_PROXY`（去掉 socks5 的 `ALL_PROXY`） | 越过 `PreparingContent`，卡在 `PreparingConfig` |
| 5 | 修正说明文件换行后重打包（`sts2-ai-agent-v0.11.0-2`），同上 | 越过 `PreparingContent`，卡在 `PreparingConfig` |

说明文件原本是 CRLF，本轮插入的一行是 LF，导致生成的 `workshop.json` 里 `description` 混用两种换行。已归一化为 CRLF 并重新打包（`git diff` 为空，说明仓库副本本来就该是这个形态）。这一条不是卡住的原因，但属于本轮引入的不一致，已修掉。

结论：**代理环境变量是越过内容阶段的关键**（此前那一步必卡），但配置阶段不再受它影响。

### 诊断

1. 直连能力不对称：`store.steampowered.com` 直连 200；`api.steampowered.com` 直连超时，经代理 200。物品元数据提交走的是后者，与"只有配置阶段卡住"一致。
2. `ModUploader.exe` 自身没有任何 TCP 连接 —— 配置阶段由 **Steam 客户端**发起，上传器只是在轮询 `ISteamUGC` 状态，所以上传器进程上的代理变量管不到这一段。
3. Steam 客户端侧没有可用配置项：它不读 `HTTP_PROXY`，只认系统代理；系统代理（`HKCU\...\Internet Settings`）本次已是 `127.0.0.1:10808` 且 Steam 是在其之后重启的，仍未越过。
4. 代理内核 `xray`（v2rayN 的 core，PID 与监听 10808 一致）当时存在 `SYN_SENT` 到上游服务器的连接，即上游本身不稳。

### 物品未被污染

`https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050` 仍显示 `File Size 1.088 MB`（与 v0.10.7 的 1088228 字节一致），更新时间未刷新。Steam 在 `CommittingChanges` 之前不会改动物品，所以四次失败都没有留下半成品。

### 下次的做法（按代价排序）

1. 让代理透明化：v2rayN 开 TUN 模式（或把 `*.steampowered.com` / `*.steamcommunity.com` 明确走代理），这样 Steam 客户端的调用也会被接管，再跑同一条 `ModUploader.exe upload` 命令。
2. 或换一个能直连的节点/线路后重试，并保持系统代理开启、Steam 在代理开启后启动。
3. 上传命令本身不用改：

```powershell
Start-Process -FilePath 'C:/Users/chart/AppData/Local/sts2-mod-uploader/win-x64/ModUploader.exe' `
  -ArgumentList @('upload','-w','<workspace>','-i','3796486050') `
  -WindowStyle Hidden -PassThru `
  -RedirectStandardOutput "$env:TEMP/moduploader-stdout.txt" `
  -RedirectStandardError  "$env:TEMP/moduploader-stderr.txt"
```

进度看 `$env:TEMP/moduploader-stdout.txt` 的 `Status:` 行；`mod-uploader.log` 只在结束时才写。

### 本次的环境影响（已恢复）

- 为让 Steam 读取系统代理，重启过一次 Steam（当时没有游戏在跑，朋友的游戏列表亦无本机游戏）；重启后 Steam 正常登录（`76561198420578597`）。
- 测试期间改动过游戏语言与窗口尺寸的存档文件，两处（Steam 档案与隔离的 `clientId` 档案）均已按备份还原为 `zhs` / 1920×1080。
