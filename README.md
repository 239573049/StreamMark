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
MarkdownServices.SetServices(window, new MarkdownServices
{
    Math = RaTeXMathRenderer.Shared,
    Highlighter = new TextMateSyntaxHighlighter(),
    Diagrams = [new MermaidDiagramRenderer()],
});

// Long chat transcript (virtualized, cross-message selection, stick-to-bottom)
var transcript = new TranscriptView { MaxContentWidth = 760 };
transcript.Entries.Add(new ContentEntry(userBubble));          // any control / data-templated item
var reply = new MarkdownStream();                                // smoothing + fade-in + caret
transcript.Entries.Add(new MarkdownEntry(reply));
reply.Push(token);  /* … */  reply.Complete();

// Single document
var view = new MarkdownView { Markdown = "# Hello" };
```

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
