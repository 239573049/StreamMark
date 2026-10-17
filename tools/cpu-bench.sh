#!/usr/bin/env bash
# 流式渲染 CPU 基准：NativeAOT 发布 bench/StreamMark.CpuHarness，在真实窗口中依次运行场景矩阵，结果写到 artifacts/cpu-bench.txt。
# 用法：tools/cpu-bench.sh [rid]（默认按当前平台）。运行期间会打开窗口，请保持其可见；每个场景约 18 秒。
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:-$(dotnet --info | awk '/RID:/ {print $2; exit}')}"
out_dir="artifacts/cpu-bench/$rid"
dotnet publish bench/StreamMark.CpuHarness -c Release -r "$rid" -o "$out_dir" 2>&1 | grep -E "warning IL|error" || true
exe="$out_dir/StreamMark.CpuHarness"
[[ -x "$exe" ]] || exe="$out_dir/StreamMark.CpuHarness.exe"
samples="bench/StreamMark.CpuHarness/Samples"
result="artifacts/cpu-bench.txt"
echo "# $(date -u +%Y-%m-%dT%H:%M:%SZ) $rid $(git rev-parse --short HEAD)" >> "$result"
run() { SM_OUT="$PWD/$result" SM_LABEL="$1" "${@:2}" "$exe" > /dev/null 2>&1; }
run idle            env SM_IDLE=1 SM_SECONDS=6
run chat            env
run chat-nofade     env SM_FADE_MS=0
run chat-nosmooth   env SM_SMOOTH=0
run chat-region     env SM_REGION=1
run long-paragraph  env SM_SAMPLE="$PWD/$samples/long-paragraph.md"
run long-para-nofade env SM_SAMPLE="$PWD/$samples/long-paragraph.md" SM_FADE_MS=0
run long-code       env SM_SAMPLE="$PWD/$samples/long-code.md"
tail -n 9 "$result"
