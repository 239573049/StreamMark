#!/usr/bin/env bash
# 打包全部 StreamMark 库到 artifacts/packages（本地 NuGet 源，供 TCode 等宿主引用）。
# 用法：tools/pack-local.sh <version>     例如 tools/pack-local.sh 0.1.0-alpha.2
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
version="${1:?version required}"
for project in Core Avalonia RaTeX Highlighting.TextMate Mermaid Theme.AtomUI; do
  dotnet pack "$root/src/StreamMark.$project" -c Release -o "$root/artifacts/packages" -p:Version="$version" --nologo -v quiet
done
ls "$root/artifacts/packages" | grep "$version"
