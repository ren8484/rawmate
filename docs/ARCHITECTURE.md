# RAWMate PC 架构

## 总览

RAWMate 是单进程、单窗口、纯本地 WPF 程序。业务、状态和界面都在 `RAWMate.cs` 中，应用不访问网络。

```text
Program.Main
  └─ MainWindow
      ├─ 目录扫描/分类/配对
      ├─ 网格与缩略图延迟加载
      ├─ 单张图异步加载与 LRU 缓存
      ├─ P/J/R/X 决定和筛选排序
      ├─ EXIF 方向与信息
      ├─ 设置/会话持久化
      └─ Windows 回收站 P/Invoke
```

## 启动与异常

`Program.Main` 创建 WPF `Application`，注册 `DispatcherUnhandledException`，再运行 `MainWindow`。未处理的 UI 异常写入 `%LOCALAPPDATA%\RAWMate\error.log`，用于定位“点击后应用关闭/重启”类问题。

## UI 结构

`BuildUi` 创建两列：

- 固定宽度左侧栏：目录、分类统计、挑片进度和操作；界面固定为暗黑模式。
- 自适应右侧：网格或单张主视图，下方照片信息。

UI 全由 `Build*` 方法程序化创建。切换网格/单张视图或应用筛选、排序时，`RebuildUi` 会重建可视树；这是因为 WPF 控件不能同时成为两个逻辑树的子节点。重建时需保留业务状态，避免重复同步解码全部缩略图。

## 目录数据流

### 扫描

`ScanDirectory`：

1. 读取所选根目录顶层 JPG/JPEG/ARW。
2. 读取 `root\jpg` 和 `root\arw` 顶层。
3. 更新三个计数和目录提示。
4. 按当前 `jpg` 文件夹中的有效照片计算 P+J+R+X 已决定进度，并计入已完成的仅 RAW 结果。

“刷新目录”在上述扫描后，通过 `BeginGpsDirectoryRefresh` 强制失效仅当前 `root\jpg` 的 GPS 显示缓存，并在单个后台 worker 复用 `ReadGpsDisplay` / WIC metadata-only 管线逐张读取；每 16 张通过 Dispatcher 更新。每次刷新及切 root 建立新 generation，旧普通 GPS 任务、旧批次结果和旧排序预读不能回填当前 UI。拍摄时间排序缺失缓存时保留既有后台 preload，不把 GPS 扫描变成 UI 线程逐张解码。

`RefreshDirectoryView` 在文件列表未变时不重建视图；列表改变时保留仍存在的选中路径、当前照片和缩放/滚动，并沿用当前文件不存在时的 fallback。不再无条件调用 `ClearFullImageCache`；只有识别到具体文件 identity 变化时使用现有逐路径失效。其他目录的 GPS 缓存与未改变的图像缓存保留。

刷新来源时后台构建新的 `GpsSourceSession`，沿用现有文件安全和 SHA256 身份绑定，但不运行写入/完整性门禁。监视器可先清除过期坐标，仅保留用户最后明确选过的来源路径作为下一次刷新候选；刷新后有效则采用最新坐标，无效则清空候选。用户手动清除、切 root 或换来源立即使旧候选/旧完成结果失效。候选不保存坐标、不持久化，也不能用于写入。

### 分类

`OrganizeFiles`：

1. 枚举根目录顶层目标扩展名。
2. 用户确认。
3. 创建 `jpg` / `arw`。
4. `File.Move` 到对应目录。
5. 同名存在或异常则跳过，绝不覆盖。
6. 成功移动 JPG/RAW 时同步迁移 P/J/R/X、仅 RAW 保留记录和当前照片状态。

### 取消分类

`UnorganizeFiles`：

