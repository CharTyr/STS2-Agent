# 环境失败复现与修复记录

日期：2026-11-14。范围仅限离线测试环境隔离、原生监听能力识别及测试结果报告；不修改产品解析或监听逻辑。

## 修复前复现

- `mcp_server/` 下将 `TEMP`、`TMP`、`TMPDIR` 指向 `build/gameplay-convergence-20261114/environment/tmp`，执行 `uv run --locked python -m unittest discover -s tests -p test_knowledge.py -v`。27 项中 4 项失败：无 checkout 的根路径解析、默认目录回退、planner 和 combat 参考文件。夹具位于真实 checkout 内，向上遍历时遇到其 `mcp_server/pyproject.toml`。日志：`build/gameplay-convergence-20261114/environment/python-knowledge-red.log`。
- 独立 .NET 9 控制台程序直接使用 `System.Net.HttpListener`，未调用产品代码；Windows 10.0.26200、.NET 9.0.20，`HttpListener.IsSupported=False`，TCP 回环临时端口分配成功，`HttpListener.Start` 抛出 `HttpListenerException`，`NativeErrorCode=6`、`HResult=0x80004005`（句柄无效）。代码及日志：`build/gameplay-convergence-20261114/environment/probe/`、`build/gameplay-convergence-20261114/environment/http-listener-probe.log`。
- 先前全套 C# 日志中的两处同类失败为 `NativeMcp.OriginHttpPolicy` 和 `LoopbackListener.RealLoopbackListenerResponds`；独立内存 Origin 策略测试先前通过。这份探针仅证明当前环境原生监听不可用，不证明真实监听路径通过。

## 修复原则

- 在测试中隔离夹具边界外的 checkout 标记探测，保留夹具内真实文件、真实 `_repo_root` 和正向解析断言。
- 仅当独立原生探针确认 Windows 原生监听不受支持且启动返回错误码 6 时显式跳过两个真实 HTTP 测试；其它异常和断言仍报失败。运行器分别统计并显示通过、失败和跳过。
- 验证期间不修改系统权限、不写仓库外目录、不调用真实游戏或模型。

## 当前实施及结果

- 四个无 checkout 测试仅在调用真实解析逻辑期间屏蔽夹具外祖先 checkout 标记；新增测试证明外部标记原本会被发现、隔离后其他真实文件仍可见、夹具内 checkout 标记仍可被发现。聚焦 `test_knowledge.py`：28/28 通过，日志 `build/gameplay-convergence-20261114/environment/python-knowledge-green.log`；补充返回类型注解后，受影响测试再次 28/28 通过，日志 `python-knowledge-typed.log`。
- 两个真实监听测试分别先运行独立探针；仅 `IsSupported=False`、Windows、`HttpListener.Start` 原生错误 6 三者同时满足时抛出测试专用跳过异常。运行器分别列出 PASS/FAIL/SKIP 并汇总；新增回归验证同一批中故意断言失败仍计作 FAIL。
- 仓库内 TEMP、离线 uv 缓存下运行 Python 全套：461 项通过、0 失败、0 跳过，日志 `build/gameplay-convergence-20261114/environment/python-full.log`。
- 仓库内 TEMP 下运行 C# 全套：1148 通过、1 失败、2 跳过，日志 `build/gameplay-convergence-20261114/environment/csharp-full.log`。两项被跳过的均为真实 HTTP 测试，内存 Origin 策略断言和新增运行器回归通过。额外失败 `Playbook.StaticPromptBudget`：COMBAT 静态提示词 15075 字符、预算 15000；与本环境测试修复不在同一文件范围，已交由主会话处理。不得把两项 SKIP 记为通过。
- `python scripts/check_verification_gates.py`：12 项静态闸门通过（含路径解析器 29 项检查），日志 `build/gameplay-convergence-20261114/environment/gates.log`。`git diff --check` 通过，日志 `build/gameplay-convergence-20261114/environment/diff-check-final.log`（只有 CRLF 转换提示）。
