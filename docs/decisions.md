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
- **Svg.Skia 固定 5.1.1，托管依赖（Svg.Model / Svg.Custom / ShimSkiaSharp / Svg.SceneGraph / Svg.Animation）覆盖到 5.2.3**：5.1.1 与 Avalonia 12.1 使用相同的 SkiaSharp 3 原生库；5.2.x 的 Svg.Skia 需要 SkiaSharp 4。Svg.Model 5.1.1 内的 `TypeDescriptor.GetConverter` 会产生 2 个 IL2026，5.2.3 已修复。Mermaid 测试（真实渲染）与 Gallery AOT 冒烟均通过。Avalonia 升级到 SkiaSharp 4 后统一升到 5.2+。
- **RaTeX 本地补丁** `native/patches/0001-enclose-border-uses-current-color.patch`：`\fbox` / `\boxed` 边框原本固定黑色，改为当前颜色；`native/build.sh` 构建前自动应用。应提交上游。
- **TextMateSharp 不支持 Kotlin**（Grammars 包无该语法）；TOML 映射到 INI 语法。
- **原生库的两条分发路径**：NuGet 包把各 RID 的 RaTeX 库放在 `runtimes/<rid>/native/`，使用方按自己的 RID 自动选用；仓库内通过项目引用使用的可执行项目（示例、测试、基准），由根目录 `Directory.Build.targets` 按其目标 RID 从 `artifacts/native/<rid>/` 复制（SDK 不会把 `RuntimeIdentifier` 传给被引用项目，原先的做法在交叉发布 osx-x64 时复制了 arm64 的库）。
- **本机交叉编译** `native/cross-build.sh`：在 macOS 上用 cargo-zigbuild 生成全部 6 个桌面 RID。已在本机实测 osx-arm64 与 osx-x64（Rosetta）的 AOT Gallery 冒烟通过；Linux / Windows 产物已验证格式与导出符号，运行验证依赖 CI。
