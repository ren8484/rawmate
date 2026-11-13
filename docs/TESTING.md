# RAWMate PC 测试指南

## 原则

- 先构建固定测试版，用户验收后再覆盖正式版。
- 文件操作测试只使用专门的临时目录和可恢复文件，不用唯一原片。
- “编译成功”不等于功能或界面正确。

## 1. 构建检查

1.5.3 G01局部修复：版本为Assembly/FileVersion 1.5.3.0、Informational/ProductVersion 1.5.3；UI继续由版本属性派生。运行 `tests\Run-GridHeaderLayoutTests.ps1`（366宽度/摘要模型、7692断言）、原21离屏模型、UX/Grid78及Cull61。测试版和隔离正式候选须检查无旁置RAWMateHeader.png启动、v1.5.3、窄窗口顶栏及Grid/单张切换，关闭后核对状态/error.log与fixture。2026-11-09的G01长摘要实机证据已获验收；G03为已知非阻断限制，其他DPI暂缓、不填PASS。纯布局/版本变更不重跑真实GPS写入；发布报告区分历史实机证据与本轮smoke。1.5.2仅为未发布准备版本。

左栏底栏布局回归：运行 `tests\Run-SidebarFooterLayoutTests.ps1`，覆盖 1920×1080 / 150% 对应的短逻辑客户区、既有最小客户区及 4K / 200% 高客户区模型。版本/设置底栏必须完全位于左栏内，“设置”保持原 26 DIP 高度并与“执行清理”至少间隔 8 DIP；4K 下上方内容尺寸和底栏定位不得改变。模型不替代目标显示设置下的实际截图核验。

1.5.1 发布保持已验收递归功能冻结；仅三个程序集版本属性变化。Test 与隔离正式候选均须显示 v1.5.1（Assembly/FileVersion 1.5.1.0，ProductVersion 1.5.1）。正式候选在无 sidecar PNG 目录启动，使用隔离照片并备份/恢复正式用户 state；重启后核对四种决定和当前路径，来源按原设计只在会话内。旧正式 EXE 在全部验证通过前保持不变。

递归集合候选：`tests\Run-RecursiveDirectoryTests.ps1 -ExifToolPath <用户工具> -BaselineRoot <release-baseline-1.5>`。新隔离夹具覆盖四天 JPG=4/RAW=3、同名路径身份、同目录/sibling/别名 PairKey、跨日期禁止配对、歧义、v2 状态独立、真实路径模拟回收、R survivor、确认后新歧义、权限拒绝（仅夹具 DACL，finally 恢复）、junction、temp/dev 排除、普通目录不排除、跨日实拍复制件 GPS 完整事务、增删子目录/来源/metadata、最新 generation、多选与 2000 文件 Dispatcher heartbeat。不会写用户原片或真的回收文件。仍运行 GPS178 / UX>=77 / Cull61 / Refresh40、原 CreateGpsIfd 9/837/18、JPG integration/evidence/safety；不重新生成九张 baseline。

递归请求授权修改 `GpsFileSafety.ValidateJpg` 的成员判定/提示。UX hash 仅更新该类，并增加“还原这两处文本后必须匹配旧 hash”断言；其余七个类与 ExistingGpsIfd proof 的冻结不变。Refresh 旧“其他目录 cache”夹具移到集合根之外，原断言不删。

递归分类新增测试在相同 runner 内：精确 day1 A / day2 B / day3 已分类 C / day4 D 的 4 JPG、3 RAW、5 pending；父目录分别移动、禁止根聚合和套娃；创建/还原 P/J/R/X、RAW survivor、持久化、selection/current/filter/sort/browse/zoom、来源新路径/坐标与 PairKey。另覆盖创建/还原冲突、重复目标全部拒绝、根名为 jpg 的边界、late target、确认后 identity 改变、祖先目录 rename 阻断、状态保存失败回移、取消前/部分完成、ADS/newline 及 500 文件后台 Dispatcher heartbeat。所有移动只在新 test-output；保留原 70 条扫描/GPS/配对/模拟清理覆盖，不真正回收照片。实际 Test UI 必须另行验证分类按钮、确认汇总、分类后 pending=0、日期结构、跨日期同名、状态与来源保留，再取消分类验证。

