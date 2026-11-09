# RAWMate Windows 显示兼容性报告

> 1.5.3发布范围：用户已接受G01并授权正式发布1.5.3，G03和其余暂缓DPI不阻塞此局部修复。1.5.3 Test/隔离正式候选已完成最小窗口和视图切换smoke，仍只有当前4K/200%环境证据；不扩展矩阵PASS。最终说明见docs/RELEASE_NOTES_1.5.3.md，以下原hash/截图/FAIL/BLOCKED历史保留。

> 用户验收/发布范围决定：2026-11-09已接受G01本次测试验收，保留G03为已知非阻断限制；允许准备1.5.2，其他DPI待条件合适再测，不阻塞本修复。详见 [1.5.2发布准备报告](RAWMate-1.5.2-Release-Report.md)。本报告的原hash、截图和测试历史保留，不把1.5.2版本准备写成新增实机测试。

> **2026-11-09 最新状态：G01实机确认P1，测试版已修复。D02 / PHYSICAL / 3840×2160 / 200% 完成本轮UI验收，保留最小窗口EXIF文本裁切P2。其余必测组合仍BLOCKED。** 用户随后选择“暂不调整，先交付当前报告”。详见文末Phase 2章节；下列11-08结论和矩阵保留为当时历史，未改写为已实测。

日期：2026-11-08（Asia/Hong_Kong）。对象：RAWMate 1.5.1，main / fc507cc619584e1d0934bc0821009ced106fbb1b。

**结论：本轮只读审计与离线验证完成；真实显示兼容性验收尚未完成，不给出矩阵全部 PASS 或新增支持范围。** 用户明确选择“暂不操作界面，只完成审计与离线验证”，因此没有操作任何 RAWMate 窗口、Windows 显示设置、虚拟显示器或 VM。

## 1. 环境与方法分层

- 实际原生探测：一个活动 DELL U2720QM / 3840×2160、60 Hz，work area 3840×2064，非远程会话。Shell HWND 的 DPI=192 / PMv2，legacy scale API 返回 180；未打开显示设置确认，不将读数合并成 RAWMate 当前 DPI。
- RAWMate 正式/Test 进程均未启动；实际主窗口 DIP/物理大小、运行时 awareness、系统缩放和动态行为均未实测。
- 没有物理环境切换，没有虚拟显示测试，没有实际窗口缩放测试。
- 离线验证：真实生产 BuildUi/CreateTile/BuildPhotoInfoCard 等工厂的 WPF Measure/Arrange/RenderTargetBitmap；明确传入客户区 DIP 尺寸，PNG 输出为 96 DPI。**这些图是离屏渲染，绝不是实机截图或改变 Windows DPI 的证据。**

