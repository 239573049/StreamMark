# StreamMark

High-performance streaming Markdown rendering for [Avalonia](https://avaloniaui.net/), built for AI chat clients.

- **Streaming first**: stable blocks are frozen and parsed once; only the tail is re-parsed. Unfinished syntax (`**bold`, `` `code``, `[link](ht`, `$$`) is healed while streaming (C# port of vercel's remend).
- **Native, no WebView**: self-drawn text blocks, virtualized transcript, cross-message selection.
- **Scientific content**: KaTeX-grade LaTeX via the [RaTeX](https://github.com/erweixin/RaTeX) engine, Mermaid diagrams, TextMate syntax highlighting.
- **NativeAOT-safe**: zero trim/AOT warnings is a CI gate.

> Status: v0.1 preview — all plan milestones (M0–M7) implemented and integrated into TCode. See [docs/architecture.md](docs/architecture.md).

## Packages

| Package | Purpose |
|---|---|
| `StreamMark.Core` | Streaming engine: segmenter, remend, block model, snapshots/diff, pacer (no UI dependency) |
| `StreamMark.Avalonia` | Avalonia controls and default theme |
| `StreamMark.Highlighting.TextMate` | Incremental syntax highlighting |
| `StreamMark.RaTeX` | LaTeX math via RaTeX |
| `StreamMark.Mermaid` | Mermaid diagrams |
| `StreamMark.Theme.AtomUI` | AtomUI design-token theme |

## Avalonia usage

```csharp
// App: default look, then (optionally) map onto AtomUI tokens
Styles.Add(new StreamMarkTheme());
Styles.Add(new AtomUIMarkdownTheme());

// Window: rendering extensions shared by every Markdown view below it
var services = new MarkdownServices
{
    Plugins = [new RaTeXPlugin(), new TextMatePlugin(), new MermaidPlugin()],
};
MarkdownServices.SetServices(window, services);
// documents should use the matching dialect so plugin tags are parsed
var entry = MarkdownEntry.FromText(markdown, services.Profile);

// Long chat transcript (virtualized, cross-message selection, stick-to-bottom)
var transcript = new TranscriptView { MaxContentWidth = 760 };
transcript.Entries.Add(new ContentEntry(userBubble));          // any control / data-templated item
var reply = new MarkdownStream(profile: services.Profile);       // smoothing + fade-in + caret
transcript.Entries.Add(new MarkdownEntry(reply));
reply.Push(token);  /* … */  reply.Complete();

// Single document
var view = new MarkdownView { Markdown = "# Hello" };
```

## Components (whitelisted HTML tags)

Built in: `<details>/<summary>`, `<think>`, `<kbd>`, `<sub>`, `<sup>`, `<mark>`, `<u>`, `<cite href>`, `<Citation id="1"/>`, `<img src width height>`.
Plugins add their own: `builder.AddBlockComponent("callout", _ => new CalloutView())`, `builder.AddInlineComponent("badge", renderer)`.

## Raw HTML

Raw HTML in Markdown (README style) is converted into the block model, so it supports selection, copying, theming and streaming. The parser is a tolerant one written for this library.

- **Block elements**: `div p center section article header footer main aside figure h1–h6 table thead tbody tfoot tr th td ul ol li dl dt dd blockquote pre hr details summary`.
- **Inline elements**: `a img picture br strong b em i u ins s del strike code kbd samp tt var sub sup mark small big span font abbr cite dfn q time`.
- **Attributes**: `align`, `width`, `height`, `bgcolor`, and `<font color size>`.
- **`style` subset**: `text-align`, `color`, `background(-color)`, `font-size`, `font-weight`, `font-style`, `margin*`, `padding*`, `border`, `border-radius`, `width`, `max-width`, `display:none`. Values are clamped so content cannot break the layout.
- **HTML around Markdown**: HTML separated from Markdown by blank lines is merged into one box, for example `<div align="center">`, a blank line, Markdown, a blank line, `</div>`.
- **Unsupported elements**: HTML containing an unknown element (including `script`, `style`, `iframe`) is shown as source and never executed. Comments are hidden.
- **Author colours**: `MarkdownServices.HtmlColors` controls them. The default is `Readable`: author colours are used, but the theme colour replaces any colour without enough contrast against its background. `Exact` and `Ignore` are also available.

## Chat features

- **Link policy**: `MarkdownServices.Links` (a `LinkPolicy`).
  - By default only `https`, `http` and `mailto` links are allowed. Relative links and `#anchors` are passed to the host.
  - Blocked links (`javascript:`, `file:`, `data:`, unknown schemes) render as plain text.
  - `LinkClickedEventArgs.RequiresConfirmation` is set when `RequireConfirmation` is on, or when the link text looks like a different URL (for example `[https://github.com](https://evil.example)`).
  - `TextSegmentView.LinkHoveredEvent` reports the real URL, for showing in a status bar.
- **Citations**: set `MarkdownServices.Sources` (an `ISourceProvider`, for example `DictionarySourceProvider`).
  - Hovering `<Citation id="1"/>` shows a source card with the title, site and snippet; clicking it opens the URL.
  - An unhandled click on a citation or a `<cite href>` raises `LinkClicked`.
- **Code blocks**: `MarkdownServices.CodeBlocks` (a `CodeBlockOptions`).
  - `ShowLineNumbers`: numbering starts at `#L10` when the fence info gives a start line.
  - Collapsing: `CollapseAfterLines` (default 40) and `CollapsedLines` (default 20). Blocks that were streamed are never collapsed.
  - `ExpandText` and `CollapseText` set the labels.
  - Download: `ShowDownload` toggles the button. `Save` overrides the default save dialog. The suggested file name comes from the fence path or the language.
  - `diff` and `patch` blocks get coloured backgrounds for added and removed lines.

## Core usage

```csharp
var document = new StreamingDocument();
document.SnapshotReady += _ => RequestFrame();   // raised on a background thread

// network callback
document.Append(token);
// end of stream
document.Complete();

// on each UI frame
var next = document.Latest;
foreach (var op in SnapshotDiff.Compute(applied, next)) { /* insert / update / remove block views */ }
applied = next;
```

## Building

```bash
git submodule update --init
./native/build.sh            # builds the RaTeX native library for this machine (needs Rust)
./native/cross-build.sh      # or: all six desktop RIDs from one macOS machine (rustup targets + zig + cargo-zigbuild)
dotnet build StreamMark.slnx
dotnet test StreamMark.slnx
dotnet publish samples/StreamMark.Gallery -c Release -r osx-arm64 && ./samples/StreamMark.Gallery/bin/Release/net10.0/osx-arm64/publish/StreamMark.Gallery --smoke
```

## Packaging for local consumers

```bash
tools/pack-local.sh 0.1.0-alpha.5     # writes artifacts/packages/*.nupkg (bump the version on every repack)
```

## License

MIT. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
