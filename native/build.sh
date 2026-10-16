#!/usr/bin/env bash
# 构建 RaTeX C ABI 动态库，输出到 artifacts/native/<rid>/。
# 用法：native/build.sh [rid]   （默认取本机 RID，例如 osx-arm64）
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
rid="${1:-}"
if [[ -z "$rid" ]]; then
  os="$(uname -s)"; arch="$(uname -m)"
  case "$os" in Darwin) os=osx;; Linux) os=linux;; *) echo "unsupported OS $os" >&2; exit 1;; esac
  case "$arch" in arm64|aarch64) arch=arm64;; x86_64) arch=x64;; *) echo "unsupported arch $arch" >&2; exit 1;; esac
  rid="$os-$arch"
fi
out="$root/artifacts/native/$rid"
mkdir -p "$out"
export CARGO_TARGET_DIR="$root/artifacts/cargo"

# 本地补丁（上游修复前）：对子模块工作区应用，已应用则跳过
for patch in "$root"/native/patches/*.patch; do
  [[ -f "$patch" ]] || continue
  if git -C "$root/native/ratex" apply --check "$patch" 2>/dev/null; then
    git -C "$root/native/ratex" apply "$patch"
    echo "applied $(basename "$patch")"
  fi
done
cargo build --manifest-path "$root/native/ratex/Cargo.toml" -p ratex-ffi --release ${CARGO_BUILD_TARGET:+--target "$CARGO_BUILD_TARGET"}
dir="$CARGO_TARGET_DIR/${CARGO_BUILD_TARGET:+$CARGO_BUILD_TARGET/}release"
for f in libratex_ffi.dylib libratex_ffi.so ratex_ffi.dll; do
  [[ -f "$dir/$f" ]] && cp "$dir/$f" "$out/"
done
ls -la "$out"
