# AI Debug CLI 查询工具

`crowd_ai_debug_query.py` 用于查询 `.workspace/artifacts/ai-debug/` 下由 AI Debug 导出的 `crowd_ai_debug_*.jsonl`、同名 `.md`、`external-player-profile.json` 和 `repro-traces/*.json`。

它不是一次性上下文包，也不会输出 `suggested_next_queries`。LLM 应该先建立认知地图，再根据问题自己决定继续查 provider、frame、agent id、phase 还是 raw evidence。

## 基本用法

默认读取最新的 AI Debug JSONL：

```powershell
python tools/ai-debug-cli/crowd_ai_debug_query.py session map
```

指定录制文件：

```powershell
python tools/ai-debug-cli/crowd_ai_debug_query.py --input .workspace/artifacts/ai-debug/crowd_ai_debug_20260515_104845.jsonl session map
```

输出 JSON：

```powershell
python tools/ai-debug-cli/crowd_ai_debug_query.py session map --format json
```

## 分层命令

第零层：认知地图

- `session map` / `runtime map`：看录制窗口、问题声明、provider 覆盖、缺席 provider、domain 分工依赖、候选实例和证据质量信号。

第一层：粗粒度状态视图

- `session overview`：录制摘要、provider 数量、`frame.dispatch` 全局状态。
- `discovery overview`：GPU 自动发现候选池、`found/recorded/capacity`、reason 分布、候选实例。
- `resource overview`：`gpu.resource` 的 bound/count/stride，用于判断证据链是否可靠。
- `repro overview`：自动回放 trace 的场景、时长、pose 数量和 commandEvents。

第二层：中粒度追问

- `frame inspect --frame N`：围绕某帧看 provider 分布、dispatch 状态、discovery 摘要、phase counts、top ids 和资源信号。
- `agent timeline --id N`：按实例串起稀疏记录，观察字段何时变化。
- `stage phases [--id N] [--phase solve.after]`：看 `gpu.stage.ph` 阶段覆盖和字段变化分布。
- `provider records --name gpu.stage [--id N] [--frame N]`：只看某个 provider 的局部记录。

第三层：原始证据验证

- `raw show --provider gpu.discovery --frame N --limit 5`：输出匹配的原始 JSONL record。
- `raw grep "field-or-keyword"`：按原始行搜索字段、provider、id 或异常值。

## 使用原则

- 先用 `session map` 建立“哪些系统在协作、哪些证据存在/缺席”的认知地图。
- 不要把 `gpu.discovery.reason` 当成 bug 原因；它只表示候选来源。
- 不要把 `gpu.stage.ph` 当成原因；它只定位变化发生在哪个阶段之后。
- 如果 `found > recorded == capacity`，先把它视为候选池截断风险，再决定是否扩大容量或缩小录制范围复录。
- raw 层只在已有假设时验证，不作为第一眼理解入口。