No-GPS-IFD 1.5 回归：`tests\Run-CreateGpsIfdTests.ps1 -Mode readonly -PairsPath <before|retained-temp|source 清单> -ExifToolPath <显式 exe> -OutputPath <test-output JSON>` 先只读跑九组完整生产验证、23 类及逐 typed payload/offset 负向检查，必须失败于预期安全类别，禁止 catch-all 冒充通过。全部通过后，`-Mode transactions` 使用全新 test-output working directory，覆盖真实 GpsFileTransaction、Grid executor、private mutation 阻断及继续下一张。另须运行 178 GPS / 61 Cull / 77 UX/Grid、JPG integration/evidence 与实际网格 UI；正式冒烟也只能使用独立 working copy。UX 原八类冻结测试仅更新经授权的 Adapter/JpgGpsIntegrity 两个 hash，六个其他类仍冻结；旧 ExistingGpsIfd MakerNotes 证明主体另行冻结。报告不得把 executor 测试写成已操作真实 UI。

GPS UX / Grid 回归： `tests\Run-GpsUxGridTests.ps1`，覆盖设置/照片菜单、直接动作、来源身份/视图/root、三态 badge、选中分类、默认跳过/明确覆盖、单失败继续、取消剩余和底层八个安全类与写前快照逐字节等价。仍须运行 178 GPS / 61 Cull、真实 JPG 集成及独立 evidence tests。人工 UI 必须在新 test-output 实拍复制件上核验单张、三张多选、混合默认跳过、主动覆盖、设置唯一配置入口，保存截图；不得据编译或模拟测试宣称实际 UI PASS。

GPS 性能回归：`tests\Run-GpsPerformanceTests.ps1 -Mode 3 -Label final` 仅写新 test-output 复制件；记录冷 baseline / 来源缓存两种路径的阶段计时与调用计数。`Run-GpsPerformanceSafetyTests.ps1 -WorkingJpgFolder <本轮实拍写后目录>` 验证 fingerprint 缓存、同 size/time 外部变化、新 file ID、外部 writer、session 锁失效、工具目录变化、合并/独立 binary 提取等价和写后临时文件再次改变的阻断。仍必须重新运行原 178 GPS / 61 Cull、JPG 集成和独立完整性证据脚本；不得用性能 PASS 替代安全 PASS。

JPG GPS 回归先运行 `tests\Run-GpsModelTests.ps1 -ExifToolPath '<用户指定 13.59 exe>'`（模拟写入与真实工具只读探测），另运行 `tests\Run-CullModelTests.ps1`。实拍写入入口 `tests\Run-JpgGpsIntegrationTests.ps1` 仅创建新的 test-output working copy，不写冻结 baseline 或用户原片，不测试 ARW 写入。UI 核验必须实际启动固定 `RAWMate-Test.exe`，执行一次确认写入，并检查当前 GPS 立即更新、P/J/R/X/计数/导航/缩放未改变；DPI 未实测必须记 NOT TESTED。详见 [GPS_METADATA.md](GPS_METADATA.md)。

历史只读诊断 `tests\Run-GpsRealFileTests.ps1` 的 probe/scan 不写文件；preflight 现仅复制来源/目标 JPG 并运行写前检查，不再枚举 RAW。历史 Phase 3B/3B.1 ARW FAIL 保留，不代表 JPG 候选支持 RAW。新完整性门禁拒绝所有 JPG Validate warning/error、未证明的 private/MakerNotes 差异；不得用忽略列表制造 PASS。

```bat
BUILD_RAWMate-Test.cmd
```

通过条件：

- 编译器返回 0。
- 生成 `RAWMate-Test.exe`。
- EXE 可启动且标题为 RAWMate。
- 源码目录存在 `RAWMate.ico` 和 `RAWMateHeader.png`，构建后的 EXE 包含内嵌界面徽标。
- 将 `RAWMate-Test.exe` 单独复制到不含 PNG 的临时目录后仍可启动，界面不依赖旁置资源。

## 2. 最小安全夹具

创建专用目录，内容可复制自测试照片：

```text
test-data/
├─ PAIR01.JPG
├─ PAIR01.ARW
├─ pair02.jpeg
├─ PAIR02.arw
├─ ONLY_JPG.JPG
├─ ONLY_RAW.ARW
├─ ignored.txt
└─ nested/
   └─ MUST_NOT_MOVE.JPG
```

核验：

- 分类后两组配对进入正确目录。
- 扩展名和配对不区分大小写。
- `ONLY_JPG` 和 `ONLY_RAW` 被正确分类并在配对检查中分别报告。
- `ignored.txt` 和 `nested` 内文件不动。
- 预先制造目标同名冲突时不覆盖。
- “取消分类”将 `jpg` / `arw` 照片还原到根目录，并保持内容哈希不变。
- 创建或取消分类后，已有 JPG 的 P/J/R/X 决定以及 RAW 保留记录仍绑定到移动后的正确路径。
- 根目录存在任一同名冲突时，“取消分类”在移动任何文件之前停止。
- 完整还原后只删除已经为空的 `jpg` / `arw` 文件夹。

