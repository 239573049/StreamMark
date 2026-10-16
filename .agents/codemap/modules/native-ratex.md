---
module: native-ratex
sources: ["native/**", "src/StreamMark.RaTeX/RaTeXNative.cs", "src/StreamMark.RaTeX/StreamMark.RaTeX.csproj"]
commit: e01b99a
updated: 2026-10-16
---
# RaTeX 原生库管道

上游 RaTeX 以 git 子模块落在 `native/ratex`，本地补丁放在 `native/patches/*.patch`，三个脚本（`build.sh` / `build.ps1` / `cross-build.sh`）用 cargo 把 `ratex-ffi` 编成动态库并写进固定的 `artifacts/native/<rid>/`；托管侧只通过一个 `LibraryImport` 门面 `RaTeXNative` 调用三个 C ABI 函数，拿到的是 UTF-8 JSON 显示列表。
**不负责：** 显示列表的绘制与数学布局（`avalonia-extensions`，`src/StreamMark.RaTeX/RaTeXMathLayout.cs`、`RaTeXMathRenderer.cs`、`KaTeXFonts.cs`，虽然与 `RaTeXNative.cs` 同项目）；子模块内 Rust 布局引擎本身（`native/ratex/crates/**`，上游代码不在本模块维护范围）；`artifacts/native/<rid>/` 到使用方输出目录的复制与 NuGet 打包（`build-and-packaging`，`Directory.Build.targets:13`、`src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:22`）；CI 的六 RID 矩阵与 AOT 冒烟（`.github/workflows/ci.yml:20`、`:116`）。

## Entry points
| Symbol | File | Role |
|---|---|---|
| `native/build.sh` | `native/build.sh:1` | 本机构建：不给参数时用 `uname` 推 RID（`:8-12`），应用补丁后 `cargo build -p ratex-ffi --release`（`:25`），把 `libratex_ffi.dylib`/`.so`/`ratex_ffi.dll` 中存在的那个拷到 `artifacts/native/<rid>/`（`:27-29`） |
| `native/build.ps1` | `native/build.ps1:1` | Windows 构建：`-Rid`（默认 `win-x64`）+ 可选 `-Target`（默认让 cargo 用宿主工具链，即 msvc）（`:3`、`:14-16`），只拷 `ratex_ffi.dll`（`:19`） |
| `native/cross-build.sh` | `native/cross-build.sh:1` | 单台 macOS 上交叉编出全部六个桌面 RID，用 `build()` 依次调 `cargo build` 或 `cargo zigbuild`（`:11-23`），末尾 `file` 打印产物类型（`:24`） |
| `RaTeXNative.ParseAndLayout` | `src/StreamMark.RaTeX/RaTeXNative.cs:31` | `[LibraryImport("ratex_ffi", EntryPoint = "ratex_parse_and_layout")]`，`string` 按 UTF-8 封送 |
| `RaTeXNative.FreeDisplayList` | `src/StreamMark.RaTeX/RaTeXNative.cs:34` | `ratex_free_display_list`，释放原生 JSON 字符串 |
| `RaTeXNative.GetLastError` | `src/StreamMark.RaTeX/RaTeXNative.cs:37` | `ratex_get_last_error`，返回线程局部错误指针（`byte*`，不当字符串封送） |
| `RaTeXEngine.Layout` | `src/StreamMark.RaTeX/RaTeXEngine.cs:22` | 唯一的托管调用点：填 `RatexOptions`（含 `StructSize`）→ 调原生 → 失败取错误串 / 成功 `JsonSerializer.Deserialize` → **`finally` 里释放原生内存**（`:48-51`） |

## Key types and functions
| Symbol | File | Role |
|---|---|---|
| `RaTeXNative.Library`（`"ratex_ffi"`） | `src/StreamMark.RaTeX/RaTeXNative.cs:8` | 库名常量，决定 .NET 去找 `libratex_ffi.dylib` / `libratex_ffi.so` / `ratex_ffi.dll` |
| `RaTeXNative.RatexOptions` | `src/StreamMark.RaTeX/RaTeXNative.cs:17` | `StructSize` / `DisplayMode`（0 行内、1 块级）/ `Color*`（null = 黑色）；`StructSize` 是上游的前向兼容约定 |
| `RaTeXNative.RatexResult` | `src/StreamMark.RaTeX/RaTeXNative.cs:25` | `byte* Data` + `int ErrorCode`，与 C 端 `RatexResult` 逐字段对应 |
| `RaTeXNative.RatexColor` | `src/StreamMark.RaTeX/RaTeXNative.cs:11` | 四个 `float`（0..1），与 `ratex.h` 的 `RatexColor` 同布局 |
| `MathLayoutResult` | `src/StreamMark.RaTeX/RaTeXEngine.cs:7` | 成功/失败的联合结果；流式输入下公式未写完属正常失败（`src/StreamMark.RaTeX/RaTeXEngine.cs:6`） |
| `DisplayList` | `src/StreamMark.RaTeX/DisplayList.cs:9` | JSON 协议 v1 的反序列化目标：`Version` / `Width` / `Height`（基线以上）/ `Depth`，em 单位、Y 轴向下 |
| `DisplayItem` | `src/StreamMark.RaTeX/DisplayList.cs:18` | 扁平结构（非多态）：`Type` ∈ GlyphPath/Line/Rect/Path，未知类型由绘制端跳过（`src/StreamMark.RaTeX/DisplayList.cs:20`） |
| `PathCommand` / `DisplayColor` | `src/StreamMark.RaTeX/DisplayList.cs:36`、`:48` | MoveTo/LineTo/CubicTo/QuadTo/Close；颜色为 float RGBA |
| `DisplayListJsonContext` | `src/StreamMark.RaTeX/DisplayList.cs:50` | `JsonKnownNamingPolicy.SnakeCaseLower` 源生成上下文（AOT 安全；`char_code`、`display_mode` 这类 snake_case 字段名靠它对齐 `ratex.h:34-41`） |
| `native/patches/0001-enclose-border-uses-current-color.patch` | `native/patches/0001-enclose-border-uses-current-color.patch:1` | 唯一补丁：改 `crates/ratex-layout/src/engine.rs` 的 `layout_enclose`，把 `\fbox`/`\boxed` 边框从 `Color::BLACK` 改成 `options.color` |
| 上游 C 头 | `native/ratex/crates/ratex-ffi/include/ratex.h:111` | ABI 与 JSON 协议的权威文档（`ratex.h:22-25` 线程安全、`:27-61` 协议与坐标系） |
| 上游导出实现 | `native/ratex/crates/ratex-ffi/src/lib.rs:201`、`:273`、`:289` | `ratex_parse_and_layout` / `ratex_free_display_list` / `ratex_get_last_error`，`LAST_ERROR` 为 `thread_local!`（`lib.rs:38`）；`crate-type = ["cdylib", "staticlib"]`（`native/ratex/crates/ratex-ffi/Cargo.toml:12`） |

## Dependencies
- Uses: 上游 RaTeX 子模块 via git submodule — `.gitmodules:1`（`path = native/ratex`，`url = https://github.com/erweixin/RaTeX`）；许可证 MIT，见 `THIRD-PARTY-NOTICES.md:25`。
- Uses: cargo / rustup 工具链 via shell — `native/build.sh:25`、`native/build.ps1:16`；交叉编译额外依赖 zig 与 cargo-zigbuild — `native/cross-build.sh:3`、`:20-23`。
- Uses: `build-and-packaging` 的 MSBuild 复制规则（本模块只产出文件，不负责分发）— `Directory.Build.targets:13`、`src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:22`。
- Used by: `avalonia-extensions`（托管侧渲染/布局）via `RaTeXEngine.Layout` + `DisplayList` — `src/StreamMark.RaTeX/RaTeXMathRenderer.cs:53`、`src/StreamMark.RaTeX/RaTeXMathLayout.cs:32`（构造时传入显示列表）、`src/StreamMark.RaTeX/KaTeXFonts.cs:26`（按 `font`/`char_code` 解析字形，字体来自 `native/ratex/fonts/*.ttf`，见 `src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:17`）。
- Used by: `tests` via 项目引用 `src/StreamMark.RaTeX/StreamMark.RaTeX.csproj` — `tests/StreamMark.Core.Tests/StreamMark.Core.Tests.csproj:14`（`RaTeXEngineTests`）、`tests/StreamMark.Avalonia.Tests/StreamMark.Avalonia.Tests.csproj:17`（`RaTeXMathRendererTests`）。
- Used by: `gallery-and-benchmarks` via `--smoke` 路径 — `samples/StreamMark.Gallery/SmokeTest.cs:33`、`samples/StreamMark.Gallery/Program.cs:22`。
- Used by: CI 三个 job，都先跑 `native-ratex` 矩阵再消费产物 — `.github/workflows/ci.yml:20`、`:54`、`:88`。

## State it owns
- `artifacts/native/<rid>/`：六个 RID 各一个动态库，本次快照中实机存在 `artifacts/native/{linux-x64,linux-arm64,osx-x64,osx-arm64,win-x64,win-arm64}/`（本机目录列举所见；本页未运行任何构建）。
- `artifacts/cargo/`：`CARGO_TARGET_DIR`，由三个脚本统一指向仓库内（`native/build.sh:15`、`native/build.ps1:8`、`native/cross-build.sh:6`）；当前目录下是 `release/` 加六个 rust target 子目录（`aarch64-apple-darwin`、`x86_64-pc-windows-gnu` …）。CI 也把 rust-cache 的 workspace 指到这里（`.github/workflows/ci.yml:39`）。
- 整个 `artifacts/` 被忽略（`.gitignore:9`），`native/ratex/target/` 也被忽略（`.gitignore:8`）——原生产物**不进版本库**，仓库只提交子模块指针与补丁。
- 子模块目录 `native/ratex`：HEAD `e01b99a` 提交的指针是 `ae391d727ac615437c63c308f4538d971a84bede`（`git diff -- native/ratex` 显示 `-dirty`），脏只来自被应用过的补丁：子模块内 `git status --porcelain` 只有 `M crates/ratex-layout/src/engine.rs`，且该文件 `:3784` 已是 `unwrap_or(options.color)`。
- **补丁的应用状态没有任何记录文件**：只能靠对子模块工作区跑 `git apply --check` 反推（`native/build.sh:20`）。`native/ratex/crates/ratex-layout/src/engine.rs` 的未提交改动就是唯一真相。
- 上游字体 `native/ratex/fonts/*.ttf`（20 个文件，OFL）以嵌入资源形式进入包 — `src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:17`、`THIRD-PARTY-NOTICES.md:29`。

## Contracts it exposes
- **C ABI 三个函数**：`ratex_parse_and_layout(const char*, const RatexOptions*) -> RatexResult`、`ratex_free_display_list(char*)`、`ratex_get_last_error() -> const char*` — 声明 `native/ratex/crates/ratex-ffi/include/ratex.h:111`、`:118`、`:127`，实现 `native/ratex/crates/ratex-ffi/src/lib.rs:201`、`:273`、`:289`，托管绑定 `src/StreamMark.RaTeX/RaTeXNative.cs:31`、`:34`、`:37`。**改这里必须三处同步**。
- **库名约定**：`LibraryImport` 的库名 `"ratex_ffi"`（`src/StreamMark.RaTeX/RaTeXNative.cs:8`）必须在各平台解析到 `libratex_ffi.dylib`、`libratex_ffi.so`、`ratex_ffi.dll` 之一——文件名来自 cargo 的 crate 名（`native/ratex/crates/ratex-ffi/Cargo.toml:2`），脚本按这三种文件名拷贝（`native/build.sh:27`）。
- **六个桌面 RID → 产物名**（各脚本的拷贝目标）：`osx-arm64`/`osx-x64` → `libratex_ffi.dylib`；`linux-x64`/`linux-arm64` → `libratex_ffi.so`；`win-x64`/`win-arm64` → `ratex_ffi.dll` — `native/cross-build.sh:18-23`。
- **上游 Cargo target 与 RID 的对应**：`aarch64-apple-darwin`、`x86_64-apple-darwin`、`x86_64-unknown-linux-gnu`、`aarch64-unknown-linux-gnu`、`x86_64-pc-windows-gnu`、`aarch64-pc-windows-gnullvm` — `native/cross-build.sh:18-23`（注意 Windows 走 gnu/gnullvm，而 `native/build.ps1` 默认让 cargo 用宿主的 msvc 工具链，`:14-16`）。
- **显示列表 JSON 协议 v1**：字段与坐标系由上游 `ratex.h:27-61` 定义（em 单位、Y 向下、基线在 y = height），托管侧用 snake_case 源生成上下文反序列化（`src/StreamMark.RaTeX/DisplayList.cs:50`），并按"忽略未知字段/未知条目类型"的前向兼容策略设计（`src/StreamMark.RaTeX/DisplayList.cs:7`、`:20`）。
- **线程契约**：原生错误状态是线程局部的（`native/ratex/crates/ratex-ffi/src/lib.rs:38`、`include/ratex.h:22-25`），所以托管侧可并发调用（`src/StreamMark.RaTeX/RaTeXEngine.cs:12`）；返回的 `Data` 必须在同一次 `Layout` 内用 `FreeDisplayList` 归还（`src/StreamMark.RaTeX/RaTeXEngine.cs:50`）。
- **补丁命名与幂等应用方式**：`native/patches/*.patch`，`git` 格式补丁，文件名 `NNNN-<英文短描述>.patch`（当前只有 `0001-`，`native/patches/0001-enclose-border-uses-current-color.patch:1`）；每个脚本都遵循"先 `apply --check` 成功才 `apply`"，因此**重复构建不失败**（`native/build.sh:20-23`、`native/build.ps1:11-12`、`native/cross-build.sh:9`）。补丁内容本身不记录上游 commit，靠 diff 上下文行号定位（该补丁锚在 `engine.rs:3781` 附近）。
- **包内布局**：`runtimes/<rid>/native/<文件名>`，由 `%(RecursiveDir)` 从 `artifacts/native/` 推导，并显式排除 `*.a`/`*.lib`/`*.d` — `src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:22`。仓库内可执行项目则靠 `Directory.Build.targets:13` 按 `RuntimeIdentifier`（缺省时 `NETCoreSdkPortableRuntimeIdentifier`，`Directory.Build.targets:9`）复制到输出目录。

## How to extend
- **新增/修改一个上游补丁**：在子模块里改代码（或 `git fetch` 后改），用 `git -C native/ratex diff > native/patches/000N-<slug>.patch` 生成补丁；确认三个脚本都会覆盖它——`native/build.sh:18-24`、`native/build.ps1:10-13`、`native/cross-build.sh:7-10`（后两个也照抄了 `--check` 再 `apply`）。漏掉某个脚本 → 该平台上补丁未生效但构建照常成功，症状是行为随构建脚本而异（例如 `\boxed` 边框颜色只在部分 RID 下正确）；反之若把 `apply --check` 删掉直接 `apply`，重复构建会因 "patch does not apply" 失败（`cross-build.sh` 的 `set -e` 会直接中止整轮交叉编译，`native/cross-build.sh:4`）。
- **新增一个 RID**：先在 `native/cross-build.sh:18-23` 的 `build <rid> <target> <build|zigbuild> <file>` 表里加一行（否则该 RID 永远没有产物）；CI 矩阵 `.github/workflows/ci.yml:24-30` 加一行（否则没有 runner 产出可下载的 artifact，`build-test`/`aot-smoke` 的 `download-artifact` 会拿不到库，`:70-73`）；`src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:22` 与 `Directory.Build.targets:13` 是通配，无需改——包内路径就是目录名。
- **升级子模块**：`git -C native/ratex fetch && git checkout <新 commit>`（父仓库随后提交新指针），然后**先验证补丁仍能应用**：`git -C native/ratex apply --check native/patches/0001-*.patch`（本次快照对该补丁的检查返回 1 = 已应用，说明它当前是"应用后"状态；升级后如果上游已经修了同一行，这个检查会因上下文不匹配失败，需要重做成新补丁或删除）；再重跑 `native/build.sh` 覆盖 `artifacts/native/<本机 rid>/`（`:13`）。
- **升级后必须顺手核对托管侧的兼容性假设**：ABI（`native/ratex/crates/ratex-ffi/include/ratex.h` 三个签名与 `RatexOptions` 布局）对 `src/StreamMark.RaTeX/RaTeXNative.cs:17`；JSON 协议对 `src/StreamMark.RaTeX/DisplayList.cs:9`；以及补丁修好之后留下的死代码 `RaTeXMathRenderer.HasUncoloredFrame`（恒 `false`，`src/StreamMark.RaTeX/RaTeXMathRenderer.cs:76`）与 `RaTeXMathLayout` 的 `blackFramesAreDefault` 参数（`src/StreamMark.RaTeX/RaTeXMathLayout.cs:32`）——若上游回退该修复，需要重新接线，否则 `\fbox` 边框在深色主题下会是黑色。
- **改 ABI（加函数 / 加选项字段）**：先改上游 `ratex.h` 与 `lib.rs` 的 `#[no_mangle] pub extern "C"`（`native/ratex/crates/ratex-ffi/src/lib.rs:201`、`:273`、`:289`），再加 `RaTeXNative` 的 `LibraryImport`（`src/StreamMark.RaTeX/RaTeXNative.cs:31`），最后在 `RatexOptions.StructSize` 语义下追加字段而不是插字段（`src/StreamMark.RaTeX/RaTeXNative.cs:19`、`ratex.h:75-76`）——插字段会让旧库把新字段当别的字段读。

## Invariants and traps
- **补丁应用是"检查后应用"，不是"强制应用"**：三个脚本都先 `git apply --check` 成功才 `apply`，失败就静默跳过——这是重复构建不失败、也是"补丁已经应用过"的判定方式 — see `native/build.sh:20`、`native/cross-build.sh:9`、`native/build.ps1:11`。补丁被应用后 `apply --check` 必定失败（本次对 `native/patches/0001-enclose-border-uses-current-color.patch` 实测 exit 1，因为 `native/ratex/crates/ratex-layout/src/engine.rs:3784` 已是 `unwrap_or(options.color)`），这正是期望路径。
- **`set -euo pipefail` 不会把"检查失败"当错误**：`native/cross-build.sh:9` 的失败发生在 `&&` 列表里，实测不触发退出；`native/build.sh:20` 则在 `if` 条件里 — see `native/cross-build.sh:4`、`native/build.sh:20`。所以补丁没应用时**不会有任何提示**，只会少打一行 `applied ...`。
- **构建脚本输出写死在仓库内 `artifacts/native/<rid>/`**，SDK 不会为被引用项目复制原生库，仓库内可执行项目靠根目录的 `Directory.Build.targets` 按 RID 复制 — see `Directory.Build.targets:3-5`、`:13`。漏掉这一步的症状是运行时 `DllNotFoundException: ratex_ffi`（AOT 下是启动即崩），而不是编译错误。
- **`RaTeXEngine.Layout` 必须释放原生内存**：成功路径在 `finally` 里调 `FreeDisplayList` — see `src/StreamMark.RaTeX/RaTeXEngine.cs:50`；`Data` 是上游 `CString::from_raw` 的所有权（`native/ratex/crates/ratex-ffi/src/lib.rs:273`），漏释放即每次排版泄漏一段 JSON。失败路径 `Data` 为 null，不需要释放（`src/StreamMark.RaTeX/RaTeXEngine.cs:37`）。
- **`ratex_get_last_error` 的指针只在下一次同线程调用前有效**，且不能 free — see `native/ratex/crates/ratex-ffi/include/ratex.h:121-125`；托管侧因此立刻 `Marshal.PtrToStringUTF8` 拷贝成 `string`（`src/StreamMark.RaTeX/RaTeXEngine.cs:39`）。
- **交叉编译只能在 macOS 上做**，依赖 rustup 目标 + zig + cargo-zigbuild 三件套 — see `native/cross-build.sh:3`；脚本自己也就这么写着（`native/cross-build.sh:2`）。CI 反过来在各平台原生 runner 上跑各自的 RID（`.github/workflows/ci.yml:24-30`），交叉脚本只用于本地验证与打包。
- **`native/build.sh` 不支持交叉编译**：它只把 `CARGO_BUILD_TARGET` 透传给 cargo（`native/build.sh:25`），若调用方没设这个环境变量而 `rid` 又是别的平台（如 `./native/build.sh linux-x64`），产物目录会落到宿主的 `artifacts/cargo/release/` 并按宿主文件名拷贝（`:26-28`）——RID 目录名与产物平台不一致。
- **`build.ps1` 只拷贝 `ratex_ffi.dll`**（`native/build.ps1:19`），不带 `-Target` 时用的是 Windows 宿主默认工具链；而 `cross-build.sh` 的 Windows 目标是 gnu / gnullvm（`native/cross-build.sh:22-23`）——同叫 `win-x64` 的两条路径产物并不来自同一套 ABI 工具链。
- **`crate-type` 同时产 `staticlib`**（`native/ratex/crates/ratex-ffi/Cargo.toml:12`），所以 `artifacts/native/<rid>/` 里可能存在 `*.a`/`*.lib`；打包与复制规则都显式排除它们（`src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:22`、`Directory.Build.targets:13`）。
- **子模块未初始化时**：`git submodule update --init` 之前 `native/ratex/` 是空目录 — `README.md:104`。症状依次是：`git -C native/ratex apply` 直接 `fatal: cannot change to ...` 且退出码 128（本次实测）；`cargo build --manifest-path native/ratex/Cargo.toml` 因 manifest 不存在而失败（`native/build.sh:25`）；`src/StreamMark.RaTeX/StreamMark.RaTeX.csproj:17` 的字体嵌入 glob 匹配不到任何 `*.ttf`，包/输出里没有 KaTeX 字体，公式全部落到系统字体回退路径（`src/StreamMark.RaTeX/KaTeXFonts.cs:26`、`:49`）。
- **`artifacts/` 是本地状态、不进版本库**（`.gitignore:9`），因此"我从零 clone 能不能跑测试"取决于有没有先跑构建脚本；CI 也必须在测试前把对应 RID 的 artifact 下载回 `artifacts/native/<rid>`（`.github/workflows/ci.yml:70-73`）。
- 与本次快照的 git 事实：父仓库 `native/ratex` 是 `-dirty` 子模块指针（`git diff -- native/ratex`），脏来自 `M crates/ratex-layout/src/engine.rs`；父仓库其余 12 处未提交改动都在 Avalonia 侧与测试里，与本模块无关。

