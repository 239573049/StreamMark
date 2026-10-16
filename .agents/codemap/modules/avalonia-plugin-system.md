---
module: avalonia-plugin-system
sources: ["src/StreamMark.Avalonia/Plugins/**", "src/StreamMark.Avalonia/Extensions/**"]
commit: 44a8f5a
updated: 2026-10-16
---
# Avalonia 插件体系与服务解析

`IMarkdownPlugin` 在 `Configure(MarkdownPluginBuilder)` 里一次性登记语法（白名单组件、Markdig 扩展、映射）、渲染（块组件视图 / 行内组件 / 围栏视图 / 公式 / 高亮 / 图表）与交互（代码块头部操作）；宿主把插件塞进 `MarkdownServices`，附加属性 `MarkdownServices.ServicesProperty` 把这份服务集合下发给整棵可视子树，首次访问时汇总成不可变的 `PluginRegistry`，注册表同时给出文档解析要用的 `Profile`。

**不负责：** 白名单标签的实际解析与 `ComponentSpec` 白名单表 / `MarkdigMapper` 的节点映射（`core-parsing-and-model`，`src/StreamMark.Core/Parsing/MarkdownProfile.cs:MarkdownProfileBuilder.AddComponent`、`src/StreamMark.Core/Parsing/HtmlComponentExtension.cs`）；块视图基类、`BlockViewFactory` 的分派分支与 `RenderContext` 的语义（`avalonia-text-rendering`，`src/StreamMark.Avalonia/Rendering/BlockViewFactory.cs:Create`）；RaTeX / TextMate / Mermaid 三个具体实现（`avalonia-extensions`，`src/StreamMark.RaTeX/RaTeXPlugin.cs`）；`Md.*` 主题资源与样式快照（`theme-atomui`、`src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs`）；图片取数 / 解码 / 缓存（`avalonia-images`，`src/StreamMark.Avalonia/Images/ImagePipeline.cs`，这里只持有 `MarkdownServices.Images` 引用）。

## Entry points
| Symbol | File | Role |
|---|---|---|
| `MarkdownServices.ServicesProperty` | `src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:85` | 可继承附加属性（默认值 `MarkdownServices.Default`）：`SetServices` 在窗口或任意祖先上设置，其下所有视图共用 |
| `MarkdownServices.GetServices` | `src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:88` | 视图侧唯一读取入口（`MarkdownView.cs:105`、`TranscriptPanel.cs:232`） |
| `MarkdownServices.Registry` | `src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:78` | `_registry ??= new PluginRegistry(this)`：首次访问才汇总、之后缓存 |
| `MarkdownServices.Profile` | `src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:81` | 转发 `Registry.Profile`，宿主建文档时传入 |
| `IMarkdownPlugin` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:17` | 插件只实现 `Name` + `Configure(builder)` |
| `MarkdownPluginBuilder` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:25` | 插件注册 API 的唯一入口（`AddBlockComponent` / `AddInlineComponent` / `AddFenceView` / `AddBlockAction` / `UseMath` / `UseHighlighter` / `AddDiagramRenderer`） |
| `PluginRegistry` | `src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:11` | 汇总后的不可变注册表：`Profile` + 渲染层查询方法 |
| `BuiltInComponentsPlugin` | `src/StreamMark.Avalonia/Plugins/BuiltInComponentsPlugin.cs:18` | 总是第一个被 `Configure` 的内置组件插件（单例 `Instance`，:20） |

