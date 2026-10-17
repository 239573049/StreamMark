---
name: fallback-font-position-history
description: Windows CI 上 IncrementalLayoutTests 失败的原因：回退字体曾依赖取 run 的起点与 Avalonia 的匹配历史
type: project
updated: 2026-10-17
source: session
evidence: [src/StreamMark.Avalonia/Rendering/TextSegmentView.cs:MatchFallback, tests/StreamMark.Avalonia.Tests/Rendering/IncrementalLayoutTests.cs:StreamedMixedScripts_MatchFullLayout]
---
alpha.16 引入逐行排版后，`StreamedLineBreaks_MatchFullLayout_AtEveryStep(styled: False)` 只在 Windows CI 上失败（2026-10-16 起）：整段排版时缺字片段"。禁则…）。"从"。"开始取 run，逐行重排从行首"首"开始取，回退字体按 run 的第一个字匹配，Windows 上两者落到不同字体，字宽不同、断行不同。macOS 上两者都是苹方，测不出来。另外 Avalonia 的 `FontManager.TryMatchCharacter` 会优先复用之前匹配过的回退字体（实测：匹配过"한"之后"。"从 PingFang SC 变成 Apple SD Gothic Neo），结果依赖进程内历史。alpha.19 两处都修了：按缺字片段起点匹配，并用静态字典固定每个字第一次的结果。

**Why:** 只在一个平台出现的断行差异，根因在字体回退而不在 `SegmentLayout`；韩文与汉字连写的文本能在 macOS 上稳定复现，不必等 Windows CI。
**How to apply:** 改动 `InlineTextSource` 的 run 切分或回退字体时，保证同一段文字无论从哪个位置开始取、何时取，都得到同一字体；跑 `IncrementalLayoutTests`。