## 3. 文件操作

- 标记 P/J/R/X 后文件数量和哈希不变；“挑片完成”提示也不移动文件。
- “执行清理”结果：P 两种都留、J 只回收 RAW、R 只回收 JPG、X 回收 JPG+RAW。
- X 必须先尝试 RAW；RAW 回收失败时 JPG 和 X 状态保持不动。R 必须先成功保存 RAW 保留记录，才能尝试回收 JPG。
- 跨目录同名安全回归：A、B 两个根目录各建 `jpg\DSC0001.JPG` 和同名 RAW，仅 A 标 X、B 标 P；在 B 执行清理必须零候选，且不得处理 A 的 JPG 或 B 的 RAW。
- 状态收尾回归：只有实际成功进入回收站的 JPG 才清除其 P/J/R/X 锚点；失败或操作前已不存在的 JPG 保留原决定。R 成功后重启仍能识别仅 RAW 为主动保留结果。
- 路径复用回归：成功回收带 R/X 的 JPG 后，在相同绝对路径新建同名 JPG；新文件必须显示“未决定”，不得继承旧状态。
- 确认后改变决定、修改文件内容/时间戳、形成重复 stem 或让 `jpg`/`arw` 成为链接时，本组必须停止并报告，不得使用旧计划继续清理。
- 清理当前照片成功后，单张和网格都按清理前当前排序进入下一张仍存在照片，末尾回绕；若当前 JPG 回收失败则停留当前照片。
- 取消确认时零文件移动。
- 锁定/无权限/操作前已不存在的文件会出现在失败列表；成功数与失败数必须由精确路径结果计算。
- 回收站中能看到并恢复测试文件。

## 4. 挑片与键盘

- 普通单击另一张只保留新选择。
- `Ctrl` 单击增加/移除单项。
- `Shift` 单击形成连续范围。
- `P`、`J`、`R`、`X` 互斥；重复按键取消决定。
- P/R 在没有唯一同名 RAW 时必须拒绝记录并提示；J 在仅有 JPG 时允许。
- `Delete` 只标废片，不立即移动文件。
- 数字键 `0`–`5` 不改变挑片状态、不显示额外徽标，也不触发自动前进。
- 单张顶栏显示“挑片 / 浏览”导航目标，但不显示自动前进启停按钮；`Caps Lock` 不切换应用功能。旧设置中的 `auto_advance` 与 `advance_target` 行会被安全忽略，并在下次保存时移除。
- 新目录存在未决定照片时默认“挑片”，已全部决定时默认“浏览”，空目录默认“挑片”；在同一目录手动切换后刷新应保持当前选择，切换其他目录或重启时重新按该目录状态判断，不受上一批照片影响。
- “挑片”下，左右键和前后按钮只在未决定照片间双向切换；P/J/R/X 做出决定后跳到下一张未决定照片。前后查找均须跳过全部已决定状态并在首尾回绕；尚未完成但没有其他未决定照片时停留当前照片并提示。
- “浏览”下，左右键和前后按钮按当前排序逐张双向切换；`P`、`X/Delete` 做出决定后直接进入紧邻的下一张照片，并在首尾回绕。
- “寻找下一张未决定”使用完整图库与当前排序，不受可见筛选结果截断；不会落到已经决定的照片。
- 最后一张未决定照片被标记后立即停在当前照片并显示标题为“挑片完成”的提示框；内容显示“已决定 / 总数”、保留、废片与 P/J/R 明细，同一次按键只出现一次，且不会触发任何回收站操作。点击“确定”后当前照片不变，导航目标自动显示为“浏览”，左右键开始按当前排序逐张浏览；若完成前筛选为“未决定”，确定后应恢复“全部”并重新显示整批照片。
- 已决定 100% 时做 P/J/R/X 之间的改判不得再次弹出“挑片完成”。再按当前决定快捷键取消为“未决定”时停留当前照片，不自动前进，也不得错误提示完成；浏览中取消决定保持“浏览”并提示“当前有 N 张未决定照片，可切换到‘挑片’继续处理”。
- 在“保留 / 废片”筛选中修改旗标只刷新当前复查结果，不自动跳到未决定照片，也不切换筛选条件。
- 单张顶栏的状态、导航目标、网格和单张四个控件高度一致、水平间距一致且垂直居中；短标签只显示“挑片”或“浏览”，完整行为通过悬停提示说明。
- 空格双向切换视图，无明显单向延迟或崩溃。