## Key types and functions
| Symbol | File | Role |
|---|---|---|
| `MarkdownPluginBuilder.Profile` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:33` | 暴露 Core 的 `MarkdownProfileBuilder`，插件在此登记白名单组件 / Markdig 扩展 / 映射 / remend 处理器 |
| `MarkdownPluginBuilder.AddBlockComponent` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:66` | 登记 `<name>` 的块级视图工厂，`registerTag: true`（默认）同时 `Profile.AddComponent(new ComponentSpec(name, ComponentKind.Block))` |
| `MarkdownPluginBuilder.AddInlineComponent` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:77` | 登记行内组件渲染器；`ComponentKind.SelfClosing`（默认）走 `CreateRun`，`InlinePair` 走 `GetStyle` + 点击 |
| `MarkdownPluginBuilder.AddFenceView` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:88` | 按围栏语言替换代码块视图；`whileStreaming: true` 才在代码块未闭合时也替换 |
| `MarkdownPluginBuilder.AddBlockAction` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:95` | 追加块头部操作描述符（`AppliesTo` 过滤 + `Execute` 回调） |
| `IInlineComponentRenderer` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:105` | 四个全带默认实现的方法：`CreateRun` / `GetStyle` / `ToPlainText` / `IsClickable`，插件只需覆写关心的部分 |
| `BlockActionDescriptor` / `BlockActionContext` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:127` / `:135` | 操作定义（Id / Label / Icon / `AppliesTo` / `Execute`）与执行上下文（当前 `MarkdownBlock`、视图 `Control`、解析后的 `CodeFenceInfo`） |
| `CodeFenceInfo.Parse` | `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:140` | 解析 ```` ```cs src/App.cs#L10-20 ```` 这类信息串为 语言 / 路径 / 起止行 |
| `PluginRegistry` 构造 | `src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:17` | 新建 builder → 依次 `Configure`（内置在前，宿主插件按列表顺序）→ 合并服务 → `Profile = builder.Profile.Build()` |
| `PluginRegistry.FindFenceView` | `src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:62` | `internal`；语言匹配且（已闭合 或 注册时声明 `whileStreaming`）才返回注册 |
| `BlockStateStore` | `src/StreamMark.Avalonia/Plugins/BlockStateStore.cs:8` | 每个文档一份的 `(块 key, 状态名) → object` 字典，视图回收重建后状态仍在 |
| `MarkdownServices` | `src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:13` | `Plugins` + 兼容写法 `Math` / `Highlighter` / `Diagrams`，以及 `Images`、`HtmlColors`、`ImageBaseUriProperty`、`ResolveImageUrl` |
| `IMathRenderer` / `MathLayout` | `src/StreamMark.Avalonia/Extensions/IMathRenderer.cs:9` / `:21` | 公式排版（`Layout` 必须线程安全）与与字号无关的排版结果（em 单位、界面线程 `Draw`） |
| `IDiagramRenderer` / `DiagramResult` | `src/StreamMark.Avalonia/Extensions/IDiagramRenderer.cs:12` / `:22` | `CanRender` 按语言认领 + `RenderAsync` 后台渲染；结果可 `Draw` 到任意矩形、可 `Dispose` |

