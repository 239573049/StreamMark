# AGENTS.md

## 这份文件管什么

StreamMark：面向 AI 对话场景的流式 Markdown 渲染库，Avalonia 12 / .NET 10，NativeAOT 安全、不使用 WebView，支持六个桌面 RID（win/linux/osx × x64/arm64）。单一解决方案 `StreamMark.slnx`。

本文件是硬约束与验证方式。代码「是什么」在 `.agents/codemap/`，理由与历史在 `.agents/memory/`，叙述性设计在 `docs/`（数据流水线见 [docs/architecture.md](docs/architecture.md)，冻结与 remend 规则见 [docs/streaming-rules.md](docs/streaming-rules.md)，决策与被否决的方案见 [docs/decisions.md](docs/decisions.md)）。它们与代码不一致时以代码为准。

关键入口：`StreamingDocument`（`src/StreamMark.Core/Streaming/StreamingDocument.cs`）、`MarkdownView` / `MarkdownStream`（`src/StreamMark.Avalonia/MarkdownView.cs`、`MarkdownStream.cs`）、`TranscriptView`（`src/StreamMark.Avalonia/Transcript/TranscriptView.cs`）、`MarkdownServices`（`src/StreamMark.Avalonia/Extensions/MarkdownServices.cs`）。

## 命令

以下命令都取自仓库现有配置（README、CI、脚本）。本次会话只读过它们，**没有执行过**，除标注外均未实际验证。

| 目的 | 命令 | 说明 |
|---|---|---|
| 构建 | `dotnet build StreamMark.slnx` | SDK 版本见 `global.json`（10.0.300，`latestFeature`） |
| 测试 | `dotnet test StreamMark.slnx` | 单项目：`dotnet test tests/StreamMark.Core.Tests/StreamMark.Core.Tests.csproj`；单用例 `--filter "FullyQualifiedName~<名字>"` 是通用 xunit 写法，仓库里没有这种调用（unverified） |
| 原生库（公式必需） | `git submodule update --init` 后 `./native/build.sh [rid]` | 需要 Rust；全 RID 交叉编译 `./native/cross-build.sh` 需要 rustup 目标 + zig + cargo-zigbuild |
| AOT 冒烟（CI 门禁的等价命令） | `dotnet publish samples/StreamMark.Gallery -c Release -r <rid>`，要求 publish 日志里 0 个 `warning IL####`；再把 `artifacts/native/<rid>/` 的文件复制到发布输出旁，运行 `StreamMark.Gallery --smoke` | 缺原生库时会在 `ratex.*` 检查失败；`--smoke` 的退出码即结论（0 = 全部通过） |
| 打包本地 NuGet | `tools/pack-local.sh <version>` | 产出 `artifacts/packages/*.nupkg`；重新打包必须升版本号 |
| 基准 | `dotnet run -c Release --project bench/StreamMark.Benchmarks -- --filter '*'` | ShortRun，只看量级；结果记录在 [docs/benchmarks.md](docs/benchmarks.md) |
| 测试截图 | 图片写到 `STREAMMARK_SNAPSHOT_DIR`，未设置时用测试输出目录下的 `snapshots/` | 只用于人工查看，没有基线比对 |

## 架构与边界

- 依赖方向单向：`Core` → Markdig；`StreamMark.Avalonia` → Core；扩展包与主题包 → Avalonia（+ Core）。**Core 不得引用 Avalonia；Avalonia 不得引用任何扩展包或主题包**——扩展一律经 `Plugins/`、`Extensions/` 的接口注册。完整图与归属表见 [.agents/codemap/architecture.md](.agents/codemap/architecture.md)。
- 解析只在工作线程：`Append` / `Complete` / `Reset` 可在任意线程调用，界面线程每帧最多取一次 `Latest` 并应用一次差异（`SnapshotReady` 在后台线程触发，必须回投界面线程）。
- 唯一真相是只追加的源码缓冲；块与选区的身份都是「原始源码偏移」。块模型、快照、会话行模型都是派生数据，不得当作真相写回。
- 视觉样式的真相是 Avalonia 资源里的 `Md.*` 键；`MarkdownStyle` 是解析后的不可变快照，只有 `MarkdownView` 与 `TranscriptPanel` 持有并下发。
- 失败被吞掉是既有行为：解析异常保留旧快照（表现为界面停止更新），高亮异常被丢弃。排查「界面不动」时先看这两处，不要先怀疑视图层。