## 5. 网格与性能

- 至少用 60 张 JPG 测试延迟缩略图。
- 运行 `tests\Run-ThumbnailPerformance.ps1 -JpgFolder <目录>`：首次为冷缓存，第二次模拟新进程并必须命中持久缓存；测试只读照片。
- 关闭并重开测试版、在测试版/正式版之间切换后，同一批未变化照片应直接复用共享缩略图；修改源照片后旧缓存必须失效。
- 标记决定及执行清理后，未被移除照片的网格/胶片条不得整批重新出现“正在加载…”或省略号占位。
- 滚动时缩略图逐步加载，界面可响应。
- 左侧固定区无滚动条。
- 右侧只有一条窄的迷你滚动条。
- 四档缩略图大小下，普通文件名保持居中完整显示，P/X 徽标不被裁切；超长文件名保持单行居中并显示省略号，卡片宽度和相邻缩略图位置不变，悬停提示显示完整名称。
- 名称/EXIF 拍摄时间升降序正确；文件修改时间不得影响拍摄时间排序。
- 无有效 EXIF 拍摄时间的 JPG 在升序和降序中都排列在末尾，并以名称稳定排序。
- “全部 / 保留 / 废片 / 未决定”四种筛选计数和内容正确。

## 6. 单张视图

- 双击和视图按钮均能进入，不退出应用。
- 默认“适应”尽量填满预览框且不裁切。
- 适应比例低于 100% 时，单击切到 100% 并再次单击回到适应；适应比例达到或超过 100% 时，单击切到 200% 并再次单击回到适应。
- 使用超长文件名进入单张视图：顶部名称保持单行、居中并显示省略号，右侧视图按钮位置不变，悬停提示显示完整名称。
- 手动输入其他倍率后单击图片，应直接返回适应，不经过额外的 100% 中间状态。
- 可输入 10%–600%；非法输入恢复当前显示。
- 放大后切换下一张仍保持相同比例。
- 图片可平移，导航器视口同步。
- 胶片条选择与主图同步。
- 连续左右切换时相邻图预载降低等待。

## 7. EXIF 与方向

按 `docs/UI_SPEC.md` 的方向矩阵测试横构图和竖构图。照片信息至少用真实 ILCE-6700 JPG 核验：

- 日期
- ILCE-6700
- ISO
- 焦段 mm
- f 值
- 快门秒数：至少覆盖 `1000000/100000000 → 1/100 秒`、`1/400 → 1/400 秒`、`1/30 → 1/30 秒`、`1/1 → 1 秒`、`5/2 → 2.5 秒`

缺失字段应显示 `—`，不能导致崩溃。

## 8. 状态恢复

设置一个非默认组合后关闭并重开：

- 固定暗黑模式，界面中没有主题切换按钮。
- 最近路径顺序。
- 最近目录菜单的“清除路径记录”按钮为圆角警示色；清除后下拉列表立即为空、当前目录继续可用，关闭重开不恢复旧路径；随后刷新有效目录时可重新加入记录。
- 网格尺寸和排序/筛选。
- 单张模式、当前照片和固定缩放倍率。
- P/J/R/X 决定及完成后的仅 RAW 保留记录；“挑片 / 浏览”导航目标不写入 `settings.txt`。

另用一份旧格式 `cull-marks.txt` 验证：有效 P/X 正常迁移；旧 `R` 行不会解释成 1.4 的“仅 RAW”。再破坏 v2 头或内容，程序必须阻止覆盖保存和执行清理，并保留可恢复文件。

确认均恢复；目录不存在时安全退回网格/空状态。

## 8.1 目录切换与刷新

- `tests\Run-GpsDirectoryRefreshTests.ps1`：固定长度合成 JPG 外部 metadata 修改，覆盖无→有、有→无、来源坐标更新/无 GPS/删除/不可读、相同 Windows stamp、旧 generation 注入及连续刷新、root 隔离、手动清除不复活、选择/模式/排序/缩放/缓存保留、当前删除 fallback，以及 500 张后台扫描的 Dispatcher heartbeat。只修改新的 test-output 合成夹具，不使用生产 GPS 写入或完整性门禁。
- 实机测试使用 test-output 实拍副本：外部工具加入/移除 GPS、改来源坐标，再点击原“刷新目录”，不重启，核验网格图标和单张坐标/来源。实机修改可恢复 mtime；全 identity 不变由合成测试独立覆盖。GPS Source > Has GPS > No GPS 的冻结图标设计不得变化。