1. 枚举 `root\jpg` 与 `root\arw` 顶层照片。
2. 在移动前检查根目录所有同名冲突；存在任一冲突则整体停止。
3. 用户确认后逐个 `File.Move` 回根目录并记录失败项。
4. 仅当 `jpg` / `arw` 完全为空时删除空文件夹。
5. 成功还原 JPG/RAW 时同步迁移挑片决定和当前照片状态。

### 文件对应

`Stem` 取文件名主体；所有集合使用 `StringComparer.OrdinalIgnoreCase`。`InspectCorrespondence` 区分完整配对、仅 JPG、仅 RAW、未记录单格式和状态矛盾。J/R 清理主动形成的单格式是正常终态；检查只报告，不移动文件。

## 图像管线

`LoadOrientedJpeg` 使用 `BitmapImage` + WIC：

- 缩略图按目标宽度解码。
- 单张图以原始大小异步解码。
- `BitmapCacheOption.OnLoad` 释放文件句柄。
- `BitmapSource.Freeze()` 允许跨线程传递。
- EXIF Orientation 负责旋转/镜像。

完整图缓存由字典 + LRU 链表管理，最多 3 张。当前图加载后预载下一张照片。缩略图除 512 项内存 LRU 外，还以“规范化绝对路径 + 文件大小 + 最后写入时间”生成哈希键写入共享持久缓存；源文件变化会自然失效。EXIF 拍摄时间同样按文件时间戳验证并持久化。

## 单张缩放

- `fitSingleImage=true` 时，根据主视图可用宽高计算缩放。
- 手动输入和 +/- 将状态切到固定倍率，范围 10%–600%。
- 点击主图在适应与 100% 间切换。
- 切换照片不重置 `singleZoom`。
- `singleViewer` 的水平/垂直偏移决定导航器视口框。

竖构图的导航器必须分别按图像实际显示宽高计算 X/Y 比例，不得套用横构图假设。

## 状态模型

### 会话设置

`settings.txt` 保存目录、视图、当前照片、适应/缩放、网格尺寸、排序、筛选和最近路径。主题、自动前进启停及“挑片 / 浏览”导航目标均不是全局持久化偏好；程序固定暗黑模式。进入新目录时根据该批照片初始化导航目标：存在未决定照片时为“挑片”，已全部决定时为“浏览”，空目录为“挑片”。同一目录内允许手动切换。

时间排序和照片信息通过 WIC 依次读取 EXIF `DateTimeOriginal`、`DateTimeDigitized` 和 `DateTime`；仅在三个字段均缺失或无效时作为“无拍摄时间”排列到末尾，不回退到文件修改时间。

键盘挑片前进由 `NextUndecidedPhoto` 按完整图库和当前排序计算。在“全部 / 未决定”筛选下，从当前照片之后寻找第一张未决定照片，到末尾后从开头继续；“保留 / 废片”筛选只刷新复查结果。完成判断必须是当前操作令未决定数量从 1 变为 0，而不是仅检查修改后是否为 100%；显示 P/J/R/X 汇总并等待用户确认后，保持当前照片并切换到“浏览”，不触发文件清理。在浏览中取消决定只提示数量，不自动切回挑片。

### 挑片数据

内存中使用：

- `pickedFiles: HashSet<string>`
- `jpgOnlyFiles: HashSet<string>`
- `rawOnlyFiles: HashSet<string>`
- `rejectedFiles: HashSet<string>`
- `retainedRawFiles: HashSet<string>`（R 清理完成后锚定仍存在的 RAW）

退出和每次变更后原子写入带版本头的 `cull-marks-v2.txt`。P/J/R/X 以 JPG 路径为决定锚点，K 以内存中的 `retainedRawFiles` 对应完成后的 RAW。旧 `cull-marks.txt` 只迁移 P/X，避免把历史版本中含义不同的 R 误读为“仅 RAW”。读取失败会阻止保存和清理，防止空状态覆盖可恢复记录。

## 回收站

`MoveToRecycleBin` 使用 Shell32 `SHFileOperation`：

