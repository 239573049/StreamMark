# 设计决策记录

2026-10-16，基于 [research/](research/) 中的五路调研确定。

| 主题 | 决定 | 被否决的方案及理由 |
|---|---|---|
| 仓库与命名 | 独立仓库 StreamMark，中立命名；TCode 通过 NuGet 引用 | 以 AtomUI 命名（会限制通用性）；Streamdown.Avalonia（易被误认为 Vercel 官方移植） |
| 平台 | 只做桌面：win / linux / osx × x64 / arm64 | 移动端：TextMateSharp 的 Oniguruma 没有 iOS / Android 构建，暂不投入 |
| 解析器 | Markdig 1.4（AOT 0 警告实测） | 自研完整增量解析器（CommonMark 兼容成本数周）；CommonMark.NET（2017 年后停更） |
| 流式策略 | 已稳定前缀冻结 + 只重解析尾部；流结束整篇重解析对齐 | 每个 token 整篇重解析（20KB 回答流完 1.17s / 2.3GB 分配，且无法复用控件） |
| 未闭合语法 | 全部自动补全：移植 vercel remend，只作用于尾部 | 暂缓显示（Codex 式，最稳但样式出现晚）；混合策略 |
| 渲染 | 每块一个 `TextLayout` 自绘 + 自研 `ILogicalScrollable` 虚拟化面板 + 源码偏移选区 | `SelectableTextBlock` 控件树（每次文本或选区变化整段重排，跨块选区代价高）；`VirtualizingStackPanel`（变高项滚动条跳动） |
| 数学 | RaTeX（Rust）经 C ABI 调用，KaTeX 级覆盖 + mhchem | CSharpMath（实测仅约 60–65% 常见命令）；XAML-Math（不支持 Avalonia 12，不 AOT 安全）；Jint + KaTeX（AOT 不可行） |
| Mermaid | Mermaider → SVG（替换 `color-mix`）→ Svg.Skia | merman（需自建 C SDK）；浏览器方案 |
| 代码高亮 | TextMateSharp，按行增量 | AvaloniaEdit 整个编辑器（每个代码块太重）；ColorCode（只能整块着色） |
| 与 AtomUI 的关系 | 核心只依赖 Avalonia，`Md.*` 资源键；`StreamMark.Theme.AtomUI` 单独成包 | 直接依赖 AtomUI（绑定版本，非 AtomUI 项目无法使用） |
| v1 范围 | 完整 Markdown、数学、Mermaid、逐字渐显 + 输出平滑、跨消息选区 | — |

## 实施中追加的决定