- 在单张视图浏览目录 A 的照片时切换到目录 B，应立即显示目录 B 当前排序后的第一张 JPG，标题、导航器、胶片条和 EXIF 均不得残留目录 A 的内容。
- 在单张视图切换到没有 `jpg` 照片的目录时，应返回空图库，不继续显示旧照片。
- 刷新同一个目录时，当前照片仍存在则继续显示；当前照片已不存在时改为显示当前排序后的第一张。
- 网格视图切换目录后不保留旧目录的选中项或照片信息。

## 9. 视觉验收

实际启动测试版并截图检查：

- 暗黑模式。
- 原生标题栏为暗色，显示程序图标、白色 `RAWMate` 文字和可用的最小化/最大化/关闭按钮。
- 左侧与主工作区保持平面双栏结构，不出现包裹整个区域的外层圆角卡片或额外窗口内边距。
- 横构图和竖构图。
- 网格和单张。
- 默认/最小/最大化窗口。
- 1920×1080、150% 缩放下首次启动窗口完整位于工作区内，左侧从“照片目录”到“执行清理”全部可见且没有滚动条。
- 1080p 与 4K 下目录概览三个 `*` 列的标题和数量均围绕列中心对齐；分别用 2、25、258、1258、12345 等精确数量检查增长时不偏移列中心，短数字不放大，五位数仅在必要时等比收紧且不得缩写、重叠或裁切单位。目录/分类双列按钮不挤压文字，挑片进度数字与条内百分比完整显示；所有按钮保持 `#436794` 深蓝底且交互时不反转颜色。
- 字重层级清楚：章节标题与按钮为半粗体，路径、目录提示和统计标签为常规字重；目录提示比路径小约 1px 且灰度更低。目录大数字保持半粗体；P/X 徽标与状态数字为半粗体，“保留 / 废片”为常规字重。
- 4K 或高窗口下左侧内容必须从顶部开始，已删除的产品名/副标题区域不得以空白形式残留；多余空间位于左侧操作区下方。
- 左下角版本号在默认、最大化和低高度窗口中均可见、不重叠；1.5 正式版的程序集信息版本为 `1.5.0.0`，界面显示 `v1.5`。
- 左侧进度标题为“已决定 / 总数”，下方只显示 `[P] 保留 数量`和`[X] 废片 数量`两个等宽 `*` 结果区；左项从左侧 12px 向右增长，右项从右侧 12px 向左增长。P/X 徽标固定 18×18、字母以半粗体双向居中；中文与数字必须在同一个 `TextBlock` 中使用相同字号和同一基线，中文为常规字重、数字为半粗体。使用一至五位精确数量检查不溢出、不缩写；不得使用固定坐标或出现 ✓、✕、○ 图标、未决定重复统计、整组状态块背景或多余挑片徽标。
- 目录路径输入区与下拉按钮外轮廓完整圆角；进度条分别在 0%、中间值和 100% 时检查左右端圆角，不能出现方角或溢出。
- 目标 Windows DPI。

任何滚动条、输入框、右键菜单、文字对比或方向问题都不能仅以“代码逻辑正确”判定通过。

## 10. 发布前记录

### 1.5 显式冻结输入

- `FreezeReleaseBaseline.cs` 仅从已验证 test-output 复制件冻结 ExistingGpsIfd 三张 JPG / 配对 RAW、九组 CreateGpsIfd before/after 与 source；所有 mutation 在内存或新副本上，不写 baseline。
- `Verify-ReleaseBaseline.ps1` 必须按冻结 manifest SHA、每项 size/SHA 在测试开始/结束 PASS，不再运行旧 226 历史全图库保护作为 release blocker。历史 NOT PASS 审计保持原样。
- Performance / integration / evidence runner 必须显式提供 `-BaselineRoot` 与 `-ExifToolPath`；integration 还需 `-CreateGpsIfdPairs`，evidence 需本轮 `-WorkingJpgFolder`。不依赖桌面路径或按文件名自动猜输入。
- 本轮实际访问原片仍须开始 freeze、结束 recheck，确认没有 rawmate temp / journal / backup / XMP，ARW 不写入。

在 PR/变更说明记录：

```text
Test build: PASS/FAIL
Classification fixture: PASS/FAIL
Recycle Bin safety: PASS/FAIL
Grid/single keyboard: PASS/FAIL
Portrait/landscape orientation: PASS/FAIL
Dark visual QA: PASS/FAIL
State restoration: PASS/FAIL
Known gaps: ...
```