## Dependencies
- Uses: `core-parsing-and-model` via `MarkdownProfileBuilder.AddComponent` / `MarkdownProfileBuilder.Build` 与 `ComponentSpec` / `ComponentKind` — `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:71`、`src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:32`
- Uses: `core-parsing-and-model` via `ComponentBlock`（视图工厂与块操作的输入模型）与 `CodeBlock` — `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:38`、`src/StreamMark.Avalonia/Plugins/BuiltInComponentsPlugin.cs:66`
- Uses: `core-highlighting` via `ISyntaxHighlighter` / `HighlightThemeKind`（只做类型搬运，实际使用在 `CodeBlockState.For`） — `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:36`
- Uses: `avalonia-text-rendering` via `BlockView` / `StackBlockView`（视图工厂签名与内置组件视图）、`DrawableTextRun`、`MarkdownStyle`、`TextSegmentView` 事件 — `src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:38`、`src/StreamMark.Avalonia/Plugins/BuiltInComponentsPlugin.cs:34`
- Used by: `avalonia-text-rendering` via `context.Plugins` 的 `TryGetBlockComponent` / `FindFenceView` / `BlockActions`、`Services.Registry.Math`、`State` — `src/StreamMark.Avalonia/Rendering/BlockViewFactory.cs:13`、`src/StreamMark.Avalonia/Rendering/CodeBlockView.cs:129`
- Used by: `avalonia-selection` via `ISelectionHost.Plugins` + `FindInlineComponent(...).ToPlainText` — `src/StreamMark.Avalonia/MarkdownView.cs:239`、`src/StreamMark.Avalonia/Selection/SelectionController.cs:229`
- Used by: `avalonia-transcript` via `MarkdownServices.GetServices(this)` → 每条的 `RenderContext`，以及每条自己的 `BlockStateStore` — `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:232`、`:39`
- Used by: `avalonia-extensions` via 三个实现 `IMarkdownPlugin` 的包（`RaTeXPlugin` / `TextMatePlugin` / `MermaidPlugin`） — `src/StreamMark.RaTeX/RaTeXPlugin.cs:6`、`src/StreamMark.Highlighting.TextMate/TextMatePlugin.cs:6`、`src/StreamMark.Mermaid/MermaidPlugin.cs:6`
- Used by: `tests` / `samples` via `new MarkdownServices { Plugins = [...] }` + `MarkdownServices.SetServices` + `services.Profile` — `tests/StreamMark.Avalonia.Tests/Plugins/PluginSystemTests.cs:130`、`samples/StreamMark.Gallery/Program.cs:83`

## State it owns
- `MarkdownServices._registry`（`src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:17`）：`Registry => _registry ??= new PluginRegistry(this)`（:78）。注册表**只构建一次**，之后插件列表、`Math`、`Highlighter`、`Diagrams` 的任何后续改动都不生效（`MarkdownServices` 的属性都是 `init`，正确做法是换一个新实例）。首次访问的时机决定一切：`MarkdownView.CreateContext`（`src/StreamMark.Avalonia/MarkdownView.cs:170`）与 `MarkdownView.OnSourceChanged` 里的 `GetServices(this).Profile`（:105）都会顺手把注册表建出来。
- `PluginRegistry`（`src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:13`）：`_blockComponents` / `_inlineComponents` / `_fenceViews` 是构造期从 builder 接过来的字典（builder 随后被丢弃），`Diagrams` / `BlockActions` 是 `ImmutableArray`，`Profile` 是 `builder.Profile.Build()` 的产物（:32）。因此注册**只发生在构造阶段**，运行期没有任何增删入口；视图层拿到的是同一批工厂委托与同一个 `MarkdownProfile` 实例（`ReferenceEquals(fence, oldFence)` 就是靠这个身份判断复用，`src/StreamMark.Avalonia/Rendering/BlockViewFactory.cs:51`）。
- `MarkdownServices.Default`（`src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:15`）是附加属性的默认值，即进程级单例：没设 `Services` 的所有视图共用它（以及它的首次访问即固化的注册表）；`ImagePipeline` 同理有 `s_defaultImages`（:31、:40）。
- `BlockStateStore` 的作用域是**每个文档视图一份**，不是每块一份：`MarkdownView._state`（`src/StreamMark.Avalonia/MarkdownView.cs:36`，输入属性变化时 `_state.Clear()`，:115）、`TranscriptPanel.EntryState.State`（`src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:39`）。`RenderContext` 收下它，缺省时自建一个空实例（`src/StreamMark.Avalonia/Rendering/RenderContext.cs:17`），并在 `With` / `WithHtml` 中透传（:53、:61）。
- 键是 `(顶层块 key, 状态名)`（`src/StreamMark.Avalonia/Plugins/BlockStateStore.cs:10`）：key 是块在原始源码中的起始偏移，源码只追加因此跨重解析稳定；状态名是字符串，已存在的有 `code.wrap`（`src/StreamMark.Avalonia/Rendering/CodeBlockView.cs:26`）、`code.state`（`src/StreamMark.Avalonia/Rendering/CodeBlockState.cs:52`）、`diagram.code`（`src/StreamMark.Avalonia/Rendering/DiagramBlockView.cs:43`）、`"collapsible.open:" + 组件名`（`src/StreamMark.Avalonia/Plugins/BuiltInComponentsPlugin.cs:62`）。约定只在界面线程访问（`BlockStateStore.cs:6`）。