- **强调扩展只启用删除线**。上标 `^`、下标 `~`、插入 `++`、高亮 `==` 在大模型输出的普通文本里误触发太多（"C++ 和 C++"、"a == b 且 c == d"、"约 ~5 到 ~10"），不启用。
- **冻结片段与最终整篇解析只使用 remend 的转义修正**（单波浪号、比较运算符），不做末尾补全：已完整的文本不应被"补全"；只有尾部使用完整补全。
- **偏移映射**：remend 插入的转义字符会让解析偏移漂移，`OffsetMap` 把所有区间映射回原始源码，保证块 key 在流式与最终解析之间一致。
- **remend 的占位 URL** 使用 `streammark:incomplete-link` / `streammark:incomplete-image`。
- **块视图统一以 `*BlockView` 命名**（`TableBlockView`、`ListBlockView`…），避免与 Avalonia 12 自带的 `TableView` / `ListView` 冲突。
- **数学包命名为 `StreamMark.RaTeX`**：`StreamMark.Math` 命名空间会遮蔽 `System.Math`。
- **AtomUI 主题用资源别名而不是画刷**：资源字典中画刷上的动态资源从字典所在位置向上查找，找不到 AtomUI 注册在窗口 / 主题作用域内的 token（文字变成透明）。别名由 StreamMark 从 Markdown 控件本身解析。
- **字体默认继承宿主**：不设置 `Md.FontFamily` / `Md.FontSize` 时沿用控件继承的字体，宿主（如 AtomUI 设在窗口上的字体）无需额外配置。
- **删除线自绘**：部分字体（含无头环境的回退字体）缺少删除线度量，`TextDecorations.Strikethrough` 不显示。
- **Svg.Skia 固定 5.1.1，托管依赖（Svg.Model / Svg.Custom / ShimSkiaSharp / Svg.SceneGraph）覆盖到 5.2.3（Svg.Animation 例外：5.2.x 依赖 SkiaSharp 4，保持 5.1.1；CI 的 Linux 任务发现了这一点）**：5.1.1 与 Avalonia 12.1 使用相同的 SkiaSharp 3 原生库；5.2.x 的 Svg.Skia 需要 SkiaSharp 4。Svg.Model 5.1.1 内的 `TypeDescriptor.GetConverter` 会产生 2 个 IL2026，5.2.3 已修复。Mermaid 测试（真实渲染）与 Gallery AOT 冒烟均通过。Avalonia 升级到 SkiaSharp 4 后统一升到 5.2+。
- **RaTeX 本地补丁** `native/patches/0001-enclose-border-uses-current-color.patch`：`\fbox` / `\boxed` 边框原本固定黑色，改为当前颜色；`native/build.sh` 构建前自动应用。应提交上游。
- **TextMateSharp 不支持 Kotlin**（Grammars 包无该语法）；TOML 映射到 INI 语法。
- **原生库的两条分发路径**：NuGet 包把各 RID 的 RaTeX 库放在 `runtimes/<rid>/native/`，使用方按自己的 RID 自动选用；仓库内通过项目引用使用的可执行项目（示例、测试、基准），由根目录 `Directory.Build.targets` 按其目标 RID 从 `artifacts/native/<rid>/` 复制（SDK 不会把 `RuntimeIdentifier` 传给被引用项目，原先的做法在交叉发布 osx-x64 时复制了 arm64 的库）。
- **本机交叉编译** `native/cross-build.sh`：在 macOS 上用 cargo-zigbuild 生成全部 6 个桌面 RID。已在本机实测 osx-arm64 与 osx-x64（Rosetta）的 AOT Gallery 冒烟通过；Linux / Windows 产物已验证格式与导出符号，运行验证依赖 CI。
- **第二阶段（2026-10-16）**：插件体系、技术债、图片子系统、AI 对话功能、交互与无障碍；自定义组件只用白名单 HTML 标签。
- **不需要组件补全处理器**：计划中的 `ComponentCloser` 被取消。未闭合的块级容器由 Markdig 在文末关闭，未闭合的行内成对组件在映射时视为包住其后的文字，写了一半的标签由现有 HTML 处理器去掉；这样流式偏移也无需额外映射。
- **`StreamMark.Highlighting.TextMate` 依赖 `StreamMark.Avalonia`**：为了提供 `TextMatePlugin`。高亮器本身仍只依赖 Core 接口。
- **Markdig 的 `CanInterrupt` 对每个候选解析器都会调用（包括没有段落时）**，自定义块解析器只应在当前块是段落时限制打断；`Block.IsOpen` 在续行判断时不可靠，块级组件自行记录关闭状态（`BlockParser.Close`）。
- **`TextRunCache`（Avalonia 不稳定 API）只在 `TextSegmentView` 中使用**：
  - 缓存以换行符分隔的段落为单位，按段落起点保存整段的整形结果；`InvalidateFrom(i)` 只丢弃起点不早于 `i` 的条目。
  - 所以失效位置必须是差异点所在段落的起点。alpha.8 和 alpha.9 直接用差异点失效，结果旧段落（连同它的段落结束）被复用，每次追加都变成一次硬换行；alpha.10 已修复，并加了回归测试 `SmoothStreamWrapTests`。
  - 修正后，流式段落的排版大约快 2 倍；用 `#pragma warning disable CS0618` 限定范围。
- **长代码分段**：超过 200 行的代码块在会话中按 128 行一段拆成多行；切行与高亮放在块共享状态 `CodeBlockState`（块状态存储）中，分段与回收后重建的视图共用，偏移全局化以保证选区跨段连续。各段的横向滚动彼此独立（已知限制）。
- **中文粗体的两个 Avalonia 问题及规避**：
  1. 为缺字文字找回退字体时，Avalonia 优先复用"上一段"的回退字体，不检查字重 / 字形，导致"中文 **粗体**"中的粗体沿用常规字重。`TextSegmentView` 自行按本段字重解析回退字体，让每段的主字体就能覆盖其字符，不进入那条复用路径。
  2. 苹方没有 700 字重，`Typeface(PingFang SC, Bold)` 会匹配到常规字重；回退时依次尝试 700 / 600 / 800 / 900，取第一个真正够粗的字形。
  有回归测试检查整形后字形的字重。
- **节奏控制器 `SkipCompletedBlocks`**（默认关闭）：积压中已有完整块（空行）时直接放出到最后一个空行，只对未完成的块做逐字平滑。
- **原始 HTML（阶段 3.5）**：
  - 使用自研的容错解析器，代码在 `Core/Html/`，不依赖 AngleSharp：体积小，NativeAOT 安全，并且能控制流式输出中写了一半的 HTML 怎么处理。
  - 解析出的 HTML 树映射到已有的块模型；div、p、center 等映射为 `HtmlBoxBlock`，携带盒子样式和可继承的文字样式。
  - 文字样式通过 `RenderContext.Inherited` 向下传递；字号按与正文字号的比例缩放，标题保持相对大小。
  - **拼接**：`MapSiblings` 用一个开放元素栈跨兄弟块追踪 HTML，处理 GFM 中 HTML 与 Markdown 交错的写法；合并后的块以第一个 HTML 块的起点作为 key。HTML 容器标签同时加入切分器的容器表，未闭合时不冻结。
  - **未知元素**：遵从用户的选择，整段按源码显示，`script`、`style` 同样处理，但绝不执行；单行的自定义元素按 CommonMark 是段落中的行内 HTML，标签按原文显示。
  - **暂不支持**：`colspan`、`rowspan` 和单元格背景色；表格单元格只能放行内内容，单元格中的 `<p>` 和 `<div>` 压平为换行。
  - **作者颜色**：默认模式 `Readable` 用 WCAG 对比度（阈值 2.2）判断是否可读，比较对象是所在的 HTML 背景；没有背景时，按主题文字色推断主题背景。
  - **链接中的图片**：`[![徽章](图片)](链接)` 和 `<a><img></a>` 中，图片的占位字符也带链接区间，点击时打开链接，而不是打开图片查看器。
