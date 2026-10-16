# StreamMark 架构

StreamMark 是面向 AI 对话场景的高性能流式 Markdown 渲染库：Avalonia 12 / .NET 10，全程 NativeAOT 安全，不依赖 WebView。

## 数据流水线

```
网络 token ──▶ ① StreamPacer（节奏控制） ──▶ ② StreamingDocument.Append（只追加的源码缓冲）
                                                   │  后台单线程，合并请求
                                                   ▼
                                     ③ StreamSegmenter：在原始源码上逐行扫描，找安全冻结边界
                                                   │
             新冻结片段：remend（仅转义）+ Markdig，解析一次，永不再动 ◀──┤
                                                   ▼
                                     ④ 尾部：remend（完整补全）+ Markdig；
                                        尾部只是未闭合代码块时走快速路径，不调用解析器
                                                   ▼
                                     ⑤ MarkdigMapper → 不可变块模型（key = 源码起始偏移）
                                                   ▼
                                     ⑥ DocumentSnapshot（冻结前缀共享 + 尾部）
                                                   │  界面线程每帧最多取一次 Latest
                                                   ▼
                                     ⑦ SnapshotDiff：跳过共享前缀，按 key 归并 → Insert / Update / Remove
                                                   ▼
                                     ⑧ 视图层：块视图（自绘文本 / 代码 / 表格 / 公式 / 图表 / 容器）
                                        放在 MarkdownView（单文档）或 TranscriptPanel（虚拟化会话）中
```

流结束（`Complete()`）时整篇重解析一次并开启新的一代，修正跨片段的引用定义、脚注和列表松紧；差异比对保证内容没变的块不会重建。

## 分层与包

| 包 | 依赖 | 职责 |
|---|---|---|
| `StreamMark.Core` | Markdig | 源码缓冲、remend 移植、切分器、块模型、快照与差异、节奏控制器。无 UI 依赖，全部可单元测试 |
| `StreamMark.Avalonia` | Avalonia、Core | 自绘块视图、`TranscriptPanel` 虚拟化面板、`SelectionModel`、默认主题（`Md.*` 资源键） |
| `StreamMark.Highlighting.TextMate` | TextMateSharp | `ISyntaxHighlighter` 实现：按行缓存语法状态，只着色新到达的行 |
| `StreamMark.RaTeX` | RaTeX 原生库 | `[LibraryImport]` 调用 RaTeX C ABI，源生成 JSON 解析显示列表，KaTeX 字体嵌入 |
| `StreamMark.Mermaid` | Mermaider、Svg.Skia | 代码块闭合后后台渲染 Mermaid |
| `StreamMark.Theme.AtomUI` | Avalonia | 把 `Md.*` 资源映射到 AtomUI 设计 token |

## 关键不变量

1. **源码只追加**（`Reset()` 开启新的一代）。块的 key 是它在原始源码中的起始偏移，因此跨重解析稳定。
2. **冻结边界单调前进**，冻结的块对象在同一代内永不改变；快照共享冻结前缀，差异比对直接跳过。
3. **冻结片段独立解析的结果与整篇解析一致**（引用定义除外，见 streaming-rules.md）。测试 `StreamingEquivalenceTests` 在每一步都验证这一点。
4. **偏移永远指原始源码**：remend 会插入转义字符，`OffsetMap` 把修补后文本中的位置映射回原始位置。
5. **界面线程不做解析**：解析在后台单线程合并执行，界面层每帧最多应用一次差异。

## 线程模型

- `StreamingDocument.Append/Complete/Reset` 可在任意线程调用。
- 解析在线程池上的单个逻辑工作者中执行；进行中有新文本只打脏标记，完成后再处理最新文本（中间版本跳过）。
- `SnapshotReady` 在工作线程触发；`Latest` 任意线程可读。
- `StreamPacer` 非线程安全，应在界面线程的动画帧回调中驱动。

## NativeAOT

