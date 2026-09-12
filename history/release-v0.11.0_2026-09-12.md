# v0.11.0 发布与工坊上传记录（2026-09-12）

## 结论

GitHub Release 与 Steam 工坊上传**都已完成**。

| 项 | 值 |
| --- | --- |
| 发布提交 | `84631b9`（feat(i18n)），已推送到 `main` |
| CI | Validate run `34629717120` → success |
| Tag | `v0.11.0` → `84631b9` |
| Release | https://github.com/CharTyr/STS2-Agent/releases/tag/v0.11.0 ，资产 `sts2-ai-agent-v0.11.0-windows.zip`（677015 字节） |
| 工坊物品 | `3796486050`，`time_updated = 2026-09-12 02:33:39` |
| 工坊 manifest | `3781676487912021003` |
| 工坊 `file_size` | `1135844` —— 与 v0.11.0 内容字节和**完全相等**（dll 1096704 + pck 608 + json 381 + README 3514 + LICENSE 34637） |
| 可见性 | `0`（公开），未被回落为私有 |
| 说明文案 | 已含新行 `The whole interface follows your game language...`（2526 字符） |

版本号五处同步（`mod_manifest.json` / `mod_id.json` / `Router.cs` / `pyproject.toml` / `uv.lock`），`check_release_metadata.py` 通过。

## 差点误判：卡住 ≠ 失败

本次上传在客户端里显示为长时间停在 `k_EItemUpdateStatusPreparingConfig`（约 14 分钟无进展、不报错），我在第 5 次尝试后手动结束进程并判为失败。**实际它随后自己成功了。**

判错的原因：只看 `ModUploader.exe` 的 stdout 和它自己的 `mod-uploader.log`（该文件只在结束时才写），没有看 Steam 客户端的日志。**Steam 客户端的日志才是权威。**

```
C:\Program Files (x86)\Steam\logs\workshop_log.txt
```

其中本次的关键几行：

```
[2026-09-12 02:02:30] Upload starting for workshop item 3796486050 by AppID 2868840
[2026-09-12 02:16:55] Upload workshop item 3796486050 failed (Failed to download manifest
                      "steampipe-partner.akamaized.net/depot/2868840/manifest/.../5/..." (timeout))
[2026-09-12 02:33:35] Upload workshop item 3796486050 failed (Failed to download manifest ... (timeout))
[2026-09-12 02:33:40] Uploaded new content ( ManifestID 3781676487912021003 ) for item 3796486050.
[2026-09-12 02:33:41] Upload finished for workshop item 3796486050 : OK
```

## 真正的卡点

不是代理模式，也不是 `ModUploader` 本身：**Steam 客户端拉取物品的旧 manifest 时超时**。

- 主机是 `steampipe-partner.akamaized.net`（Akamai）。本机网络下这个域不稳定：本次共 8 条相关失败记录，全部是它。
- 同一时期 Steam 的其他 CDN 请求是好的，走的是国内镜像：`st.dl.eccdnx.com`(999)、`dl.steam.clngaa.com`(937)、`xz.pphimalayanrt.com`(720)。
- 这个拉取发生在 `PreparingConfig` 阶段、由 Steam 客户端发起 —— `ModUploader.exe` 在这段时间**没有任何 TCP 连接**（实测），所以上传器进程上的 `HTTP_PROXY` 等变量影响不到它，这也解释了为什么加代理变量只推过了 `PreparingContent` 就再也推不动。
- 每次尝试会在这一步耗约 14 分钟后记失败，然后**下一次重试可能就成功**。这不是 v0.11.0 独有：v0.10.7 那次（01:10:16 失败 → 01:10:58 重试 → 01:11:03 成功）也是同一模式。

## 下次怎么做

1. **判成败一律看 `workshop_log.txt` 的 `Upload finished ... : OK`**，并用 Web API 复核 `time_updated` / `file_size`，不要看上传器 stdout：
   ```powershell
   Invoke-WebRequest -Uri 'https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/' `
     -Method POST -Body 'itemcount=1&publishedfileids%5B0%5D=3796486050' `
     -ContentType 'application/x-www-form-urlencoded' -Proxy 'http://127.0.0.1:10808'
   ```
2. **别急着杀进程**：单次尝试可能耗 15 分钟以上，且有内部重试。给足 20–30 分钟，或失败后原样重跑一次。
3. 若要降低这一步的不确定性，可把 `steampipe-partner.akamaized.net` 也走代理（v2rayN 分流规则），或换线路。
4. 上传命令与观察方式见下；`-2` 后缀的工作区是重打包副本，内容与首个工作区等价。

```powershell
Start-Process -FilePath 'C:/Users/chart/AppData/Local/sts2-mod-uploader/win-x64/ModUploader.exe' `
  -ArgumentList @('upload','-w','<workspace>','-i','3796486050') `
  -WindowStyle Hidden -PassThru `
  -RedirectStandardOutput "$env:TEMP/moduploader-stdout.txt" `
  -RedirectStandardError  "$env:TEMP/moduploader-stderr.txt"
```

## 关于 TUN

事后检查：`xray_tun` 网卡（ifIndex 61）虽为 Up，但**没有安装任何有效路由**（只有 link-local / multicast），默认路由仍在以太网与 Radmin VPN 上。所以"TUN 已开"在这台机器上并未真正接管流量 —— 这一点与本次结论无关（Steam 的 HTTP 层本来就通过系统代理 `127.0.0.1:10808` 正常工作，`content_log` 里可见 `124.72.137.38:80 / 127.0.0.1:10808`），但下次若要依赖 TUN，需要先确认 `Get-NetRoute | Where ifIndex -eq <tun>` 里有实际的 `0.0.0.0/0` 或分流路由。

## 本轮顺手修掉的不一致

首次打包时，我插入的一行是 LF，而说明文件其余部分是 CRLF，导致生成的 `workshop.json` 里 `description` 混用两种换行。已把仓库内的 `description.en.txt` / `description.zh-CN.txt` 归一化为 CRLF 并重新打包；`git diff` 为空，说明仓库副本本来就是 CRLF 形态，这一条修的是我的插入。上到工坊的是修正后的版本。

## 本次的环境影响（已恢复）

- 为让 Steam 读取系统代理，重启过一次 Steam（当时没有游戏在跑）；重启后正常登录（`76561198420578597`）。
- 测试期间改动过游戏语言与窗口尺寸的存档，两处（Steam 档案与隔离的 `clientId` 档案）均已按备份还原为 `zhs` / 1920×1080。