## Contracts it exposes
- **`IMarkdownPlugin`**：`Name`（仅标识，注册表内不去重、不按名查找）+ `Configure(MarkdownPluginBuilder)`；`Configure` 在构造 `PluginRegistry` 时被同步调用一次，只允许**登记委托与工厂**，不要在里面做 IO 或反射（NativeAOT，`docs/architecture.md:57`）。
- **`MarkdownPluginBuilder` 各方法的语义**：`AddBlockComponent(name, factory, registerTag)` 把 `name` 映射为块视图工厂，默认同时把标签加进白名单（同名重复登记直接覆盖，`src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:68`）；工厂只负责 **new**，内容一律通过 `BlockView.Update` 下发，同名组件的新模型复用同一视图；`AddInlineComponent(name, renderer, kind, registerTag)` 默认按自闭合组件登记；`AddFenceView(language, factory, whileStreaming)` 按 `CodeBlock.Language` 精确匹配（大小写不敏感）；`AddBlockAction(descriptor)` 是追加列表，无去重。
- **`IInlineComponentRenderer`** 的四个方法都带默认实现，插件按需覆写：`CreateRun`（自闭合组件 → `DrawableTextRun`，`null` 表示不显示）、`GetStyle`（成对组件内部文字的 `InlineComponentStyle`，`null` 表示继承）、`ToPlainText`（复制到剪贴板时替代文本）、`IsClickable`（决定悬停手型与 `TextSegmentView.ComponentClickedEvent`，`src/StreamMark.Avalonia/Rendering/TextSegmentView.cs:566`、`:576`）。
- **`IMathRenderer` / `ISyntaxHighlighter` / `IDiagramRenderer`**：分别经 `UseMath` / `UseHighlighter` / `AddDiagramRenderer` 登记。`Layout` 必须线程安全（渲染层可能后台预排版）、失败返回 `null` 并给出 error（`src/StreamMark.Avalonia/Extensions/IMathRenderer.cs:7`）；`ISyntaxHighlighter` 来自 Core，按语言 `CreateSession` + `GetThemeColors`（`src/StreamMark.Core/Highlighting/ISyntaxHighlighter.cs:30`）；`IDiagramRenderer.CanRender` 按语言认领、`RenderAsync` 可在后台线程跑、失败回退源码（`src/StreamMark.Avalonia/Extensions/IDiagramRenderer.cs:10`）。
- **`MarkdownServices.ServicesProperty`**：`inherits: true`，设置在窗口 / 会话视图 / 任意祖先上；`MarkdownView` 与 `TranscriptPanel` 都在静态构造里挂了 `Changed` 处理器，只做“作废当前 `RenderContext` 并请求下一帧”（`src/StreamMark.Avalonia/MarkdownView.cs:44`、`src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:95`）。
- **`Profile` 的用法约定**：宿主**必须**用 `services.Profile` 创建文档，四条路径都要传：`new StreamingDocument(services.Profile)`（`src/StreamMark.Core/Streaming/StreamingDocument.cs:37`，`null` 即回落到 `MarkdownProfile.Default`）、`new MarkdownStream(profile: services.Profile)`（`src/StreamMark.Avalonia/MarkdownStream.cs:20`）、`MarkdownEntry.FromText(text, profile)`（`src/StreamMark.Avalonia/Transcript/TranscriptEntry.cs:25`）、`MarkdownView.Markdown` 时由控件内部自建（`src/StreamMark.Avalonia/MarkdownView.cs:105`）。
- **兼容写法**：`Math` / `Highlighter` / `Diagrams` 三个属性等价于“注册一个只提供该能力的插件”，`PluginRegistry` 构造里 `Math = builder.Math ?? services.Math`（`src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:25`）；`Diagrams` 则是 `[.. services.Diagrams, .. builder.Diagrams]`（:27），两者的优先级方向相反。