- 所有库项目 `IsAotCompatible=true`，裁剪 / AOT 警告视为错误。
- 正则一律 `[GeneratedRegex]`；JSON 一律源生成；主题以 `x:Class` 编译加载；不使用反射绑定。
- CI 的 `aot-smoke` 任务发布 Gallery 并运行 `--smoke`，要求 0 个 IL 警告。

## 视图层（StreamMark.Avalonia）

| 组件 | 说明 |
|---|---|
| `TextSegmentView` | 一个文本片段 = 一个 `TextLayout`。自定义 `ITextSource` 提供行内样式；公式、图片、任务复选框是 `DrawableTextRun`（基线对齐）。选区只画矩形，不重新排版；删除线自绘；文档末尾片段绘制渐显蒙版与流式光标 |
| 块视图 | `TextBlockView`（段落 / 标题 / HTML）、`CodeBlockView`（逐行排版缓存 + 后台增量高亮 + 复制 / 换行）、`TableBlockView`（自定义列宽布局，不用 `Grid`）、`MathBlockView`（保留上一次成功排版）、`DiagramBlockView`（闭合后后台渲染、缓存、图表 / 源码切换）、列表 / 引用 / 提示块 / 容器 / 脚注 / 定义列表 |
| `DocumentPresenter` | 把快照差异应用到顶层块视图：同类型复用，类型变化才替换 |
| `MarkdownView` | 单文档、非虚拟化、高度随内容；`Markdown` / `Document` / `Stream` 三种输入 |
| `TranscriptPanel` / `TranscriptView` | 会话：所有消息摊平成行（Markdown 顶层块各一行，宿主内容各一行），只实例化视口 ±600px 内的行；按块类型估算未测量行高；滚动锚定；贴底跟随；`MaxContentWidth` 居中列 |
| `SelectionModel` / `SelectionController` | 选区坐标 `(消息, 顶层块 key, 片段, 偏移)`，与视图无关；单击 / 拖拽（自动滚动）/ 双击选词 / 三击选段 / Shift 扩展；⌘C 纯文本、⌘⇧C Markdown 源码、⌘A 全选；被虚拟化移出的块也能复制 |
| `MarkdownStream` | 节奏控制器 + 流式文档 + 渐显时间记录，任意线程 `Push` / `Complete` |
| `MarkdownServices` | 可继承附加属性：`IMathRenderer`、`ISyntaxHighlighter`、`IDiagramRenderer`、`IImageLoader` |

### 扩展实现

| 包 | 实现 |
|---|---|
| `StreamMark.RaTeX` | `RaTeXMathRenderer`：C ABI 排版（线程安全、LRU 缓存含错误），显示列表在界面线程转为 `GlyphRun` + 几何；KaTeX 字体从嵌入资源加载为私有 `GlyphTypeface`；默认颜色用哨兵色区分，保留 `\color`；CJK / emoji 回退系统字体 |
| `StreamMark.Highlighting.TextMate` | `TextMateSyntaxHighlighter`：每个明暗主题一个 Registry + 锁（TextMateSharp 2.0.4 非线程安全）；会话按行缓存语法状态，追加只着色新行，中间修改在结束状态一致处停止；冷启动超时行重试 |
| `StreamMark.Mermaid` | `MermaidDiagramRenderer`：Mermaider → SVG → 解析 `var()` / `color-mix()` 为字面颜色 → Svg.Skia `SKPicture` → 自定义绘制操作在 Avalonia 的 Skia 画布上矢量回放 |
| `StreamMark.Theme.AtomUI` | `AtomUIMarkdownTheme`：`Md.*` → `MarkdownResourceAlias(SharedTokenKind.*)`，从控件本身解析 AtomUI token |

### 主题解析

`MarkdownStyle.Resolve(control)` 一次性读取 `Md.*` 资源：值为 `MarkdownResourceAlias` 时从控件本身查找目标键（宿主 token 可能只在窗口 / 主题作用域可见），`Color` 自动转画刷；未设置 `Md.FontFamily` / `Md.FontSize` 时沿用控件继承的 `TextElement` 字体，行高取字号 1.6 倍。主题变体或资源变化时重新解析并下发到所有视图。
