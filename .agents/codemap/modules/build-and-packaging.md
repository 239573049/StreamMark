---
module: build-and-packaging
sources: ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "StreamMark.slnx", ".github/workflows/ci.yml", "tools/pack-local.sh", "bench/**"]
commit: e01b99a
updated: 2026-10-16
---
# 构建与打包

仓库根目录的三个 MSBuild 文件（`Directory.Build.props` / `Directory.Build.targets` / `Directory.Packages.props`）给所有项目加同一套约束（`net10.0`、`src/` 下自动可打包 + AOT 兼容分析、IL 警告即错误），`global.json` 钉住 SDK，`StreamMark.slnx` 列出全部项目；CI 先按 RID 构建 RaTeX 原生库，再分三个 RID 做构建 + 测试与 NativeAOT 冒烟；`tools/pack-local.sh` 把 6 个库打成本地 NuGet 包供宿主（TCode）引用。
**不负责：** RaTeX 原生库本身怎么编（`native-ratex`，`native/build.sh`、`native/build.ps1`、`native/cross-build.sh`）；Gallery 的 `--smoke` 检查了什么（`gallery-and-benchmarks`，`samples/StreamMark.Gallery/SmokeTest.cs`）；测试工程与快照断言（`tests`）；各包的源码实现（`core-streaming`、`avalonia-text-rendering` 等）；`StreamMark.RaTeX.csproj` 内部把原生库打进 `runtimes/<rid>/native/` 的打包项（`native-ratex`）。

## Entry points
| Symbol | File | Role |
|---|---|---|
| `Directory.Build.props`（根，MSBuild 自动导入） | `Directory.Build.props:1` | 所有项目的统一属性源：TFM / 语言版本 / 库元数据 / IL 警告列表 / `src/` 特有条件组 |
| `Directory.Build.targets`（根） | `Directory.Build.targets:1` | 唯一的目标：给**非可打包**项目按 RID 复制 RaTeX 原生库到输出与发布目录 |
| `Directory.Packages.props`（根） | `Directory.Packages.props:1` | 中央包版本管理（CPM）：`ManagePackageVersionsCentrally` + 全部 `<PackageVersion>` |
| `global.json` | `global.json:2` | SDK 选择：`10.0.300` + `rollForward: latestFeature` |
| `StreamMark.slnx` | `StreamMark.slnx:1` | 解决方案（XML 格式）：`/src/` 6 个库、`/tests/` 2 个、`/bench/` 1 个、`/samples/` 1 个 |
| `jobs.native-ratex` | `.github/workflows/ci.yml:20` | 6 个桌面 RID × 原生 runner 上构建 `ratex_ffi` 动态库并上传 artifact |
| `jobs.build-test` | `.github/workflows/ci.yml:53` | 3 个 RID：下载对应原生 artifact → `dotnet build` / `dotnet test` → 上传截图快照 |
| `jobs.aot-smoke` | `.github/workflows/ci.yml:87` | 3 个 RID：`dotnet publish` Gallery（NativeAOT）+ IL 警告 grep 门禁 + 运行 `--smoke` |
| `jobs.bench` | `.github/workflows/ci.yml:123` | 仅 `workflow_dispatch` 且 `inputs.bench` 为真时跑 BenchmarkDotNet 并上传结果 |
| `tools/pack-local.sh` | `tools/pack-local.sh:1` | 本地打包脚本：6 个库 → `artifacts/packages`，版本由参数传入 |