## How to extend
- **To add 一个插件**：① 实现 `IMarkdownPlugin`，在 `Configure` 里一次性登记（参照 `RaTeXPlugin`，`src/StreamMark.RaTeX/RaTeXPlugin.cs:6`）；② 挂进 `new MarkdownServices { Plugins = [new MyPlugin(), ...] }` 并经 `MarkdownServices.SetServices(viewOrAncestor, services)` 下发（`src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:85`）；③ 用 `services.Profile` 创建文档——`MarkdownEntry.FromText(text, services.Profile)` / `new StreamingDocument(services.Profile)` / `new MarkdownStream(profile: services.Profile)`（`samples/StreamMark.Gallery/Program.cs:85`、`:89`）。漏掉 ③ 的症状：插件注册的组件标签根本不会被解析（`Profile` 变了但文档用的是 `MarkdownProfile.Default`），标签按普通 HTML / 文本落到 `HtmlBlock` / 段落里，`TryGetBlockComponent` 永远不会被调用（`src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:35` 的注释即为此）。
- **To add 行内 / 块级组件视图**：① 实现 `IInlineComponentRenderer`（只覆写需要的方法）或提供一个 `Func<ComponentBlock, BlockView>` 工厂；② `builder.AddInlineComponent(name, renderer, kind)` / `builder.AddBlockComponent(name, factory)`（`src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:66`、`:77`）；自定义视图类继承 `StackBlockView` 或 `BlockView` 并在 `OnUpdate` 里**按阅读顺序**消耗片段号（`src/StreamMark.Avalonia/Rendering/BlockView.cs:43`）；③ 若视图要在回收后保留状态，把状态写进 `context.State`（键用 `(TopKey, 状态名)`，参照 `CollapsibleBlockView.OnUpdate` 的 `context.State.Get(topKey, StateName, defaultOpen)`，`src/StreamMark.Avalonia/Plugins/BuiltInComponentsPlugin.cs:81`）。漏掉 ③ 的症状：视图被虚拟化回收后展开 / 换行 / 图表视图选择全部回到默认值。
- **To add 代码围栏视图与块操作**：① `builder.AddFenceView("diff", code => new DiffView(), whileStreaming: false)`，视图需自己消费片段号（`segment++`）并实现 `MeasureOverride`（测试替身见 `tests/StreamMark.Avalonia.Tests/Plugins/PluginSystemTests.cs:78`）；② 头部按钮用 `builder.AddBlockAction(new BlockActionDescriptor(id, label, MarkdownIcons.X, block => block is CodeBlock { Language: "cs" }, ctx => Task.CompletedTask))`（`src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:127`）；③ 需要围栏信息串（路径 / 行号）时在 `Execute` 里用 `Plugins.CodeFenceInfo.Parse`（`src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:140`）。漏掉 ② 的症状：`AppliesTo` 恒为 false 时按钮不出现（`src/StreamMark.Avalonia/Rendering/CodeBlockView.cs:131`），而操作列表在**每次上下文变化时整体重建**（`UpdateActions`，:122），漏掉 `AppliesTo` 的过滤条件会让操作出现在所有代码块 / 表格上。