## 改动规则

- 块的身份是 `MarkdownBlock.Key`（源码起始偏移）。给块模型加字段时必须同时进 `ComputeFingerprint`（`src/StreamMark.Core/Model/Blocks.cs`），否则内容变化在完成后不会产生 Update。
- `DiffKind` 新增成员要同步三处 `switch`：`SnapshotDiff`、`DocumentPresenter.Apply`、`TranscriptPanel`（`ApplyAll`）。漏改会让新操作被静默忽略。
- 冻结片段与 `Complete()` 的整篇解析只用位置无关的转义修正（remend 的 singleTilde / comparisonOperators）；末尾补全只作用于尾部。在冻结片段上启用补全会让流式结果与最终结果不一致。
- 新增 remend handler：同时加进 `Remend` 的启用列表与（插件场景）`MarkdownProfileBuilder.AddRemendHandler`，否则开关存在但永不执行。
- 新增白名单 HTML 组件要动四处：`ComponentSpec` 注册、解析器插入顺序（`HtmlComponentBlockParser` 必须在 `HtmlBlockParser` 之前，行内组件解析器在 `AutolinkInlineParser` 之前）、映射器、块或行内视图。未知元素整组按源码显示且绝不执行（含 `script` / `style` / `iframe`）。
- 行内 HTML 的文字样式有两条独立实现（`MarkdigMapper` 里的标签栈与 `HtmlMapper.HtmlInlineBuilder`）；加样式必须两处都改。
- 新增块视图：`BlockViewFactory` 的 `ViewTypeFor` / `CreateBuiltIn` / `CanReuseBuiltIn` 三个分支都要加，漏第三个会让视图在每次快照更新时被重建。片段编号必须与 `BlockText.Segments` 的收集顺序一致，否则选区与复制错位。
- 新增 `Md.*` 键：`MarkdownResourceKeys` 常量 + `MarkdownStyle` 的 required 属性与解析 + `Themes/StreamMarkTheme.axaml` 的 Light/Dark 字典 + 消费点（+ 可选的 AtomUI 映射）。键名写错或别名解析失败不会报错，只会静默回落。
- 主题里写 `MarkdownResourceAlias`，不要写画刷或 `DynamicResource`：宿主 token 在窗口 / 主题作用域，从字典位置向上查找找不到会让文字变透明。宿主主题要注册在 `StreamMarkTheme` 之后。
- 文档必须用 `services.Profile` 创建（`MarkdownEntry.FromText(text, services.Profile)`、`new MarkdownStream(profile: …)`），否则插件注册的标签不会被解析；`MarkdownView` 上先 `SetServices` 再设 `Markdown`，反过来文档会留着旧方言。
- 插件 `Configure` 只登记委托与工厂（NativeAOT，禁反射）；`PluginRegistry` 首次访问即固化，之后修改 `Plugins` 无效。同名组件后登记者覆盖先登记者是设计内行为。
- 选区坐标只存值（`Entry` / `BlockKey` / `Segment` / `Offset`），不得持有视图引用；复制必须经 `ISelectionHost.GetSnapshot(entry)` 取模型，否则被虚拟化回收的块复制不到。
- `TextRunCache` 是 Avalonia 不稳定 API，只在 `TextSegmentView` 内用 `#pragma warning disable CS0618` 限定；`InvalidateFrom` 的失效位置必须是差异点所在段落的起点，否则每次追加都会变成一次硬换行。
- NativeAOT：正则一律 `[GeneratedRegex]`、JSON 一律源生成、主题用 `x:Class` 编译加载、不用反射绑定。裁剪 / AOT 警告按 `Directory.Build.props` 的列表视为错误，CI 另外要求发布日志里 0 个 IL 警告。
- 包版本只写在 `Directory.Packages.props`，偏离用 `VersionOverride`（见 `src/StreamMark.Mermaid` 的 Svg.Skia）。新项目要加进 `StreamMark.slnx`；放在 `src/` 下的项目自动获得可打包、AOT 兼容与 XML 文档属性。
- 原生库有两条分发路径：NuGet 包的 `runtimes/<rid>/native/` 与仓库内的 `artifacts/native/<rid>/`（由 `Directory.Build.targets` 按目标 RID 复制给非可打包项目）。改任一条都要顾及另一条。
- 改动本文档提到的命令、边界或联动步骤时，在同一次改动里更新它；改动模块的入口、契约或扩展步骤时，同一次改动里更新对应的 `.agents/codemap/` 页面。