- **阶段 4（对话功能）**：
  - **链接策略**：判断在渲染层的 `TextSegmentView.LinkDecisionFor` 中进行，结果按内容缓存。被禁止的链接按 `IncompleteLink` 样式显示，也不响应点击；需要确认的链接只设置 `RequiresConfirmation`，由宿主决定如何确认。协议中夹带空白或控制字符（`java\tscript:`）的链接一律拒绝。
  - **引用卡片**：显示在 `OverlayLayer` 上，与图片查看器相同，都是自绘控件，不用 `Popup` 和主题模板；悬停 350ms 后显示，离开 200ms 内移进卡片可以保持显示。覆盖层第一次加入子控件前没有尺寸，定位时改用窗口客户区。
  - **代码块折叠**：超过 40 行的已完成代码块默认折叠。流式输出中出现过的代码块（`IsOpen`）完成后不折叠，避免用户正看着的内容突然收起。展开状态保存在块状态存储中。会话视图里，折叠的长代码只占一行；展开或收起时，`ExpandedChangedEvent` 通知面板重新分段。
  - **行号**：画在 `CodeBlockView` 的左侧栏，`CodeTextView` 本身不变，命中测试也不受影响；行号的行高与代码行相同，保证基线对齐。
- **diff 视图**：
  - **解析**：`Core/Diff/UnifiedDiff` 容错解析，能处理流式输出中写了一半的 diff。区段头声明的行数只用来判断"区段已满"；区段未满时，`--- ` / `+++ ` 开头的行是删除或新增的内容，不当作文件头。
  - **并排配对**：连续的删除行与随后的新增行按顺序一一配对。行内改动只取公共前缀和后缀之间的部分；公共部分不足 30% 时视为整行改写，不做行内高亮。
  - **选区**：整块是一个选区片段，文本为原始 diff；每个单元格记录它在原始行中的起点，选区与复制都按原始偏移计算。
  - **语法着色**：旧侧（上下文和删除行）与新侧（上下文和新增行）分别送进高亮会话，在后台计算，结果按原始行号保存在块状态中。区段之间不连续，跨区段的多行结构可能着色不准，这是已知限制。
  - **暂不虚拟化**：块内的行不做虚拟化（整个 diff 是会话中的一行），超大的 diff 暂不分段。
- **diff 默认样式**：按用户要求，默认采用 GitHub Markdown 的样式（`DiffViewMode.Markdown`），不显示行号、文件标题栏和统计；头部按钮在默认样式与并排视图之间切换。带行号的统一视图由宿主通过 `DiffMode` 选用。并排视图宽度不够时，退回宿主默认的非并排方式。
- **阶段 5（交互与无障碍）**：
  - **查找**：在块模型上按片段查找，结果不跨片段；会话中尚未解析的按需条目会被解析。高亮保存在 `SelectionModel` 中，与选区共用 `Changed` 事件，各片段视图像绘制选区一样绘制高亮。
  - **跳转定位**：先按估算位置滚动，目标行实例化后再按字符的实际位置微调一次，放在视口上部约 30% 处。`MarkdownView` 本身不滚动，交给外层滚动容器的 `BringIntoView`。
  - **拖选与点击**：指针移动超过 3 像素才开始拖拽选择并捕获指针，所以单击能送达链接、图片和宿主内容；按在 `ContentEntry` 控件上不开始选择。
  - **右键菜单**：使用 `MenuFlyout`，样式来自宿主主题；关闭时，在 `ContextRequested` 的隧道阶段把事件标记为已处理。
  - **无障碍**：Avalonia 12 没有公开的 `TextPattern`，读屏只能读到名称；标题通过 `AutomationProperties.HeadingLevel` 提供级别。
  - **键盘选区**：Shift+↑/↓ 用字符框上下方的探测点按坐标命中，探测距离逐步加大，以跨过块之间的间距。
  - **修正**：Markdig 中脚注组的区间从文档开头算起，曾使 `FootnotesBlock` 的 key 为 0，与第一个块重复；现在取其中脚注的区间。