## Invariants and traps
- **同名组件后登记者覆盖先登记者** — builder 用普通字典赋值（`BlockComponents[name] = factory`，`src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:68`），内置插件排在最前（`src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:20`），所以宿主插件覆盖内置是**设计内行为**；`Profile.AddComponent` 同样是先 `RemoveAll` 同名再追加（`src/StreamMark.Core/Parsing/MarkdownProfile.cs:79`），两处必须同时覆盖，否则出现“解析成组件但视图还是内置的”或反之。
- **内置组件总是启用且无法关闭** — `IEnumerable<IMarkdownPlugin> plugins = [BuiltInComponentsPlugin.Instance, .. services.Plugins]` 是硬编码拼接（`src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:20`），`details` / `think` / `citation` / `cite` 四个视图必然存在（`src/StreamMark.Avalonia/Plugins/BuiltInComponentsPlugin.cs:26`，均以 `registerTag: false` 登记，白名单标签来自 `ComponentSpec.BuiltIn`，`src/StreamMark.Core/Parsing/ComponentSpec.cs:20`）；未注册视图的 `ComponentBlock` 回落到 `ComponentContainerView` 而不是报错（`src/StreamMark.Avalonia/Rendering/BlockViewFactory.cs:13`）。
- **插件注册只用委托与工厂，不用反射** — `MarkdownPluginBuilder` 的字段全是 `Func<...>` / `IInlineComponentRenderer` / 记录类型（`src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:35-41`），Core 侧 `MapBlock<T>` / `MapInline<T>` 也只做 `is TNode` 判定后调委托（`src/StreamMark.Core/Parsing/MarkdownProfile.cs:87`）；库项目一律 `IsAotCompatible=true` 且裁剪警告视为错误（`docs/architecture.md:57`），所以插件里不要引入反射式注册 / 动态类型扫描。
- **注册表不可变，改动只在注册阶段** — 运行期没有任何 API 能往注册表里加东西；`Registry` 一旦被首次访问就固定（`src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:78`），此后修改 `Plugins` 列表内容不会生效（属性本身也是 `init`）。想在运行期换插件只能换一个新的 `MarkdownServices` 实例并重新 `SetServices`。
- **`MarkdownServices` 变化不会重建自建文档** — 附加属性的 `Changed` 处理器只调 `InvalidateContext()`（作废 `RenderContext` 并请求下一帧，`src/StreamMark.Avalonia/MarkdownView.cs:44`、`:172`），而 `_ownDocument ??= new StreamingDocument(GetServices(this).Profile)`（:105）只在 `Markdown` / `Document` / `Stream` 变化且 `_ownDocument` 为 null 时重建。所以先设 `Markdown`、后 `SetServices` 的话，文档仍用旧 `Profile`，插件标签不被解析；正确顺序是先 `SetServices` 再设 `Markdown`（测试即如此，`tests/StreamMark.Avalonia.Tests/Plugins/PluginSystemTests.cs:132`）。
- **相容属性与插件的优先级方向相反** — 公式与高亮是 `builder.Math ?? services.Math`（插件赢，`src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:25`），图表是 `[.. services.Diagrams, .. builder.Diagrams]` 且 `FindDiagramRenderer` 返回**第一个** `CanRender` 匹配（:27、:48），因此旧写法 `services.Diagrams` 反而排在插件前面、先被命中。`Math` / `Highlighter` 是单值（后登记覆盖先登记），`Diagrams` 是列表（先登记者优先）。
- **围栏视图默认只在代码块闭合后替换** — `FindFenceView` 要求 `!code.IsOpen || registration.WhileStreaming`（`src/StreamMark.Avalonia/Plugins/PluginRegistry.cs:63`），流式中途是普通 `CodeBlockView`，闭合后 `DocumentPresenter.Restyle`（`src/StreamMark.Avalonia/Rendering/DocumentPresenter.cs:83`）才会换成插件视图；`CanReuse` 用 `ReferenceEquals(fence, oldFence)` 判定，因此同语言注册被替换（新注册表）时旧视图会被换掉，而围栏视图与 `CodeBlockView` 之间来回切换也会导致视图重建。
- **`AddBlockComponent(registerTag: true)` 会与 Core 的内置标签表合并**，`ComponentTable` 用 `OrdinalIgnoreCase` 查找、`BlockNames` 取小写（`src/StreamMark.Core/Parsing/ComponentSpec.cs:50`、`:58`），而 `MarkdigMapper` 把块级组件名统一 `ToLowerInvariant()` 后放进模型（`src/StreamMark.Core/Parsing/MarkdigMapper.cs:188`）——注册 `"Callout"` 与注册 `"callout"` 等价，视图工厂字典同样是 `OrdinalIgnoreCase`。
- **`BlockStateStore` 只在界面线程访问、且随文档生命周期清空** — `MarkdownView.OnSourceChanged` 里 `_state.Clear()`（`src/StreamMark.Avalonia/MarkdownView.cs:115`），因此换文档 / 重设 `Markdown` 会把折叠、换行、图表选择全部重置；`BlockStateStore.Set` 要求 `T : notnull`（`src/StreamMark.Avalonia/Plugins/BlockStateStore.cs:15`），存可空类型（如 `CodeBlockState?`）会静默不写入。

