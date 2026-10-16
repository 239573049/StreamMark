#!/usr/bin/env bash
# 在一台 macOS 机器上交叉编译全部桌面 RID 的 RaTeX 动态库（用于本地验证与打包；CI 在各平台原生 runner 上构建）。
# 依赖：rustup（含目标）、zig、cargo-zigbuild。
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
export CARGO_TARGET_DIR="$root/artifacts/cargo"
for patch in "$root"/native/patches/*.patch; do
  [[ -f "$patch" ]] || continue
  git -C "$root/native/ratex" apply --check "$patch" 2>/dev/null && git -C "$root/native/ratex" apply "$patch" && echo "applied $(basename "$patch")"
done
build() {
  local rid="$1" target="$2" tool="$3" file="$4"
  echo "== $rid ($target)"
  cargo "$tool" --manifest-path "$root/native/ratex/Cargo.toml" -p ratex-ffi --release --target "$target"
  mkdir -p "$root/artifacts/native/$rid"
  cp "$CARGO_TARGET_DIR/$target/release/$file" "$root/artifacts/native/$rid/"
}
build osx-arm64   aarch64-apple-darwin       build     libratex_ffi.dylib
build osx-x64     x86_64-apple-darwin        build     libratex_ffi.dylib
build linux-x64   x86_64-unknown-linux-gnu   zigbuild  libratex_ffi.so
build linux-arm64 aarch64-unknown-linux-gnu  zigbuild  libratex_ffi.so
build win-x64     x86_64-pc-windows-gnu      zigbuild  ratex_ffi.dll
build win-arm64   aarch64-pc-windows-gnullvm zigbuild  ratex_ffi.dll
find "$root/artifacts/native" -type f -name "*ratex_ffi*" -exec file {} \;
