# 构建 RaTeX C ABI 动态库（Windows），输出到 artifacts/native/<rid>/。
# 用法：native/build.ps1 -Rid win-x64 [-Target x86_64-pc-windows-msvc]
param([string]$Rid = "win-x64", [string]$Target = "")
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/.."
$out = Join-Path $root "artifacts/native/$Rid"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$env:CARGO_TARGET_DIR = Join-Path $root "artifacts/cargo"
# 本地补丁（上游修复前）：对子模块工作区应用，已应用则跳过
Get-ChildItem (Join-Path $root "native/patches") -Filter *.patch -ErrorAction SilentlyContinue | ForEach-Object {
    git -C (Join-Path $root "native/ratex") apply --check $_.FullName 2>$null
    if ($LASTEXITCODE -eq 0) { git -C (Join-Path $root "native/ratex") apply $_.FullName; Write-Host "applied $($_.Name)" }
}
$args = @("build", "--manifest-path", (Join-Path $root "native/ratex/Cargo.toml"), "-p", "ratex-ffi", "--release")
if ($Target) { $args += @("--target", $Target) }
& cargo @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dir = if ($Target) { Join-Path $env:CARGO_TARGET_DIR "$Target/release" } else { Join-Path $env:CARGO_TARGET_DIR "release" }
Copy-Item (Join-Path $dir "ratex_ffi.dll") $out
Get-ChildItem $out