详细配置/Manifest/源代码证据见 [DPI-AUDIT.md](C:/rawmate/DPI-AUDIT.md)。只读探针和原始 JSON 位于 `C:\rawmate\test-output\display-compatibility-20261108\`。

## 2. 必测矩阵（实机）

| ID | 目标分辨率 | 目标缩放 | 驱动模式列表 | 实际执行的窗口/DPI/环境 | 状态 |
| --- | --- | --- | --- | --- | --- |
| D01 | 3840×2160 | 150% | 有 | 未启动 Test、未切换 | BLOCKED：本轮未授权交互 |
| D02 | 3840×2160 | 200% | 有 | 仅环境探测；shell 192 不等于应用测试 | BLOCKED：未核验 RAWMate |
| D03 | 2560×1440 | 100% | 有 | 未执行 | BLOCKED：本轮未授权交互 |
| D04 | 2560×1440 | 125% | 有 | 未执行 | BLOCKED：本轮未授权交互 |
| D05 | 2560×1440 | 150% | 有 | 未执行 | BLOCKED：本轮未授权交互 |
| D06 | 1920×1080 | 100% | 有 | 未执行 | BLOCKED：本轮未授权交互 |
| D07 | 1920×1080 | 125% | 有 | 未执行 | BLOCKED：本轮未授权交互 |
| D08 | 1920×1080 | 150% | 有 | 未执行 | BLOCKED：本轮未授权交互 |
| D09 | 1366×768 | 100% | 有 | 未执行 | BLOCKED：本轮未授权交互 |
| D10 | 1366×768 | 125% | 有 | 未执行 | BLOCKED：本轮未授权交互 |
| E01 | 2560×1600 | 150% | 有 | 未执行 | NOT TESTED |
| E02 | 3440×1440 | 100% | 未列出 | 未自定义显示模式 | NOT TESTED |
| E03 | 3440×1440 | 125% | 未列出 | 未自定义显示模式 | NOT TESTED |

驱动列出分辨率不等于已验证该分辨率/缩放组合。用户先前日常使用 4K / 1080p 的反馈可作背景，不能补填上述正式矩阵。没有将尺寸模型换算成 D01–D10 PASS。

## 3. 离线模型基线

使用 `OfflineLayoutAudit.cs` 编译测试 runner，链接本轮未修改的 RAWMate.cs。用 FormatterServices 创建不运行 MainWindow 构造函数的测试状态，逐字段初始化新控件/集合；不调用 Window.Show、ShowDialog 或 Application.Run，不发送输入，不注册实际应用窗口。

使用当前生产 UI 工厂；每个模型独立一棵控件树。用四个新副本：LANDSCAPE.JPG、PORTRAIT.JPG、长文件名竖图 JPG、PORTRAIT.ARW。横/竖图通过生产 LoadOrientedJpeg 读取；仅使用 512 像素派生图注入缓存。来源只通过 WIC 读取，未启动 ExifTool 或 GPS transaction。单张适应倍率与 viewport 调用生产方法做离线布局，模型倍率不代表原图画质/100% 检查结果。

为避免没有 HWND/Dispatcher 的夹具进入后台调度，测试侧预置 queued 标志、图像和计数。首次夹具漏设 thumbnailLoadQueued 引起 NullReferenceException，修订夹具后重跑；生产源码没有对应修改。该夹具错误不计产品崩溃或 P0。没有通过异常捕获伪装成功。

每个尺寸输出普通 Grid（真实样式的 3/3 筛选摘要）、五位数摘要 stress Grid、单张，共 **21 个模型**：

| 模型 | 客户区 DIP 宽×高 | 普通 Grid | stress Grid | 单张按钮边界检查 |
| --- | --- | --- | --- | --- |
| M01 | 1380×860 | 未发现按钮越界 | 未发现按钮越界 | 未发现按钮越界 |
| M02 | 1000×640 | 未发现按钮越界 | 越界，见 G01 | 未发现按钮越界 |
| M03 | 1280×640 | 未发现按钮越界 | 未发现按钮越界 | 未发现按钮越界 |
| M04 | 1093×566 | 未发现按钮越界 | 未发现按钮越界 | 未发现按钮越界 |
| M05 | 1707×912 | 未发现按钮越界 | 未发现按钮越界 | 未发现按钮越界 |
| M06 | 1920×1000 | 未发现按钮越界 | 未发现按钮越界 | 未发现按钮越界 |
| M07 | 984×600 | 未发现按钮越界 | 越界，见 G01 | 未发现按钮越界 |

M07 是更窄客户区的边界样本，可用于后续核对约 1000 DIP 外框的实际客户区；**没有测量真实非客户区，不能把 M07 宣称为某个 Windows 窗口或 DPI 的实测尺寸**。M04 同样只是显式给定的布局尺寸，不等于 D10 的实际工作区。

“未发现按钮越界”仅表示本夹具中可见 Button 未超根矩形或 ClipToBounds 祖先；不是字体清晰度、命中测试、所有文本、弹窗、图片交互和启动稳定性 PASS。文件名/EXIF/source 的正常 ellipsis 不是缺陷。模型中 12345 是压力显示值，实际文件不是 12345 张；决定与数量由测试侧注入，不评价业务统计。

## 4. 问题与分型

### G01 — 窄 Grid 顶栏长摘要溢出（P1 候选，实机等级待复核）

离线稳定复现：

1. 构建真实 Grid UI，设客户区 1000×640 DIP。
2. 筛选摘要设为“筛选：全部照片 · 12345/12345 张 · 单击选择，双击单张浏览”。
3. 进行 Measure/Arrange；顶栏横排实际宽度 691 DIP，从 x=332 延伸到 x=1023。
4. 最右“单张视图”按钮 bounds 为 x=991、y=10、w=32、h=28；超出 1000 客户区约 23 DIP，只有约 9 DIP 留在根范围。
5. 984×600 模型中按钮 x=991 已完全位于根矩形之外。普通短计数在本次模型中未越界。

根因：BuildGridView 把统计摘要和所有按钮放入同一 Auto 列的横向 StackPanel；没有优先收缩/省略摘要的空间分配。该模型已证明几何溢出，但实际 OS 客户区、点击可达性和 DPI 影响未验证。因此列为 **P1 候选**，不宣布“实机没有 P1”。

证据：[M02stress-grid.png](C:/rawmate/test-output/display-compatibility-20261108/renders/M02stress-grid.png)、[M07stress-grid.png](C:/rawmate/test-output/display-compatibility-20261108/renders/M07stress-grid.png)、offline-layout-results.json 中对应按钮 bounds。

最小方案候选：让摘要使用受约束的 Star 区和 ellipsis，操作按钮使用独立 Auto 区；保持按钮、事件和业务不变。**本轮只读/离线范围没有修改生产代码；修复和真实窗口回归尚未执行。**

### G02 — 窗口位置/大小恢复未实现（P2，静态行为差异）

SaveSettings/LoadSettings 无窗口几何字段，启动 CenterScreen。因而不能按现有源码保证“关闭后恢复窗口位置”；这不是本轮引入的回归。实际移动、关闭和重启未执行。应先明确该项是否要求新增位置持久化，不能在 DPI QA 中顺手加功能。

### R01 / R02 — 尚未复现的风险，不计已确认缺陷

- R01：没有显式 PMv1/PMv2 配置/处理，动态缩放、跨不同 DPI 屏幕的清晰度和重排待测。单屏硬件也不能代替跨屏测试。
- R02：弹窗固定宽度、外层 StackPanel 与工作区 MaxHeight 限制可能让低高度下按钮不可见；当前尚无实际弹窗复现，保持 NOT TESTED，不凭源码直接修改。

没有发现产品 P0 的实际证据；也没有执行足够实机测试来排除 P0/P1。G01 尚未完成实机复核/修复，不能宣布完成整个兼容性验收。

## 5. Phase 3 功能验收状态

| 范围 | 本轮证据 | 仍未验证 |
| --- | --- | --- |
| A 主窗口 | 默认/边界尺寸的生产控件离线布局；侧栏缩放与底行可见；G01 | 正常启动、原生标题栏、最大化/还原/真实拖动、字体清晰度 |
| B 浏览 | 横竖 JPG 解码、Grid 卡片/长文件名、省略、单张/胶片条/导航器布局模型 | 实际切图、缩放输入、平移/导航器拖动、实际 DPI 下重绘 |
| C 操作 | 按钮几何与 GPS 来源/操作行模型；78 UX 与 61 Cull 回归 | 实际点击 P/J/R/X、分类进度、还原、配对/GPS/清理/错误确认弹窗 |
| D 小窗口/恢复 | 7 个客户区尺寸模型，G02 静态差异 | 真正支持的最小外框、缩小再放大、窗口移动和重启位置 |
| E 动态 DPI | 只有只读环境探针 | 全部动态缩放/重启后 DPI 对比及跨显示器测试 |

没有对任何文件执行移动、分类、回收、删除或 GPS 写入。原有功能按钮可见的离线图不等于操作已通过。

## 6. 构建、回归及保护

- 构建：固定 RAWMate-Test.exe，1.5.1.0 / ProductVersion 1.5.1，SHA256 **A45BFBB603AFEC63FB9EA5B3056B0BA1ACCBAD49BED7BC146EB17FB83C5D523B**。
- 离线布局 runner 完成 21 个模型，保留 G01 的 FAIL_FINDING，不将其转成整体 PASS。
- GPS UX/Grid **78 PASS**（fake 写入），Cull **61 PASS**（模拟回收）；runner exit 0，日志 regression.txt。没有生产修复，因此没有无意义重复整套 GPS 实拍写入回归。
- RAWMate.cs SHA **107C091402087569ACF8AE9454595C24C1815A5DB099E08EAA8E66C5680719C7** 不变。
- 正式 RAWMate.exe SHA **68E28B80E615F7B3EFC8F2489FBC95E697D3647725739777D2DC908D2AF7A5DC** 不变。
- 4/4 fixture SHA 不变，照片子树没有新增文件；正式/Test 原有 state 与日志 SHA 均不变。
- 当前冻结 release baseline **24/24 size/SHA PASS**；旧发布/历史报告不改写。
- 仅新增本报告/DPI-AUDIT 和独立 test-output 证据；未改生产源码、Manifest、DPI 设置、构建脚本；未 commit / merge / push / tag / Release。

## 7. 证据索引

根：`C:\rawmate\test-output\display-compatibility-20261108\`。

- DisplayProbe.cs / DisplayProbe-v2.exe、display-environment.json / display-environment-v2.json：原生环境观察与 DPI 证据限制。
- RAWMate.exe.manifest.xml / RAWMate-Test.exe.manifest.xml：实际 RT_MANIFEST 原文。
- dpi-overrides.json：正式/Test 规范路径的 HKCU/HKLM AppCompat 层覆盖查询，均无覆盖；无旁置 exe.config / exe.manifest。
- os-runtime.json：OS/.NET/编译器元数据；artifacts-before.json / test-build.json：产物身份。
- OfflineLayoutAudit.cs / .exe、offline-layout-run.txt、offline-layout-results.json：真实工厂、范围限制、21 个模型和 bounds。
- renders/M01-grid.png 到 M07-single.png，以及各 stress-grid.png：**21 张离线 PNG**。
- fixtures-before.json、state-before/、protection.txt、baseline-protection.txt、regression.txt：保护和回归。

代表图：[默认 Grid](C:/rawmate/test-output/display-compatibility-20261108/renders/M01-grid.png)、[1000 DIP 长摘要溢出](C:/rawmate/test-output/display-compatibility-20261108/renders/M02stress-grid.png)、[1093×566 单张模型](C:/rawmate/test-output/display-compatibility-20261108/renders/M04-single.png)。本轮真实 UI 截图数量为 **0**。

## 8. 最低尺寸与支持范围

源码首选最小值为 1000×640 DIP 外框，但不是此次验证得到的兼容性保证。离线在 984/1000 DIP 宽的 stress Grid 有问题；1093×566 及更宽样本未发现同类按钮越界，不能据此直接映射到某一物理分辨率。

后续实机可从 **至少约 1100 DIP 客户区宽** 的保守起点检查，再下探；这是由离线边界提出的测试建议，**不是已验证的最小外框或正式支持承诺**。最低物理显示分辨率、允许 DPI、实际最小高度尚无法确定。

本轮建立的是可重复的源码/Manifest/离线布局证据基线，尚未建立新的实机支持范围。完整完成标准仍未满足：D01–D10、真实弹窗、窗口交互/位置恢复、动态 DPI 与 G01 实机严重度均待后续界面授权。当前阶段按用户选择结束，不更改已发布产品。

## Phase 2 实机验收与G01定向修复（2026-11-09）

### 1. 结论及范围

**G01真实窗口复现为P1，测试候选已解决；D02物理4K/200%完成本轮UI验收，无未解决P0/P1，保留G03/P2文本裁切。其余矩阵未完成。** 用户随后选择“暂不调整，先交付当前报告”，本轮不切换系统显示设置。

工作目录C:\rawmate；分支codex/g01-header-layout；HEAD仍为fc507cc619584e1d0934bc0821009ced106fbb1b，未提交。唯一产品变更是BuildGridView顶栏布局（9行新增、2行删除），没有GPS/source/session/write/transaction/integrity/cache、Grid选择、分类、P/J/R/X或清理逻辑改动。没有Manifest、最小窗口配置或G02位置恢复修改。未覆盖正式EXE，未改已发布v1.5.1历史。

独立证据目录：C:\rawmate\test-output\display-phase2-20261109。11-08审计、离线图片和失败历史原样保留；本节不把旧结果追改为PASS。

### 2. 实际显示环境及窗口

| 项目 | 实测 |
| --- | --- |
| 环境 | PHYSICAL；当前单台Dell U2720QM；非VM/虚拟DPI测试 |
| Windows设置 | 3840×2160，200%；只读确认，未改变 |
| 当前显卡模式 | 3840×2160 / 60 Hz |
| 标准Test路径/PID/HWND | C:\rawmate\RAWMate-Test.exe / 35324 / 1969864 |
| 自身HWND DPI | 192，GetDpiForWindow |
| 进程/窗口awareness | System DPI-aware，值1；不是PMv1/v2 |
| WPF device scale | 2，真实MainWindow宿主测量 |
| 普通外框/客户区 | 2760×1720 / 2734×1649物理px；外框1380×860 DIP |
| 最小外框/客户区 | 2000×1280 / 1974×1209物理px；外框1000×640、客户区987×604.5 DIP |
| 最大化客户区 | 3840×2019物理px |

标准产物原生观测JSON：standard-runtime-normal.json、standard-runtime-minimum.json、standard-runtime-maximized-final.json。外部只读探针只临时改变自身测量线程的DPI坐标上下文，finally恢复；未改变目标或系统。首次虚拟化坐标诊断另存，不拿它冒充物理大小。Windows设置原值截图windows-display-readonly.png。没有用Shell DPI替代RAWMate DPI。

### 3. G01复现、严重度与修复

测试专用宿主G01LiveFixture.cs链接生产MainWindow，用真实构造函数/窗口/message loop。F9只替换galleryCaption.Text为“筛选：全部照片 · 12345/12345 张 · 单击选择，双击单张浏览”，不改统计、枚举或业务；F8记录按钮边界/命中。修复前后宿主在before-live / after-live，最终标准Test另由BUILD_RAWMate-Test.cmd构建和运行，不含测试hook。

| 同一最小客户区987 DIP、按钮32×28 DIP | 修复前 | 修复后 |
| --- | --- | --- |
| Grid按钮范围 | 951–983，完整命中 | 903–935，完整命中 |
| Single按钮范围 | 991–1023，完全在客户区外，centerHit=false | 943–975，完整在客户区内，centerHit=true |
| 摘要 | Auto横向栈，无省略 | 独立受限Star列，NoWrap/CharacterEllipsis |
| 真实操作 | Single不可访问（几何/命中） | 实际点击Single进入照片，再点Grid返回 |

JSON：before-live/005-stress-toggle.json、after-live/004-stress-toggle.json。G01为**P1，关键视图切换不可访问**；正常3/3摘要在最小窗口与“图库”标题挤占也实机确认，属同根因P2。

最小修复：标题/摘要/原五按钮采用Auto / Star / Auto三列，只有摘要让出宽度。原按钮尺寸、顺序、文字、图标、事件完全不变；不增加最小窗口宽度。正常摘要和长摘要均不再挤占标题或按钮。

真实截图（不是离屏渲染）：

- [修复前长摘要](test-output/display-phase2-20261109/before-live/min-stress.png)
- [修复后同尺寸长摘要](test-output/display-phase2-20261109/after-live/min-stress.png)
- [真实点击Single后的照片](test-output/display-phase2-20261109/after-live/min-single-landscape.png)
- [标准Test最小Grid](test-output/display-phase2-20261109/standard-min-small-grid.png)
- [标准Test最大化](test-output/display-phase2-20261109/standard-maximized-stable.png)

### 4. 当前实机矩阵（与11-08历史分开）

| ID | 分辨率 | 缩放 | 本轮实际环境 | 结果 |
| --- | --- | --- | --- | --- |
| D01 | 3840×2160 | 150% | 未切换 | BLOCKED：用户暂不调整设置 |
| D02 | 3840×2160 | 200% | PHYSICAL，Windows设置+自身HWND192 DPI；普通/最大化/最小窗口 | PASS（本轮UI范围，保留G03 P2） |
| D03 | 2560×1440 | 100% | 未切换 | BLOCKED |
| D04 | 2560×1440 | 125% | 未切换 | BLOCKED |
| D05 | 2560×1440 | 150% | 未切换 | BLOCKED |
| D06 | 1920×1080 | 100% | 未切换 | BLOCKED |
| D07 | 1920×1080 | 125% | 未切换 | BLOCKED |
| D08 | 1920×1080 | 150% | 未切换 | BLOCKED |
| D09 | 1366×768 | 100% | 未切换 | BLOCKED |
| D10 | 1366×768 | 125% | 未切换 | BLOCKED |
| E01 | 2560×1600 | 150% | 未执行 | NOT TESTED |
| E02/E03 | 3440×1440 | 100%/125% | 未使用该模式 | NOT TESTED |
| 动态缩放 | 单屏运行时切换 | — | 未改变设置 | NOT TESTED |
| 跨屏迁移 | 不同DPI多显示器 | — | 无真实双屏条件 | NOT TESTED |

无虚拟显示或VM验收。离线DIP模型、可枚举的显示模式都不补填物理矩阵。没有更改显示设置，结束时仍是原3840×2160、200%。

### 5. D02真实UI明细

仅使用独立副本：横图、竖图、长文件名、JPG/ARW配对。原4副本保持原路径；为启用取消分类确认入口，又复制一组到photos/organized/jpg和arw，这2个新副本也单独先冻结SHA。没有移动、改写或回收照片。

| 检查 | 实际执行及结果 | 证据（相对本轮目录） |
| --- | --- | --- |
| 启动、版本 | 标准Test启动正常，v1.5.1可见 | standard-normal.png |
| 最大化/还原/最小化 | 标题栏真实操作；最小化时进程Responding；恢复可操作 | standard-maximized-stable.png；standard-runtime-minimized.json |
| 最小窗口 | 拖至1000×640 DIP；侧栏、GPS、视图按钮可访问 | standard-runtime-minimum.json；standard-min-grid-source.png |
| 缩小再放大 | 最小窗口再次最大化，无布局异常 | standard-resized-maximized.png |
| Grid/Single、横竖图 | 实际点击切换；方向、比例正常，长文件名省略 | after-live/min-single-landscape.png；standard-min-single-portrait.png |
| 缩放/平移/导航器/胶片条 | 28%适应→输入100%确认；主图拖动、导航器拖动改变视口，再恢复适应 | standard-min-zoom-pan.png；standard-min-navigator.png |
| P/J/R/X | 测试副本各触发；无配对R先显示安全提示，配对照片R生效；只有状态改动 | state-after/cull-marks-v2.txt；standard-no-raw-warning.png；standard-min-decisions.png |
| 挑片/浏览 | 点击切入浏览，继续照片导航 | standard-min-decisions.png |
| GPS来源及底部区 | 横图设session来源，在无GPS竖图显示来源、禁用设为来源、应用按钮 | standard-min-single-portrait.png |
| GPS确认 | 应用/取消完整可见，点击取消；未写入 | standard-gps-confirm-stable.png |
| 创建分类 | 进度+确认，可移动4；选择否，无移动 | standard-classify-stable-0.png、-1.png |
| 取消分类 | 预分类副本使入口可用，计划2；选择否，无移动 | standard-undo-stable-0.png、-1.png |
| 配对报告 | 完整2、仅JPG2、仅RAW0、异常0；只报告，关闭 | standard-pair-check-0.png、-1.png |
| 清理确认 | J/R/X及JPG2/RAW0汇总可见；选择否，无回收 | standard-cleanup-confirm.png |
| 设置 | ExifTool可用13.59、完整身份已验证；不改设置，关闭 | standard-settings.png |
| 顶栏菜单 | 最小窗口打开筛选/排序/缩略图菜单；实际选择拍摄时间升序、小缩略图 | standard-min-filter-*.png、standard-min-sort-*.png、standard-min-thumbnail-*.png |
| 刷新目录 | 只读刷新到4JPG/2RAW，当前照片和模式继续可用 | standard-min-small-grid.png |
| 位置恢复 | G02按用户要求排除，不作为PASS项目 | OUT OF SCOPE / NOT TESTED |

弹窗分别保留父窗口与独立owned窗口截图（-0/-1），不会把父截图边界裁出的部分误判为弹窗按钮缺失。Windows动画过渡帧另外保留，结论使用稳定截图。Computer-use出现瞬时“no monitor found”或geometry不可用时重新观察/恢复RAWmate窗口，没有将未知动作结果当PASS；程序error.log未新增异常。

### 6. 问题与限制

| ID | 等级 | 证据与结论 | 状态 |
| --- | --- | --- | --- |
| G01 | P1（短摘要标题挤占P2） | 4K/200%，最小987 DIP客户区，Single完全越界；Auto栈无约束根因 | 测试候选修复，正式EXE未改 |
| G03 | P2 | 最小窗口第一行EXIF长文本末尾裁切；主要浏览/GPS可用，放大可显示 | 记录限制，不扩展本轮顶栏修改 |
| G02 | 原审计项 | 窗口位置持久化 | 本轮明确排除 |
| R02 | 原离线风险 | 当前4K/200%弹窗按钮完整 | 其他小工作区仍待实测，不能全局关闭风险 |

G03见standard-min-single-portrait.png / standard-min-grid-source.png。已测试组合没有未解决P0/P1；最小窗口不承诺所有EXIF长文本完整展开。

### 7. 回归和完整性证据

| 检查 | 本轮结果 |
| --- | --- |
| BUILD_RAWMate-Test.cmd | exit0，构建标准Test；未正式构建 |
| 原21离线模型 | 21/21执行，按钮越界/裁剪findings=0；历史2个压力失败保留 |
| 新GridHeaderLayoutTests | exit0，366模型/7692断言；984–1101逐DIP边界及1280/1380/1707/1920，三种摘要含2048字符压力 |
| UX/Grid | exit0，78/78；fake writes only，冻结安全断言保留 |
| Cull | exit0，61/61；模拟回收 |
| 实机受影响UI | D02范围完成；G01真实点击通过；G03/P2保留 |
| GPS178/实拍写入/全量事务 | NOT RUN：唯一生产变更为BuildGridView布局，无GPS/安全业务修改；UI确认均取消。UX冻结断言和局部源码diff支持此范围判断；不把未运行项目写PASS |
| 隔离照片 | 6/6长度/SHA不变，含2ARW；无新增temp/journal/backup/XMP |
| 冻结release fixture | 24/24长度/SHA一致；manifest SHA仍CDFDB44DF98A36BF21F3AE0987036B44AA928602409BFB667179CCE186624321 |
| 用户状态及日志 | 正式7/7、Test4/4恢复原SHA；共享派生日期缓存退出变化已留档并恢复；error.log全程SHA不变 |
| Git | diff --check通过，HEAD未变；无commit/merge/push/tag/Release |

新增tests/GridHeaderLayoutTests.cs及对应runner直接调用生产BuildUi，断言摘要受限、省略、标题/摘要不重叠、五按钮顺序/尺寸/完整边界，不复制产品UI。原OfflineLayoutAudit.cs不变，复制到新目录链接新源码重跑；抽查原两失败压力图和最小单张图，仍标OFFLINE，不作为真实截图。

| 文件 | SHA256 |
| --- | --- |
| RAWMate-Test.exe（交付） | 023F844992F7EDE9AF18283B75DC27680A2AD8AF7F979D57D02657EB8AD9068E |
| RAWMate.exe（正式，未覆盖） | 68E28B80E615F7B3EFC8F2489FBC95E697D3647725739777D2DC908D2AF7A5DC |
| RAWMate.cs（本轮） | 272F7397A47415BB3DD1CA5F03AE05A47B630EBB3F0270DB03614637C2C61DFB |

日志build.log、boundary.log、ux-grid.log、cull.log；结构化证据offline-after/offline-layout-results.json、protection-partial.json、photos-baseline-protection.json。测试程序已关闭，正式产物未启动/覆盖。

### 8. 可靠支持范围

当前仅有此物理Dell显示器的3840×2160 / 200% / System DPI-aware / 暗色UI验收证据。窗口从1000×640 DIP外框（2000×1280物理px）到最大化，核心浏览和操作可用，保留G03/P2。普通1380×860 DIP可减少长文本裁切。最小窗口是本环境操作下限，不代表1366×768/125%等组合获支持。

不能据此确定跨设备最低显示分辨率、宣称1080p/1440p已验收，或承诺Per-Monitor动态迁移。其余组合等待未来单独授权后按同一流程验证。本轮不发布正式版本。