## Key types and functions
| Symbol | File | Role |
|---|---|---|
| `TargetFramework` / `LangVersion` / `Nullable` / `ImplicitUsings` | `Directory.Build.props:3`、`:4`、`:5`、`:6` | 全仓库统一 `net10.0` + C# `latest` + 可空引用类型 + 隐式 using；项目文件里不再重复声明 TFM |
| `Version` / `PackageLicenseExpression` / `Company` / `Product` / `Authors` | `Directory.Build.props:7`、`:10`、`:11`、`:8`、`:9` | 包元数据默认值（版本 `0.1.0`），`pack-local.sh` 用 `-p:Version` 覆盖 |
| `WarningsAsErrors`（IL 列表） | `Directory.Build.props:14` | 把 22 个裁剪 / AOT 诊断 ID 提升为错误：`IL2026`、`IL2046`、`IL2050`、`IL2055`、`IL2057`、`IL2058`、`IL2059`、`IL2060`、`IL2067`、`IL2070`、`IL2072`、`IL2075`、`IL2077`、`IL2080`、`IL2087`、`IL2090`、`IL2091`、`IL2104`、`IL3000`、`IL3050`、`IL3051`、`IL3053`，追加到既有 `$(WarningsAsErrors)` |
| `src/` 条件 PropertyGroup | `Directory.Build.props:18` | 以 `MSBuildProjectDirectory` 是否含 `/src/` 为条件（`:18`）设置 `IsPackable=true`（`:19`）、`IsAotCompatible=true`（`:20`）、`GenerateDocumentationFile=true`（`:21`）、`NoWarn=$(NoWarn);CS1591`（`:22`） |
| `_StreamMarkNativeRid` | `Directory.Build.targets:8`、`:9` | 取 `$(RuntimeIdentifier)`；为空时回退到 `$(NETCoreSdkPortableRuntimeIdentifier)`（即本机 RID） |
| `_StreamMarkNativeDir` | `Directory.Build.targets:10` | `$(MSBuildThisFileDirectory)artifacts\native\<rid>\` —— 原生库的来源目录 |
| `None Include=...*ratex_ffi.*`（复制原生库的 Item） | `Directory.Build.targets:13` | 条件 `'$(IsPackable)' != 'true' and Exists('$(_StreamMarkNativeDir)')`（`:12`）；排除 `*.a;*.lib;*.d`，`Link=%(Filename)%(Extension)` 平铺到输出根，`CopyToOutputDirectory` / `CopyToPublishDirectory = PreserveNewest`、`Visible=false`（`:14`） |
| `ManagePackageVersionsCentrally` | `Directory.Packages.props:3` | 打开 CPM：所有 `<PackageReference>` 不带版本 |
| `AvaloniaVersion` | `Directory.Packages.props:5` | `12.1.3`，被 `Avalonia` / `Avalonia.Desktop` / `Avalonia.Skia` / `Avalonia.Themes.Fluent` / `Avalonia.Headless` 五行引用（`:9-13`）；注释说明与 TCode / AtomUI 6.2.5 所用版本一致 |
| `PackageVersion` 清单 | `Directory.Packages.props:9`–`:26` | Avalonia 五件套 + `AtomUI.Core 6.2.5`（`:15`）+ `Markdig 1.4.0`（`:17`）+ `TextMateSharp` / `TextMateSharp.Grammars 2.0.4`（`:18`、`:19`）+ `Mermaider 0.15.1`（`:20`）+ `Svg.Skia 5.2.3`（`:21`）+ `Microsoft.NET.Test.Sdk 18.11.0` / `xunit.v3 3.2.2` / `xunit.runner.visualstudio 3.1.5` / `BenchmarkDotNet 0.15.8`（`:23-26`） |
| `VersionOverride`（Mermaid 的版本覆盖） | `src/StreamMark.Mermaid/StreamMark.Mermaid.csproj:14`–`:21` | CPM 的唯一例外：`Svg.Skia` 固定 `5.1.1`（`:14`）、`Svg.Model` / `Svg.Custom` / `ShimSkiaSharp` / `Svg.SceneGraph` 覆盖到 `5.2.3`（`:17-20`）、`Svg.Animation` 保持 `5.1.1`（`:21`，5.2.x 依赖 SkiaSharp 4）；理由与实测见 `docs/decisions.md:30` |
| `sdk.version` / `sdk.rollForward` | `global.json:3`、`:4` | 要求 `10.0.300`，同一 feature band 内允许上浮（本机 10.0.301 满足） |
| 解决方案分组 | `StreamMark.slnx:2`、`:10`、`:14`、`:17` | `<Folder>` + `<Project Path>` 列出 6 库 + 2 测试 + 1 基准 + 1 示例；`dotnet build/test StreamMark.slnx` 是 CI 与 README 的统一入口 |
| `native-ratex` 矩阵与构建 | `.github/workflows/ci.yml:24`、`:39`、`:42`、`:46` | `win-x64/win-arm64/linux-x64/linux-arm64/osx-x64/osx-arm64` 六个 RID 各在原生 runner 上跑 `native/build.sh <rid>`（Unix，`:42`）或 `native/build.ps1 -Rid <rid>`（Windows，`:46`）；`fail-fast: false`（`:22`）；cargo 缓存落在 `artifacts/cargo`（`:39`） |
| `actions/upload-artifact`（原生库） | `.github/workflows/ci.yml:47` | 把 `artifacts/native/<rid>/` 上传为 artifact `ratex-<rid>`，`if-no-files-found: error`（`:51`） |
| `actions/download-artifact` | `.github/workflows/ci.yml:70`、`:104` | 后续两个任务把同名 artifact 解到 `artifacts/native/<rid>`，正好是本仓库内可执行项目按 RID 复制原生库的目录（`:72`、`:107`） |
| `STREAMMARK_SNAPSHOT_DIR` | `.github/workflows/ci.yml:78` | 测试任务把截图目录指向 `${{ github.workspace }}/artifacts/snapshots`，随后无条件上传（`if: always()` 在 `:80`，artifact 名 `snapshots-<rid>` 在 `:82`，`if-no-files-found: ignore` 在 `:84`） |
| `aot-smoke` 的 publish + grep 门禁 | `.github/workflows/ci.yml:111`、`:112` | `dotnet publish samples/StreamMark.Gallery -c Release -r <rid> -o artifacts/aot`，用 `tee` 留日志；`grep -E "warning IL[0-9]+" publish.log` 命中即 `::error::` 并 `exit 1` —— 要求 0 个 IL 警告 |
| `aot-smoke` 的复制与冒烟 | `.github/workflows/ci.yml:116`、`:119` | `cp artifacts/native/<rid>/* artifacts/aot/` 后运行 `./artifacts/aot/StreamMark.Gallery --smoke`；`--smoke` 分流见 `samples/StreamMark.Gallery/Program.cs:22` |
| `bench` 任务 | `.github/workflows/ci.yml:124`、`:133`、`:137` | 条件 `github.event_name == 'workflow_dispatch' && inputs.bench`；`dotnet run -c Release --project bench/StreamMark.Benchmarks -- --filter '*' --exporters github`，结果目录 `BenchmarkDotNet.Artifacts/results/` 上传为 artifact（`:134`） |
| `pack-local.sh` 主循环 | `tools/pack-local.sh:7`、`:8` | `for project in Core Avalonia RaTeX Highlighting.TextMate Mermaid Theme.AtomUI`，逐个 `dotnet pack src/StreamMark.$project -c Release -o artifacts/packages -p:Version=$version --nologo -v quiet`；`set -euo pipefail`（`:4`）+ `version="${1:?version required}"`（`:6`） |
| `pack-local.sh` 收尾校验 | `tools/pack-local.sh:10` | `ls artifacts/packages | grep "$version"`：打印本次版本对应的包文件，顺带确认确实产出了该版本 |

## Dependencies
- Uses: `native-ratex` via 文件目录约定 —— `Directory.Build.targets:10`、`:13` 从 `artifacts/native/<rid>/` 读 `*ratex_ffi.*`（这些文件由 `native/build.sh:28` 的 `cp` 写入，`CARGO_TARGET_DIR` 指向 `artifacts/cargo`，`native/build.sh:15`）；CI 里同一目录是 `native-ratex` 任务的 artifact 产物 —— `.github/workflows/ci.yml:47`、`:72`。
- Uses: `gallery-and-benchmarks` via `PublishAot` + 冒烟命令行 —— `samples/StreamMark.Gallery/StreamMark.Gallery.csproj:7` 打开 `PublishAot`，`.github/workflows/ci.yml:111`、`:121` 负责发布与运行；该模块页面也会引用本页（AOT 冒烟目标与 `artifacts/aot`）。
- Uses: `tests` via 解决方案与快照目录 —— `.github/workflows/ci.yml:76`、`:78`；`tests/StreamMark.Avalonia.Tests/HeadlessApp.cs:39` 读 `STREAMMARK_SNAPSHOT_DIR`。
- Used by: 所有 `src/` 模块 via MSBuild 自动导入 —— `Directory.Build.props:3`、`:14`、`:18`（TFM、IL 警告即错误、`IsPackable` / `IsAotCompatible` / XML 文档）与 `Directory.Packages.props:3`（`<PackageReference>` 一律省版本号，例如 `src/StreamMark.Core/StreamMark.Core.csproj:6`、`src/StreamMark.Avalonia/StreamMark.Avalonia.csproj:7`）。
- Used by: `native-ratex`（反向）via `artifacts/` 目录约定与 CI 调用 —— `native/build.sh:15` 把 `CARGO_TARGET_DIR` 指到 `artifacts/cargo`，`.github/workflows/ci.yml:42`、`:46`、`:39`；`native-ratex` 模块页面同样会链接本页。
- 类库内部依赖：`Directory.Packages.props:15` 的 `AtomUI.Core 6.2.5` 只被 `src/StreamMark.Theme.AtomUI/StreamMark.Theme.AtomUI.csproj:8` 直接引用；`Directory.Packages.props:21` 的 `Svg.Skia` 只被 `src/StreamMark.Mermaid/StreamMark.Mermaid.csproj:14` 引用（用 `VersionOverride` 压到 5.1.1）。

## State it owns
- `artifacts/` 是唯一的构建产物根，**整体被忽略**：`.gitignore:9`（`git check-ignore` 确认 `artifacts/native/.../libratex_ffi.dylib`、`artifacts/packages/*.nupkg`、`artifacts/aot/StreamMark.Gallery` 均命中 `.gitignore:9`）。
- `artifacts/native/<rid>/`：原生 RaTeX 动态库（`native-ratex` 写入，本模块读取）。当前有 6 个 RID 的子目录：`osx-x64`、`osx-arm64`、`linux-x64`、`linux-arm64`、`win-x64`、`win-arm64`。
- `artifacts/cargo/`：Rust 构建中间产物（`native/build.sh:15`、`.github/workflows/ci.yml:39`）。
- `artifacts/packages/`：`tools/pack-local.sh:8` 的本地 NuGet 源，当前有 66 个 `.nupkg`（6 个包 × 多个 alpha 版本）。
- `artifacts/aot`（CI，相对工作目录，`.github/workflows/ci.yml:111`）与 `artifacts/aot-x64/`（本机发布产物）等：AOT 发布输出，含二进制与 `*.xml` 文档（`GenerateDocumentationFile` 的产物）。
- `artifacts/snapshots/`：仅 CI 设置（`.github/workflows/ci.yml:78`）；本地测试默认写到测试输出目录下的 `snapshots/`（`tests/StreamMark.Avalonia.Tests/HeadlessApp.cs:39`，`.gitignore:7` 忽略）。
- `BenchmarkDotNet.Artifacts/`（含 `results/`）：基准运行产物，被 `.gitignore:6` 忽略；CI 的 `bench` 任务上传为 artifact（`.github/workflows/ci.yml:134`）。
- 各项目的 `bin/`、`obj/`：`.gitignore:1`、`:2`；`obj/project.assets.json` 里记录了 CPM 与版本覆盖的实际解析结果。
- 纯 CI 产物（不落仓库）：artifact `ratex-<rid>`（`.github/workflows/ci.yml:49`）、`snapshots-<rid>`（`:82`）、`benchmarks`（`:136`）。

## Contracts it exposes
- 项目位置决定身份：只要 `$(MSBuildProjectDirectory)` 含 `/src/`（`Directory.Build.props:18`），就自动 `IsPackable=true`、`IsAotCompatible=true`、`GenerateDocumentationFile=true` 且 `CS1591` 不警告（`:19-22`）。放在 `tests/`、`samples/`、`bench/` 下的项目不吃这组属性，因此默认不可打包。
- 全部项目共享 `net10.0` + `LangVersion=latest` + `Nullable=enable` + `ImplicitUsings=enable`（`Directory.Build.props:3-6`）；包默认版本 `0.1.0`、MIT、公司 / 产品名为 StreamMark（`:7-12`）。
- IL 警告即错误：`Directory.Build.props:14` 的 ID 列表对**每个项目**生效（`IL2026` 反射 / `IL3050` AOT 代码生成 / `IL3000` 单文件路径等都在内）。这是编译期门禁，任何项目引入反射式 API 都会直接把构建打断。
- 非可打包项目按 RID 复制原生库：条件 `'$(IsPackable)' != 'true'` 同时命中"显式 `IsPackable=false`"（`samples/StreamMark.Gallery/StreamMark.Gallery.csproj:4`、`tests/StreamMark.Core.Tests/StreamMark.Core.Tests.csproj:4`、`tests/StreamMark.Avalonia.Tests/StreamMark.Avalonia.Tests.csproj:4`、`bench/StreamMark.Benchmarks/StreamMark.Benchmarks.csproj:4`）与"未设置该属性"的项目；RID 取 `$(RuntimeIdentifier)`，缺省回退 `$(NETCoreSdkPortableRuntimeIdentifier)`；目录不存在时整组 Item 不生成（`Directory.Build.targets:12`），所以没编过原生库也能构建（只是运行时会缺库）。
- NuGet 使用方走另一条路：包内 `runtimes\<rid>\native\` 由 `src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:22` 的 `Pack="true" PackagePath="runtimes\%(RecursiveDir)native\%(Filename)%(Extension)"` 生成，宿主按自身 RID 自动选用，不经过 `Directory.Build.targets`。
- 中央版本清单（`Directory.Packages.props`）：`Avalonia*` 12.1.3（`:5`、`:9-13`）必须与 `AtomUI.Core` 6.2.5（`:15`）配套（注释 `:4`、`:14` 说明与 TCode 一致）；`Markdig 1.4.0`、`TextMateSharp(.Grammars) 2.0.4`、`Mermaider 0.15.1`、`Svg.Skia 5.2.3`；测试栈 `xunit.v3 3.2.2` + `Microsoft.NET.Test.Sdk 18.11.0` + `xunit.runner.visualstudio 3.1.5`；基准 `BenchmarkDotNet 0.15.8`。
- 版本覆盖（`VersionOverride`）是 CPM 的受控逃生口，目前只允许 Mermaid 用：`Svg.Skia 5.1.1` 与 `Svg.Animation 5.1.1`，其余 Svg 托管依赖 5.2.3（`src/StreamMark.Mermaid/StreamMark.Mermaid.csproj:14-21`）。`obj/project.assets.json` 显示实际解析结果就是 `Svg.Skia/5.1.1`、`Svg.Model/5.2.3`、`Svg.Custom/5.2.3`、`ShimSkiaSharp/5.2.3`、`Svg.SceneGraph/5.2.3`。
- SDK 版本：`10.0.300` 起，允许 feature band 内上浮（`global.json:3`、`:4`）；包恢复时把 `NU1605`（降级）当错误（`src/StreamMark.Mermaid/obj/project.assets.json` 的 `warningProperties.warnAsError`），所以中央版本只能往上调、不能悄悄往下降。
- 命令入口：`dotnet build StreamMark.slnx -c Release`（`.github/workflows/ci.yml:74`）、`dotnet test StreamMark.slnx -c Release --no-build`（`:76`）、`dotnet publish samples/StreamMark.Gallery -c Release -r <rid>`（`:111`）、`tools/pack-local.sh <version>`（`tools/pack-local.sh:3`）。
- 原生库文件名契约：复制与打包都只匹配 `*ratex_ffi.*` 且排除 `*.a;*.lib;*.d`（`Directory.Build.targets:13`），所以静态库永远不进输出；`.pdb` 之类不在排除列表里，会被一起复制（`artifacts/aot-x64/` 里能看到 `StreamMark.Gallery.dSYM`）。

## How to extend
- 新增一个项目：放在 `src/StreamMark.Xxx/` 就能自动获得 `IsPackable` / `IsAotCompatible` / XML 文档（`Directory.Build.props:18-22`），再把它加进 `StreamMark.slnx`（照 `StreamMark.slnx:3` 的 `<Project Path="src/...">` 写法，放在对应 `<Folder>` 里），否则 `dotnet build/test StreamMark.slnx` 不会构建它；必须自查 AOT 安全，因为 `IL2026` 等已是错误（`Directory.Build.props:14`）。**漏掉 slnx 条目 → 项目只在被别处引用时才编译，CI 的 aot-smoke 不会覆盖它。**
- 新增包引用：先在 `Directory.Packages.props` 的 `<ItemGroup>` 加 `<PackageVersion Include="X" Version="…" />`（`Directory.Packages.props:7-27`），再到项目里写 `<PackageReference Include="X" />`（**不带版本**，照 `src/StreamMark.Core/StreamMark.Core.csproj:6`）。**漏掉 `PackageVersion` → 恢复期报 NU1008/NU1010（"PackageReference items do not have corresponding PackageVersion"）；在 `PackageReference` 上写死版本 → 报 "should not define the version on the PackageReference items"。**确实需要偏离中央版本时用 `VersionOverride`（`src/StreamMark.Mermaid/StreamMark.Mermaid.csproj:17`），不要改中央版本；把中央版本往下调会撞上 `NU1605` 视为错误。
- 新增一个 NativeAOT 冒烟目标：在 `samples/` 下新建可执行项目并 `IsPackable=false` + `PublishAot=true`（照 `samples/StreamMark.Gallery/StreamMark.Gallery.csproj:4`、`:7`），提供自己的 `--smoke` 分支，然后给 `.github/workflows/ci.yml:91` 的矩阵加 RID 或新增一个任务，publish 后照 `:112` 用 `grep -E "warning IL[0-9]+"` 卡 0 警告，并在运行前照 `:118` 把 `artifacts/native/<rid>/*` 拷到发布目录。**漏掉拷贝 → 原生库不在二进制旁边，冒烟在原生调用处崩；漏掉 `IsPackable=false` → `Directory.Build.targets:13` 不再复制，同时会被当成可打包库。**
- 新增 CI 任务：需要原生库的任务必须 `needs: native-ratex`（`.github/workflows/ci.yml:54`、`:88`）并 `actions/download-artifact` 取 `ratex-<rid>` 到 `artifacts/native/<rid>`（`:70`、`:104`）；需要 SDK 的任务用 `actions/setup-dotnet` + `global-json-file: global.json`（`:67`、`:69`、`:101`、`:103`）。**漏掉 `needs` → 任务与原生构建并发，下载 artifact 失败或拿到上一次的库。**
- 加 / 删一个可发布包：改 `tools/pack-local.sh:7` 的项目名列表（当前 6 个）——这是硬编码的循环，新增 `src/` 项目不会自动进包列表。

## Invariants and traps
- **裁剪 / AOT 警告一律是错误**，列表写死在 `Directory.Build.props:14`（`IL2026`、`IL2046`、`IL2050`、`IL2055`、`IL2057`、`IL2058`、`IL2059`、`IL2060`、`IL2067`、`IL2070`、`IL2072`、`IL2075`、`IL2077`、`IL2080`、`IL2087`、`IL2090`、`IL2091`、`IL2104`、`IL3000`、`IL3050`、`IL3051`、`IL3053`）—— see `Directory.Build.props:WarningsAsErrors`。新出现的 ID 不在列表里就不拦，加代码时留意。注意 `IL3000`/`IL3050` 也在其中，所以任何 `Assembly.Location` 式反射或运行时动态代码都不许进入库。
- **AOT 发布必须 0 个 IL 警告**，且由 CI 用日志 grep 而不是靠 MSBuild 属性保证 —— see `.github/workflows/ci.yml:112`（`grep -E "warning IL[0-9]+" publish.log`）。`IsAotCompatible=true`（`Directory.Build.props:20`）只做分析，`PublishAot` 只在 Gallery 上打开（`samples/StreamMark.Gallery/StreamMark.Gallery.csproj:7`），所以"分析通过"≠"发布干净"，必须有冒烟任务兜底。
- **SDK 不会把 `RuntimeIdentifier` 传给被引用项目**，所以仓库内可执行项目（示例 / 测试 / 基准）靠 `Directory.Build.targets` 按自己的 RID 从 `artifacts/native/<rid>/` 复制原生库 —— see `Directory.Build.targets:8-13`；交叉发布时曾因此复制了错误架构的库（osx-x64 发布拿到 arm64），记录在 `docs/decisions.md:33`。条件里的 `$(NETCoreSdkPortableRuntimeIdentifier)`（`:9`）就是"未指定 RID 时用本机 RID"的兜底。
- **`'$(IsPackable)' != 'true'` 也命中"没写过这个属性"的项目** —— see `Directory.Build.targets:7`。后果：任何新建的项目只要不在 `/src/` 下、又没显式 `IsPackable=false`，也会被塞进原生库复制项；反过来，若某个 `src/` 项目被加进复制列表的目录范围之外就复制不到。判断"是否可打包"时不要依赖"属性未设置"这个状态。
- **NuGet 使用方与仓库内的取库路径完全不同**：使用方按 RID 从包内 `runtimes/<rid>/native/` 自动选用（`src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:29`），仓库内走 `artifacts/native/<rid>/`（`Directory.Build.targets:13`）。只改一边会出现"包能跑、仓库内测试跑不起来"或反之 —— see `docs/decisions.md:33`。
- **`Svg.Skia` 的版本组合是刻意不一致的**：`Directory.Packages.props:21` 声明 5.2.3，但 Mermaid 用 `VersionOverride` 把它压回 5.1.1，只为托管依赖升到 5.2.3（`src/StreamMark.Mermaid/StreamMark.Mermaid.csproj:14-21`）。`Svg.Animation` 必须是 5.1.1（5.2.x 依赖 SkiaSharp 4，与 Avalonia 12.1.3 的 SkiaSharp 3 冲突）；`Svg.Skia` 升到 5.2.x 会把 SkiaSharp 拉到 4 —— see `docs/decisions.md:30`。同时 `NU1605`（降级）被当错误 —— see `src/StreamMark.Mermaid/obj/project.assets.json` 的 `warningProperties.warnAsError`，所以这类覆盖只能靠 `VersionOverride` 表达，不能靠"中央版本调低"。
- **Avalonia 版本与 AtomUI 版本是绑定关系**：`Directory.Packages.props:5` 的 12.1.3 与 `:15` 的 `AtomUI.Core 6.2.5` 必须一起动（注释 `:4`、`:14` 写明与 TCode 使用的组合一致）；单独升 Avalonia 会让 `StreamMark.Theme.AtomUI`（引用 `AtomUI.Core`，`src/StreamMark.Theme.AtomUI/StreamMark.Theme.AtomUI.csproj:8`）对不上。
- **`src/` 判定是字符串包含，不是目录层级**：条件用 `$(MSBuildProjectDirectory).Contains('/src/')`（`Directory.Build.props:18`，用 `[System.IO.Path]::DirectorySeparatorChar` 拼）。任何路径里带 `/src/` 的目录都会继承可打包 + AOT 属性，这是"移动项目目录会改变项目行为"的陷阱。
- **`pack-local.sh` 不会自动发现新包**：项目名列表写死在 `tools/pack-local.sh:7`，且版本必须每次显式传参（`:6` 的 `:?` 会让空参数直接失败）；收尾 `ls | grep "$version"`（`:10`）只是回显，不会因为缺包而报错退出 —— 少打了一个包不会被这个脚本发现。
- **CI 的 `snapshots-<rid>` 上传用 `if-no-files-found: ignore`**（`.github/workflows/ci.yml:84`）而原生库 artifact 用 `error`（`:51`）：前者缺文件是正常（无头测试可能没写截图），后者缺文件说明原生构建没产出，必须失败。

## Tests
- 本模块**没有单元测试**：三个根 `Directory.*` 文件、`global.json`、`StreamMark.slnx`、`tools/pack-local.sh` 都没有对应的测试工程。
- CI 里与被改动构建配置直接相关的校验：
  - `.github/workflows/ci.yml:74`：`dotnet build StreamMark.slnx -c Release` —— 任何 IL 警告（`Directory.Build.props:14`）在这里就变成构建失败。
  - `.github/workflows/ci.yml:76`：`dotnet test StreamMark.slnx -c Release --no-build`（带 `STREAMMARK_SNAPSHOT_DIR`，`:78`）—— 改中央包版本、改 TFM 后最先在这里暴露。
  - `.github/workflows/ci.yml:111`：`dotnet publish samples/StreamMark.Gallery -c Release -r <rid> -o artifacts/aot` 配合 `:112` 的 IL 警告 grep 门禁，`:121` 运行 `--smoke`（`samples/StreamMark.Gallery/Program.cs:22` 分流到 `SmokeTest.Run`）。
  - `.github/workflows/ci.yml:133`：`dotnet run -c Release --project bench/StreamMark.Benchmarks -- --filter '*' --exporters github`，仅 `workflow_dispatch` + `inputs.bench`（`:124`）。
- 本地等价命令见 `README.md:101`（`./native/build.sh` → `dotnet build StreamMark.slnx` → `dotnet test StreamMark.slnx` → `dotnet publish … --smoke`，命令分别位于 `README.md:105`、`:107`、`:108`、`:109`；`tools/pack-local.sh` 的用法在 `README.md:115`）。本次未实际运行任何构建 / 测试 / 打包命令 (unverified)。

## Related flows
None —— 构建与打包不在任何运行时流程的调用链上；`.agents/codemap/flows/` 目录在本次快照中不存在（其他模块页面同样按约定引用而未编写），因此即使有流程页面也无法打开 (unverified)。
��
