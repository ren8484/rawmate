# RAWMate PC 发布流程

## 1. 测试候选

### 1.5.3 G01局部修复发布

2026-11-09用户明确授权“把正式版更新成1.5.3并发布release”，覆盖本地正式替换及GitHub发布。1.5.2仅为历史准备版本，不创建其正式tag/Release。范围仍为已验收G01、版本、相关回归和说明；G03与其余暂缓DPI不阻塞，未测组合不填PASS。

本轮Test构建、21离屏模型、366边界模型/7692断言、78 UX/Grid、61 Cull均通过；Test和隔离正式候选完成无旁置header资源启动、v1.5.3、窄窗口及Grid/单张切换smoke。旧1.5.1 EXE备份后，以已验证候选原子替换。最终正式SHA256为 `DB182F53FF243091ABE93292345E0E4399FB2861F71AAE0CFEF2863CBCD2F287`。保护11个状态文件、4张隔离照片/RAW和24个冻结fixture；error.log不变，无GPS写入/实际分类或清理。

发布顺序：审查stage仅源码/docs/tests，commit/合并main/push，注解tag v1.5.3，等待Windows CI，创建正式非预发布Release且仅上传RAWMate.exe，下载复核资产SHA。精确commit/CI/Release URL和最终状态保存于本机 `test-output/release-1.5.3-20261109/RAWMate-1.5.3-Release-Report.md`；GitHub入口为 https://github.com/ren8484/rawmate/releases/tag/v1.5.3 。原1.5.2准备记录及DPI失败历史保留。

### 1.5.2 G01 修复发布准备

2026-11-09 用户接受 G01 测试验收，保留 G03 为已知限制，批准准备 1.5.2。范围冻结为 `BuildGridView` 受限摘要/独立按钮布局、相关测试、版本及说明；不增加功能，不改 DPI Manifest 或业务安全代码。

当前仅准备阶段：标准 Test 构建/版本核验，原21布局模型、新G01边界、UX/Grid、Cull回归及文件保护审查。既有4K/200% G01实机证据可继续引用；版本提升不伪装为新增物理DPI测试。G03与其余暂缓DPI组合不阻塞本次局部修复，未测组合仍保持BLOCKED/NOT TESTED。

正式阶段另行明确授权后才执行：备份正式1.5.1 EXE、隔离正式候选构建/最小冒烟、hash与日志保护、最终文档状态收尾、commit/merge/push/tag v1.5.2、GitHub Release及下载资产SHA复核。出现构建/测试/完整性异常即停止。发布真正完成前 README/PROJECT_CONTEXT 保留正式1.5.1，CHANGELOG的1.5.2保持Unreleased。准备清单见根目录 `RAWMate-1.5.2-Release-Report.md`。

### 1.5.1 发布

递归功能已验收后只同步版本元数据（Assembly/FileVersion 1.5.1.0，Informational/ProductVersion 1.5.1），冻结业务实现。重新构建 Test 并运行 134 递归 / 178 GPS / 78 UX / 61 Cull / 40 Refresh、CreateGpsIfd 9/837/18、integration/evidence/performance safety。原 1.5/1.4.1 FAIL/BLOCKED 证据不改写。

正式构建前备份旧 RAWMate.exe 并核对 SHA；`BUILD_RAWMate.cmd "C:\rawmate\test-output\<release-run>\candidate\RAWMate.exe"` 将同一个正式构建输出到隔离目录。仅在全部脚本及该正式候选的递归/分类/还原/决定持久化/GPS/重启冒烟 PASS，原片/基准保护与日志 PASS 后，才原子替换 C:\rawmate\RAWMate.exe。任何失败停止，保留旧正式 EXE。最后 push main、注解 tag v1.5.1、CI、Release 单 EXE 与下载 SHA 复核。

### 1.5 的当前保护基准

发布必须使用专用 `test-output/release-baseline-1.5/` 冻结夹具，不能要求用户长期保留活跃图库。`release-baseline-1.5-manifest.json` 记录 relative path、size、SHA256、role、GPS state、相关结构；manifest 自身 SHA 单独绑定。`tests/Verify-ReleaseBaseline.ps1 -BaselineRoot <冻结目录> -ManifestSha256 <冻结 SHA>` 在开始/结束检查每项身份。baseline 不写入；GPS/mutation/evidence 只用新的 working copy，保留失败材料。

旧 226 inventory 为 Historical / Retired Baseline，205 exact / 16 missing / 5 ARW mismatch 的 NOT PASS 结果保留为审计，不再作为发布 blocker；不删除、不覆盖、不修改旧 SHA，不证明损坏或 RAWMate 修改。

本轮实际访问的真实原图库输入仍须 before/after SHA256 保护，确认无 temp/journal/backup/XMP 且 RAW unchanged。不能用历史退役取消本轮原片保护。

1.5 全套重新运行：测试构建、178 GPS、77 UX/Grid、61 Cull、九组 CreateGpsIfd、837 negatives、18 positive controls、performance safety、JPG integration（Existing/Create/overwrite/failure/RAW/XMP）、independent evidence、performance、40 条 Refresh Directory、baseline/original protection、实际 Test UI，再文档/正式构建/正式 UI、Git audit/commit/main push/`v1.5`/Release/下载 asset SHA。任何当前核心门禁失败停止。

1. 确认源码和文档已保存。
2. 运行 `BUILD_RAWMate-Test.cmd`。
3. 按 `docs/TESTING.md` 完成相关测试。
4. 向用户交付固定名称 `RAWMate-Test.exe` 和测试结果。
5. 等待用户明确确认。

需要异地或扩大样本测试时，可把候选源码和文档推送到 `rawmate/test`；该分支触发 GitHub Actions 并生成名为 `RAWMate-Test` 的构建产物。测试分支不提交 EXE、不移动正式标签，也不代表正式发布。

不要用带时间戳或随机后缀的测试 EXE，以免 Windows 每次重复询问权限。

## 2. 正式构建

仅在用户确认后：

1. 运行 `BUILD_RAWMate.cmd`。
2. 确认只生成正式 `RAWMate.exe`，图标正确。
3. 确认 `RAWMateHeader.png` 已作为资源嵌入 EXE，并在没有旁置 PNG 的目录中完成启动检查。
4. 计算 SHA-256：

```powershell
Get-FileHash .\RAWMate.exe -Algorithm SHA256
```

5. 再做一次启动和核心流程冒烟测试。
6. 更新 `CHANGELOG.md`，提交源码和文档。

## 3. 桌面快捷方式

- 名称：`RAWMate`
- 目标：正式 `RAWMate.exe`
- 起始位置：EXE 所在目录
- 图标：正式 EXE 内嵌图标
- 不创建“RAWMate 照片工具”等重复名称。

## 4. GitHub

- 源码仓库不提交 EXE。
- 普通 PR 的 CI artifact 是测试构建，不代表正式发布。
- 正式发布使用带版本标签的 GitHub Release，并附：
  - `RAWMate.exe`
  - SHA-256
  - 变更摘要
  - 支持的 Windows/运行要求
  - 已知限制
- Release 只分发单个正式 `RAWMate.exe`；不上传压缩包、测试版、测试快捷方式或旁置图片。
- 发布后保证 `README.md`、`CHANGELOG.md`、`PROJECT_CONTEXT.md` 与代码一致。

## 5. 回滚

保留上一正式 Git 标签与 Release。出现严重问题时：

1. 停止分发新 EXE。
2. 指向上一正式 Release。
3. 从上一标签重新构建，不从来历不明的本地 EXE 回滚。
4. 新建 Issue 记录复现、影响和修复验证。
