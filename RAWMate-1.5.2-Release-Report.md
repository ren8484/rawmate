# RAWMate 1.5.2 发布准备报告

> 历史快照：用户随后要求直接升级并发布1.5.3，1.5.2未发布。下文保留当时准备结果，不代表当前版本。正式发布记录见本机test-output/release-1.5.3-20261109/RAWMate-1.5.3-Release-Report.md。

日期：2026-11-09，Asia/Hong_Kong。

**状态：PREPARED / NOT RELEASED。** 用户已接受G01本次测试验收，接受G03为已知非阻断限制，并允许准备1.5.2修复发布。其他DPI组合留待有合适设备或愿意调整设置时继续，不阻塞此次局部修复。未把这个范围决定改写成未测环境PASS。

## 发布范围与冻结依据

- 只包含已验收的BuildGridView G01修复、对应布局回归、版本和发布资料。
- 网格顶栏为Auto标题 / Star受限摘要 / Auto原五按钮。超长摘要省略，按钮原尺寸、顺序、文案、图标、事件不变；未扩大最小窗口。
- 不修改GPS、分类、P/J/R/X、扫描、缓存、清理、transaction/integrity或DPI Manifest。不处理G02，不扩展功能。
- G01的1.5.1测试候选实机证据保存在test-output/display-phase2-20261109；用户验收后版本准备只增加三个程序集版本属性变更。
- 将当前源码的三个1.5.2版本属性归一化回1.5.1后，SHA256精确等于已验收源码：`272F7397A47415BB3DD1CA5F03AE05A47B630EBB3F0270DB03614637C2C61DFB`。因此没有以版本准备为由追加产品修改。

## 当前仓库及版本

| 项目 | 值 |
| --- | --- |
| 仓库/分支 | C:\rawmate / codex/g01-header-layout |
| HEAD | fc507cc619584e1d0934bc0821009ced106fbb1b，未提交新commit |
| 当前正式版 | 1.5.1；RAWMate.exe FileVersion 1.5.1.0 |
| 当前源码/Test | AssemblyVersion/FileVersion 1.5.2.0；Informational/ProductVersion 1.5.2 |
| UI版本来源 | 原AssemblyInformationalVersion派生逻辑保留，无硬编码版本文字 |
| Test资源 | RAWMateHeader.png已嵌入；未调用入口程序检查元数据 |
| 原验收Test备份 | test-output/release-1.5.2-preparation-20261109/accepted-G01-Test.exe |

## 本轮重新执行的检查

| 检查 | 结果/证据 |
| --- | --- |
| BUILD_RAWMate-Test.cmd | PASS，exit0；build.log |
| 原21个离线模型 | PASS，21个执行，按钮越界/裁剪findings=0；offline/offline-layout-results.json |
| G01边界回归 | PASS，366个模型/7692断言，exit0；boundary.log |
| UX/Grid | PASS，78条，fake writes only，exit0；ux-grid.log |
| Cull | PASS，61条，模拟回收，exit0；cull.log |
| 版本/嵌入资源 | PASS；version-and-resources.json |
| 已验收源码冻结 | PASS；除三个版本属性外精确一致，protection-and-scope.json |
| 用户状态/error.log | 11/11 SHA不变；本轮未启动应用，没有状态恢复或日志覆盖 |
| release冻结fixture | 24/24大小/SHA一致，未修改历史baseline |
| git diff --check | PASS |

本轮没有使用computer-use，没有改变Windows显示设置。1.5.2构建没有重新做真实窗口smoke；已接受的G01真实证据属于版本提升前的同一布局候选，不能写成新版本现场操作。最终隔离正式候选的最小smoke仍待正式阶段执行。

GPS178、实拍GPS写入、全量事务/分类集成本轮NOT RUN：生产布局已获验收，本轮仅升版本并回归受影响UI；UX冻结安全断言继续通过。不把未运行测试记PASS，不重写历史FAIL/BLOCKED。

元数据探针首次使用ReflectionOnlyLoadFrom，而当前PowerShell 7不支持该API；此工具侧失败保存在metadata-probe-diagnostic.txt。改用程序集名称/文件版本/资源读取，未调用Program.Main，最终版本和资源验证通过；未修改产品来迎合探针。

## 产物SHA256

| 文件 | SHA256 |
| --- | --- |
| 新RAWMate-Test.exe | 96FF4669735606804E6DFFC862A4246BBD50D65BD8A140D3F64C798FF20FF675 |
| 正式RAWMate.exe，保持原1.5.1 | 68E28B80E615F7B3EFC8F2489FBC95E697D3647725739777D2DC908D2AF7A5DC |
| 原已验收G01 Test备份 | 023F844992F7EDE9AF18283B75DC27680A2AD8AF7F979D57D02657EB8AD9068E |

本轮证据：C:\rawmate\test-output\release-1.5.2-preparation-20261109。EXE、测试输出、照片、ExifTool、日志和用户状态均不进入Git。

## 已知限制与显示范围

- G03：最小窗口第一行EXIF长文本末尾可能裁切；保留为已知非阻断限制，没有顺手修改底部布局。
- 目前物理验收仅3840×2160 / 200%、RAWMate HWND DPI192、System DPI-aware。最小窗口1000×640 DIP外框到最大化，关键操作可用。
- D01和D03–D10仍BLOCKED，可选模式、动态缩放、跨屏迁移NOT TESTED。用户明确同意暂缓，不阻塞此次G01修复。
- 发布范围决定不构成跨设备最低分辨率或Per-Monitor支持声明。

## 已准备的发布资料

CHANGELOG新增1.5.2 Unreleased；README/PROJECT_CONTEXT区分正式1.5.1与候选1.5.2；补充架构/测试/发布流程；docs/RELEASE_NOTES_1.5.2.md为可审阅说明草稿。DPI审计和显示报告补充用户验收决策，原实测、hash和历史结果继续保留。

## 尚未执行的正式步骤

当前“准备发布”没有执行正式替换或远程发布动作。后续明确授权后：

1. 备份并核对正式1.5.1 EXE，构建隔离正式1.5.2候选。
2. 最小正式smoke：版本/独立资源启动、窄窗口顶栏五按钮与Grid/Single切换；保护状态、日志及照片，保留G03。
3. 核验通过后替换正式EXE；若构建/测试/hash异常则停止、保留旧正式版。
4. 收尾active docs，stage白名单源码/文档/tests，复核不含EXE/test-output/ExifTool/JPG/ARW/recovery。
5. 建议commit为`release: RAWMate 1.5.2`，合并main并push，创建注解tag v1.5.2及GitHub Release。
6. 只上传正式RAWMate.exe，下载并复核资产SHA；再填写正式commit、tag、URL和最终hash。

| 正式发布记录 | 当前值 |
| --- | --- |
| Release commit | NOT CREATED |
| v1.5.2 tag | NOT CREATED |
| GitHub Release URL | NOT CREATED |
| 1.5.2正式EXE及资产SHA | NOT BUILT / NOT UPLOADED |

准备检查通过不等于“1.5.2正式版发布完成”。本轮没有commit、merge、push、tag或Release。
