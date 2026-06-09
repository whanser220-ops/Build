# GPU Pass Catalog AI 填充规则

`gpu_pass_catalog.yaml` 和 `shaders/*.compute.ai.yaml` 是 AI/人长期维护的结构化知识库，不是脚本生成物。

## AI 填充流程

1. 先定位最小相关代码：触发 dispatch 的 C# wrapper、ComputeShader、kernel 函数、相关 HLSL include、资源绑定函数。
2. 给 pass 分配稳定 `pass_id`，不要沿用临时 RenderDoc event 名作为主键。
3. 填写 `display_name`、`shader`、`kernel`、`source`，这些字段必须能回查到本地文件。
4. 填写 `purpose`，用一两句话描述这个 GPU pass 在系统里的职责，不写本次截帧结论。
5. 填写 `inputs` / `outputs`，重点写资源语义和访问方向，不要求枚举每个常量。
6. 填写 `dispatch`，描述并行粒度、线程组和 group count 公式。
7. 填写 `performance_hints`，只写设计层风险，例如访问模式、atomic、groupshared、分支发散风险；不要写“这次就是瓶颈”。
8. 如果证据不足，字段留空或写待确认问题，不要让 Python 或 AI 编造。

## 修改同步流程

当本地代码修改命中以下任一项时，AI 必须同步维护 catalog：

- `#pragma kernel` 新增、删除或重命名。
- kernel 函数的职责或数据流改变。
- buffer / texture / constant 的读写方向改变。
- dispatch group count、thread group size 或并行粒度改变。
- C# GPU marker、dispatch wrapper、debug registry 或 pass alias 改变。
- Shader 拆分、pass 拆分或多个 kernel 合并。

最小同步步骤：

1. 更新 `gpu_pass_catalog.yaml` 的 pass 级字段。
2. 更新对应 `shaders/*.compute.ai.yaml` 的 kernel 级字段。
3. 运行 `python tools/renderdoc-cli/validate_gpu_pass_catalog.py --repo .`。
4. 如果校验失败，优先补文档，不要让脚本生成语义字段。

## Python 工具边界

Python 可以做：

- 读取 YAML / JSON。
- 规范化字段命名。
- 输出 `gpu_pass_catalog.json` 作为分析 artifact。
- 按 `pass_id`、alias、marker 或 shader/kernel 做 join。
- 在缺失匹配时写 `missing_catalog_entry`。

Python 不可以做：

- 根据 kernel 名自动生成 `purpose`。
- 根据资源名自动生成 `meaning`。
- 根据 shader 代码自动生成 `performance_hints`。
- 自动给缺失的 `display_name`、`source.file` 或 `source.function` 填 fallback。
- 把本次 RenderDoc timing 反写进长期 catalog。

## 字段口径

- `purpose`：长期设计意图。
- `inputs.meaning` / `outputs.meaning`：资源在系统中的业务含义。
- `dispatch.data_parallel_unit`：一个线程负责的逻辑实体。
- `performance_hints.expected_bottleneck`：设计层可能风险，不是当前帧结论。
- `known_risks`：维护者希望未来 AI 优先检查的风险点。
