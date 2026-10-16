# 基准测试

`dotnet run -c Release --project bench/StreamMark.Benchmarks -- --filter '*' --job short`
（macOS arm64，.NET SDK 10.0.301，2026-10-16，BenchmarkDotNet ShortRun，误差较大，仅看量级）

## 流式解析：20KB 聊天式文档，按 4 字符一个 token 流完（约 5100 次更新）

| 方案 | 总耗时 | 总分配 |
|---|---:|---:|
| 每个 token 整篇重解析（remend + Markdig） | 1,270 ms | 1,854 MB |
| StreamMark（冻结前缀 + 只解析尾部，含最终整篇重解析） | 36 ms | 21 MB |

StreamMark 快约 35 倍，分配少约 87 倍。平均每次更新约 7µs。
调研中的"只解析尾部 5ms / 11MB"只计 Markdig 解析；这里还包含 remend、块模型映射、切分器和流结束时的整篇重解析。

实际使用时 `StreamPacer` 把提交频率限制在每帧最多一次（约 60 次/秒），远低于这里的每 token 一次。

## remend：约 58k 字符的单个尾部块

| 方法 | 平均耗时 | 分配 |
|---|---:|---:|
| `Remend.Complete` | 194 µs | 115 KB |

亚毫秒级，与 vercel 线性扫描器的结论一致（其 changelog：915ms → 0.4ms）。

## 视图层（无头 Skia，Debug 测试构建，`VirtualizationScaleTests`）

| 场景 | 结果 |
|---|---|
| 单条消息 10 万个顶层块：首次应用快照 + 布局 | 50 ms，实例化 ≤ 120 行 |
| 在 10 万块中任意跳转滚动（每次布局） | 约 7 ms |
| 10 万块之后继续流式追加（每帧应用 + 布局） | 约 3 ms |

## 扩展（NativeAOT，子代理实测）

| 扩展 | 结果 |
|---|---|
| TextMate：300 行 C# 逐行流式 | 约 88 µs / 行（JIT 约 136 µs） |
| TextMate：C# 语法冷启动 | 9–12 ms（新二进制首次启动约 386 ms，启动时后台预热） |
| Mermaid：5–6 节点流程图 | 约 1.2 ms（进程内首次渲染额外 50–90 ms） |
| RaTeX：排版 | 亚毫秒级，结果按 (latex, 显示模式) 缓存 |
