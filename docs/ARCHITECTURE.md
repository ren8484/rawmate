# RAWMate PC 架构

## 总览

RAWMate 是单进程、单窗口、纯本地 WPF 程序。业务、状态和界面都在 `RAWMate.cs` 中，应用不访问网络。

```text
Program.Main
  └─ MainWindow
      ├─ 目录扫描/分类/配对
      ├─ 网格与缩略图延迟加载
      ├─ 单张图异步加载与 LRU 缓存
      ├─ 旗标/评分和筛选排序
      ├─ EXIF 方向与信息
      ├─ 设置/会话持久化
      └─ Windows 回收站 P/Invoke
```

## 启动与异常

`Program.Main` 创建 WPF `Application`，注册 `DispatcherUnhandledException`，再运行 `MainWindow`。未处理的 UI 异常写入 `%LOCALAPPDATA%\RAWMate\error.log`，用于定位“点击后应用关闭/重启”类问题。

## UI 结构

`BuildUi` 创建两列：

- 固定宽度左侧栏：品牌、主题、目录、统计和操作。
- 自适应右侧：网格或单张主视图，下方照片信息。

UI 全由 `Build*` 方法程序化创建。切换主题或视图时，`RebuildUi` 会重建可视树；这是因为 WPF 控件不能同时成为两个逻辑树的子节点。重建时需保留业务状态，避免重复同步解码全部缩略图。

## 目录数据流

### 扫描

`ScanDirectory`：

1. 读取所选根目录顶层 JPG/JPEG/ARW。
2. 读取 `root\jpg` 和 `root\arw` 顶层。
3. 更新三个计数和目录提示。

### 分类

`OrganizeFiles`：

1. 枚举根目录顶层目标扩展名。
2. 用户确认。
3. 创建 `jpg` / `arw`。
4. `File.Move` 到对应目录。
5. 同名存在或异常则跳过，绝不覆盖。

### 配对

`Stem` 取文件名主体；所有集合使用 `StringComparer.OrdinalIgnoreCase`。`FindMissingPairs` 同时计算：

- `jpgWithoutRaw`
- `rawWithoutJpg`

程序只允许清理第二类。

## 图像管线

`LoadOrientedJpeg` 使用 `BitmapImage` + WIC：

- 缩略图按目标宽度解码。
- 单张图以原始大小异步解码。
- `BitmapCacheOption.OnLoad` 释放文件句柄。
- `BitmapSource.Freeze()` 允许跨线程传递。
- EXIF Orientation 负责旋转/镜像。

完整图缓存由字典 + LRU 链表管理，最多 5 张。当前图加载后预载前后照片，从而减少左右切换延迟。

## 单张缩放

- `fitSingleImage=true` 时，根据主视图可用宽高计算缩放。
- 手动输入和 +/- 将状态切到固定倍率，范围 10%–600%。
- 点击主图在适应与 200% 间切换。
- 切换照片不重置 `singleZoom`。
- `singleViewer` 的水平/垂直偏移决定导航器视口框。

竖构图的导航器必须分别按图像实际显示宽高计算 X/Y 比例，不得套用横构图假设。

## 状态模型

### 会话设置

`settings.txt` 保存主题、目录、视图、当前照片、适应/缩放、网格尺寸、排序、筛选、自动前进开关和最近路径。

自动前进由 `NextPhotoForAutoAdvance` 按当前排序计算；网格视图同时服从当前筛选，单张视图使用完整图库。末张返回空值并停止，不循环回第一张。

### 挑片数据

内存中使用：

- `pickedFiles: HashSet<string>`
- `rejectedFiles: HashSet<string>`
- `starRatings: Dictionary<string,int>`

退出和每次变更后写入 `cull-marks.txt`。

## 回收站

`MoveToRecycleBin` 使用 Shell32 `SHFileOperation`：

- `FO_DELETE`
- `FOF_ALLOWUNDO`
- `FOF_NOCONFIRMATION`
- `FOF_SILENT`
- `FOF_NOERRORUI`

上层必须自己显示确认、统计成功和失败。不要把 `FOF_ALLOWUNDO` 移除。

## 未来可拆分方向

只有在加入回归测试后才考虑拆分：

- `PhotoCatalog`：扫描、排序、筛选。
- `PairingService`：配对和缺失报告。
- `RecycleBinService`：清理与结果。
- `ImageLoader`：方向、缓存、预载。
- `SettingsStore` / `CullMarkStore`。
- `MainWindow`：只负责视图和交互。

当前不要仅为代码风格进行大规模迁移；单文件构建和零依赖仍是产品交付优势。