## 并发改动

这个仓库经常有并发编辑。改任何文件前先读它的当前内容，保留不属于你的改动，不要整文件覆写。

## 冲突处理

先服从更高优先级的指令与任务本身的范围；在范围内保持既有不变量、使用最小 diff。两者无法同时满足时先说明冲突再动手。任何规则都不构成顺手重构无关代码、整文件重排或未经要求提交 / 推送的许可。

## 仓库地图

详细地图在 `.agents/codemap/`（生成时仓库 HEAD 是 `9b0e08b`，工作树同时有未提交改动；地图可能落后于代码——不一致时以代码为准并顺手修页面）。动手前先读对应页面：

- 改动解析、切分、快照、节奏 → `.agents/codemap/modules/core-streaming.md`、`modules/remend-and-offsetmap.md`
- 改动 Markdig 方言、白名单组件、HTML/CSS 映射 → `.agents/codemap/modules/core-parsing-and-model.md`
- 改动块视图、样式解析、流式渐显 → `.agents/codemap/modules/avalonia-text-rendering.md`
- 改动会话行模型、虚拟化、滚动 → `.agents/codemap/modules/avalonia-transcript.md`
- 改动选区或复制格式 → `.agents/codemap/modules/avalonia-selection.md`
- 改动图片加载、缓存、策略 → `.agents/codemap/modules/avalonia-images.md`
- 加插件、组件视图、围栏视图 → `.agents/codemap/flows/plugin-registration.md`
- 改动主题、`Md.*` 键 → `.agents/codemap/flows/theme-resolution.md`
- 改动构建、包版本、CI、原生库 → `.agents/codemap/modules/build-and-packaging.md`、`modules/native-ratex.md`
- 不确定新代码放哪里 → `.agents/codemap/architecture.md` 的归属表
- 遇到不认识的业务名词 → `.agents/codemap/domain.md`

## 仓库记忆

`.agents/memory/` 记录代码里看不到的上下文：被纠正的做法、决定及其理由、陷阱、外部资源。它是背景资料，规则在本文件。

**开始任务前：** 读 `.agents/memory/MEMORY.md`，打开与任务相关的条目。条目描述的是它写下的那个时点：依赖它引用的路径、符号或命令之前先核实。与代码或本文件冲突时按代码与本文件执行，并修正或删除该条目。

**事情发生时写，不要留到任务结束：**
- 用户纠正了你的做法，或确认了一个非显然的做法 → `feedback`
- 做出了影响后续改动的决定、说明了理由，或否决了某个方案 → `project`
- 发现了代码里难以看出、本文件又没写的陷阱 → `project`
- 用户提到外部资源（看板、工单、文档、监控） → `reference`

**不要记录：** 代码、git 历史、本文件或 `.agents/codemap/` 已经写明的东西；一次性的任务状态；密钥或个人数据；未经验证的猜测。

**怎么写：** 先更新已有条目再新建；一个事实一个文件，格式见 `.agents/memory/README.md`；用绝对日期；给出证据（`path:Symbol`、提交或链接）；同步 `MEMORY.md` 的那一行；发现条目是错的就删掉。个人习惯写进 `.next-cowork/memory/`（不提交）。

**升格：** 某个 `feedback` 条目被再次确认且对所有人成立时，建议把它写进本文件——不要自己改这里的规则。记忆的改动会出现在 diff 里，除非被要求不要单独提交它们。
