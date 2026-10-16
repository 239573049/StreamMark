# Third-party notices

StreamMark includes or is derived from the following third-party works.

## remend (vercel/streamdown)

`src/StreamMark.Core/Remend/` is a C# port of `packages/remend` from
https://github.com/vercel/streamdown, and the remend test fixtures under
`tests/StreamMark.Core.Tests/Fixtures/remend/` are derived from its test suite.

Copyright 2023 Vercel, Inc. Licensed under the Apache License, Version 2.0
(http://www.apache.org/licenses/LICENSE-2.0). Modifications: ported to C#,
sentinel URL scheme changed from `streamdown:` to `streammark:`.

## LiveMarkdown.Avalonia

The backslash math parsers in `src/StreamMark.Core/Parsing/BackslashMathExtension.cs`
follow the approach of LiveMarkdown.Avalonia (https://github.com/DearVa/LiveMarkdown.Avalonia),
licensed under the Apache License, Version 2.0.

## Markdig

https://github.com/xoofx/markdig — Copyright (c) Alexandre Mutel. BSD 2-Clause License.

## RaTeX

`native/ratex` (git submodule) — https://github.com/erweixin/RaTeX. MIT License.

## KaTeX fonts

The KaTeX fonts embedded in `StreamMark.RaTeX` (shipped by RaTeX under `native/ratex/fonts`)
are licensed under the SIL Open Font License 1.1.

## TextMateSharp, Mermaider, Svg.Skia

Referenced as NuGet packages under their respective MIT licenses.