## Tests
- 本模块主测试：`tests/StreamMark.Avalonia.Tests/Plugins/PluginSystemTests.cs:19`。四个用例覆盖：`BuiltInAndPluginComponents_Render`（:124，插件块组件 / 行内组件 / 头操作只在 `cs` 块上出现 / `diff` 围栏视图 / 三个折叠块的展开状态 / 组件 `ToPlainText` 参与复制）、`CompatibilityProperties_AndPlugins_RenderTheSameModel`（:166，`Math = RaTeXMathRenderer.Shared` 与 `Plugins = [new RaTeXPlugin()]` 得到同一个 `Registry.Math` 与同长度 `Profile.Components`）、`BlockState_SurvivesVirtualization`（:179，写 `code.wrap` 后滚动回收再回来仍为 `true`）、`FenceInfo_ParsesPathAndLines`（:220）。测试内自带 `TestPlugin`（:22）可直接当插件模板。
- Core 侧的白名单标签注册测试在 `tests/StreamMark.Core.Tests/Parsing/ComponentTests.cs:118`（`MarkdownProfileBuilder.AddComponent` + `DocumentComposer`），属于 `core-parsing-and-model`，不经 `MarkdownServices`。
- `PluginSystemTests` 是唯一走 `MarkdownServices.SetServices` 的插件路径测试；`tests/StreamMark.Avalonia.Tests/Math/`、`Mermaid/`、`Highlighting/` 直接构造渲染器（`RaTeXMathRenderer` / `MermaidDiagramRenderer` / `TextMateSyntaxHighlighter`），不经 `MarkdownServices`（已 grep 确认这三个目录内无 `MarkdownServices` / `Profile` 引用）。`tests/StreamMark.Avalonia.Tests/Images/ImageRenderingTests.cs:85`、`Rendering/HtmlRenderingTests.cs:109` 用 `new MarkdownServices { Images = pipeline }` 只测图片管线。
- 命名模式：子目录 `Plugins/`、`Math/`、`Mermaid/`、`Highlighting/`、`Rendering/`、`Transcript/`、`Images/`，xUnit v3 `[Fact]` / `[Theory]`，类名与被测组件同名，测试体挂在 `HeadlessApp.Run` 里（`tests/StreamMark.Avalonia.Tests/HeadlessApp.cs:24`）。
- 运行：全量 `dotnet test StreamMark.slnx`（`README.md:91`）。只跑本文件可用 `dotnet test tests/StreamMark.Avalonia.Tests/StreamMark.Avalonia.Tests.csproj --filter "FullyQualifiedName~PluginSystemTests"`（unverified：仓库脚本与 CI 里没有这种调用，本次未执行）。

## Related flows
- [plugin-registration](../flows/plugin-registration.md)
- [streaming-token-to-view](../flows/streaming-token-to-view.md)
- [theme-resolution](../flows/theme-resolution.md)
olution.md)