## Tests
- 本模块没有单元测试（`native/` 下只有三个脚本与一个补丁，没有测试工程）。
- 间接依赖原生库的测试：`tests/StreamMark.Core.Tests/RaTeX/RaTeXEngineTests.cs:7`（类注释明写"需要本机已构建 RaTeX 原生库（native/build.sh）"，`:6`；覆盖 `\ce`、`\begin{align}`、`\xrightarrow` 等命令、行内基线度量、未闭合输入报错、`Parallel.For` 200 次并发），以及 `tests/StreamMark.Avalonia.Tests/Math/RaTeXMathRendererTests.cs:11`（度量范围、LRU 缓存、错误传播，走 `RaTeXMathRenderer.Layout`，`src/StreamMark.RaTeX/RaTeXMathRenderer.cs:53`）。
- 前提：先 `git submodule update --init`（`README.md:104`）再 `./native/build.sh`（`README.md:105`），否则两条测试路径都会因找不到 `ratex_ffi` 而失败；CI 里则由 `native-ratex` job 提供产物（`.github/workflows/ci.yml:54`、`:70-73`）。本页未运行任何测试或构建（只做了只读检查：`git submodule status`、`git apply --check`、目录列举、`grep`）。
- NativeAOT 冒烟也直接调原生库：`samples/StreamMark.Gallery/SmokeTest.cs:33`（`ratex.layout`）与 `:36`（`ratex.error`），CI 在 publish 后先把 `<rid>` 目录下所有产物 `cp` 到二进制旁边再跑 `--smoke`（`.github/workflows/ci.yml:116-121`）。

## Related flows
- None（其他模块页面引用的 `../flows/*.md` 在本次快照中不存在：`.agents/codemap/` 下只有 `modules/`，见 `.agents/codemap/modules/avalonia-transcript.md:100`）。
