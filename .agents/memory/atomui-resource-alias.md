---
name: atomui-resource-alias
description: AtomUI 主题必须用 MarkdownResourceAlias 而不是画刷，否则文字会变透明
type: project
updated: 2026-10-16
source: init
evidence: [src/StreamMark.Theme.AtomUI/AtomUIMarkdownTheme.cs, src/StreamMark.Avalonia/MarkdownResourceAlias.cs, docs/decisions.md]
---
`StreamMark.Theme.AtomUI` 往 `Md.*` 键里写的是 `MarkdownResourceAlias(SharedTokenKind.*)`，由 StreamMark 在 Markdown 控件自身的作用域解析，而不是直接写画刷或 `DynamicResource`。

**Why:** 资源字典里画刷上的动态资源从字典所在位置向上查找，找不到 AtomUI 注册在窗口 / 主题作用域内的 token，结果是文字变成透明。
**How to apply:** 为其它设计系统写主题时照这个模式做；把别名换成画刷或动态资源会静默变透明，`MarkdownStyle` 解析失败时不会报错。