- `FO_DELETE`
- `FOF_ALLOWUNDO`
- `FOF_NOCONFIRMATION`
- `FOF_SILENT`
- `FOF_NOERRORUI`

`BuildCleanupPlan` 只正向枚举当前 `root\jpg` 的 J/R/X，并只用当前 `root\arw` 建立唯一配对。确认后 `ExecuteCleanupPlan` 再校验根目录边界、链接、决定和文件时间戳。P 不动；J 回收 RAW；R 先持久化 RAW 保留意图再回收 JPG；X 先回收 RAW，成功后再回收 JPG。

`RecycleFiles` 返回精确的 `SuccessPaths` 与 `FailedPaths`。成功 JPG 才清除其 JPG 锚定决定，失败项保留以便重试；部分 X 在 RAW 成功、JPG 失败时仍保留 X 锚点。清理后只淘汰成功移除文件的图像缓存，并按清理前排序进入当前照片之后的下一张幸存照片。不要移除 `FOF_ALLOWUNDO`。

## 未来可拆分方向

JPG GPS 性能：ApprovedExifToolRunner 以 adapter session 为单位完整 manifest 验证并持续锁定全部依赖；Run 仅检查成员/identity/锁/环境，异常 fail closed。ExifToolAdapter 把 XML/base64 metadata、strict Validate、ImageDataHash 与 binary 块合并一次读取；per-file baseline 绑定完整 SHA256 和 Windows file identity，锁定后再确认才可复用。临时写后仍完整结构/private/WIC 验证；仅去除同内容重复的内置 WIC 解码。GpsPerformance 阶段计时不改 UI/挑片行为，细节见 GPS_METADATA.md。

JPG GPS 1.5 仍在唯一 `RAWMate.cs`：`GpsMetadata`（DMS/十进制度与 WIC）、`ApprovedExifToolRunner`（冻结完整工具 manifest、依赖锁、13.59、禁用户 config）、`ExifToolAdapter`（严格五字段、XML/ImageDataHash/Validate）、`JpgGpsIntegrity`（真实 JPEG 段、完整解码、typed IFD、HiddenData/MPImage2 动态证明）、`GpsSourceSession`（内存来源与根目录隔离）、`GpsEditPlan`（恰好一个 JPG，不枚举 RAW）、`GpsFileTransaction`（同目录副本/验证/原子替换/恢复）。GPS UX / Grid 新增 `GpsBatchPlan`（逐目标分类并持有单 JPG 子计划）、`GpsBatchExecutor`（顺序安全事务/当前结束后取消/精确汇总）、`GpsUxState`（动作与 badge 状态）。No-GPS-IFD 阶段仅经授权扩展 JpgGpsIntegrity 并传入 Adapter 的计划坐标；六个其他基础安全/事务类不变。ExistingGpsIfdPath 原 MakerNotes 证明主体冻结；CreateGpsIfdPath 使用独立 typed pool、HiddenInfo nested、全字节 map 与公共 EXIF ownership，两个路径共享原 MPF/图像/解码/语义门禁。MainWindow 接设置、直接动作、来源条、网格选择/框选与统一确认/进度/结果。后台验证不触发挑片逻辑，写后目标路径 cache epoch 防止旧 full-image completion 回填，metadata/thumbnail 使用文件 stamp。来源监视器 + identity 轮询失效，退出期间不允许打断活动事务。RAW/XMP 没有 GPS 写入路径。详见 `GPS_METADATA.md`。

只有在加入回归测试后才考虑拆分：

- `PhotoCatalog`：扫描、排序、筛选。
- `PairingService`：配对和缺失报告。
- `RecycleBinService`：清理与结果。
- `ImageLoader`：方向、缓存、预载。
- `SettingsStore` / `CullMarkStore`。
- `MainWindow`：只负责视图和交互。

当前不要仅为代码风格进行大规模迁移；单文件构建和零依赖仍是产品交付优势。
