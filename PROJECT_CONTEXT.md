# RAWMate PC 长期项目上下文

> 面向 Web 版 ChatGPT、Codex、GitHub Copilot/协作者的项目事实源。它不是面向普通用户的使用说明。任何新会话应先读 `AGENTS.md`，再读本文件。

## 1. 一句话定义

RAWMate PC 是一个本地、离线、无第三方依赖的 Windows WPF 照片挑选工具：把相机同时拍摄的 JPG/JPEG 与 Sony ARW 分类成配对目录，以 JPG 快速挑片，并安全地将废片 JPG 与对应 ARW 一起移入 Windows 回收站。

## 2. 当前范围

- 当前仓库只代表 Windows PC 版。
- 其他平台实现不参与当前开发、测试、版本记录或 GitHub 计划。
- 当前主源码是 `RAWMate.cs`，约 2,500 行，界面和业务逻辑都在这一文件中。
- 当前构建不依赖 Visual Studio 项目、NuGet、网络服务、数据库或云端账号。
- 当前用户主要环境：Windows、Sony ILCE-6700，JPG 色彩空间 sRGB，Dell U2725QM 显示器使用 sRGB 模式。
- 当前正式版本：`1.0.0`；Windows Assembly/FileVersion 为 `1.0.0.0`。
- 当前标准本地仓库路径：`C:\rawmate`。旧开发目录只作历史快照，不再作为修改入口。

## 3. 产品缘起与目标

相机同时保存 JPG 和 ARW 时，直接浏览 ARW 较慢，若只在 JPG 文件夹删除废片，又容易留下无主 ARW。RAWMate 的核心价值是：

1. 让 JPG 成为快速挑片代理。
2. 始终能找到同名 ARW。
3. 所有清理可从 Windows 回收站恢复。
4. 提供接近 Lightroom 的网格/单张浏览效率，但不承担 RAW 显影、编辑和图库数据库管理。

非目标：RAW 解码/显影、色彩编辑、云同步、照片导入备份、永久删除、面部识别、完整 DAM（数字资产管理）。

## 4. 标准目录契约

用户选择一个相机照片主目录，例如：

```text
Moon/
├─ DSC0001.JPG
├─ DSC0001.ARW
├─ DSC0002.JPG
└─ DSC0002.ARW
```

点击“创建分类”后：

```text
Moon/
├─ jpg/
│  ├─ DSC0001.JPG
│  └─ DSC0002.JPG
└─ arw/
   ├─ DSC0001.ARW
   └─ DSC0002.ARW
```

规则：

- 只分类根目录顶层文件，不递归。
- JPG 扩展名支持 `.jpg`、`.jpeg`，ARW 支持 `.arw`，均不区分大小写。
- 配对键是文件名主体且不区分大小写。
- 不覆盖目标中的同名文件；冲突或访问失败会被计入跳过项。
- 点击“取消分类”会把 `jpg` / `arw` 中的照片还原到根目录；如果根目录存在任一同名文件，会在移动前停止，绝不覆盖。
- 浏览源固定为 `jpg`，ARW 配对源固定为 `arw`。

## 5. 核心用户流程

1. 选择照片目录，或从最近 3 个目录中切换。
2. 扫描目录，查看 JPG、RAW 和待分类数量。
3. 点击“创建分类”，把顶层照片移到 `jpg` / `arw`；需要恢复相机原目录结构时使用“取消分类”。
4. 在网格中快速浏览、排序、筛选、单选/多选。
5. 用 `P`、`X/Delete`、`0–5` 进行旗标和星级挑片。
6. 双击或按空格进入单张视图，查看完整图像、胶片条、导航器与 EXIF。
7. 使用“移除所有废片”，把废片 JPG 与同名 ARW 移入 Windows 回收站；或使用右键菜单处理当前照片。
8. 若曾在资源管理器只删除 JPG，使用“检查配对”找出无主 ARW，并在确认后移入回收站。

## 6. 已实现功能基线

### 6.1 目录与分类

- 选择目录和最近 3 个路径。
- 扫描根目录顶层以及 `jpg` / `arw` 子目录。
- 显示 JPG、RAW、待分类数量。
- 创建/取消 JPG/JPEG 与 ARW 分类，不覆盖同名文件。
- 检查两类缺失配对；仅清理无主 ARW。

### 6.2 图库

- JPG 缩略图延迟加载。
- 网格缩略图尺寸：小、中、大、特大。
- 排序：名称升/降序、EXIF 拍摄时间升/降序；无拍摄时间的照片排列在末尾。
- 筛选：全部、保留、废片、已评分、未标记。
- 普通单击单选，`Ctrl` 多选，`Shift` 范围多选。
- 双击进入单张视图；右键菜单可打开原图、旗标、删除 JPG 或删除 JPG+同名 ARW。

### 6.3 单张视图

- 异步加载完整 JPG，缓存最近 5 张并预载前后照片。
- 网格/单张视图切换；左右键切换照片。
- 适应窗口、可输入 10%–600% 缩放、加减按钮；单击在适应与 200% 间切换。
- 切换下一张时保留当前缩放频率。
- 可滚动/拖动浏览放大图。
- 导航器显示当前视口并支持拖动定位。
- 底部胶片条用于连续切换照片。
- 单张主图同样提供右键菜单。

### 6.4 挑片与清理

- `P` 保留，`X/Delete` 废片，`0` 清星，`1`–`5` 评分。
- 可记忆的自动前进模式；开启时 `P`、`X/Delete`、`1`–`5` 标记后跳到当前序列下一张，`Caps Lock` 切换，末张停止且 `0` 不前进。
- 保留与废片互斥；星级可与旗标并存。
- 选中项可连同同名 ARW 移入回收站。
- 所有废片可一次性连同同名 ARW 移入回收站。
- 右键可只删除 JPG，或删除 JPG + 同名 ARW。
- 所有删除都调用 Windows `SHFileOperation` + `FOF_ALLOWUNDO`，不做永久删除。

### 6.5 图像与元数据

- 读取 EXIF Orientation 1–8，在网格、单张、导航器和胶片条中保持一致方向。
- 显示拍摄日期、相机型号、ISO、焦段、光圈和曝光时间。
- 当前 JPG 解码使用 WPF/WIC，并设置 `BitmapCreateOptions.IgnoreColorProfile`；当前工作流假设相机和显示器均为 sRGB。

### 6.6 状态记忆

关闭时保存并在下次恢复：

- 界面固定为暗黑模式，不保存主题状态。
- 最后目录与最近 3 个目录。
- 网格/单张视图。
- 当前照片。
- 单张是否适应、最后缩放比例。
- 网格缩略图大小。
- 排序和筛选。
- P/X 旗标与星级。
- 自动前进开关。

## 7. 持久化位置与格式

程序数据位于：

```text
%LOCALAPPDATA%\RAWMate\
├─ settings.txt
├─ cull-marks.txt
└─ error.log
```

- `settings.txt` 是 `key=value` 文本，记录界面状态。
- `cull-marks.txt` 使用照片绝对路径的 Base64 文本记录 P/X/星级。
- `error.log` 记录未处理的 WPF UI 异常。
- 这些文件是本机状态，不进入 Git。删除它们只会重置 RAWMate 状态，不会删除照片。

注意：程序自身的“创建分类 / 取消分类”会同步迁移 JPG 的挑片标记；用户在程序外移动/改名照片后，旧记录仍可能失效。当前没有面向外部移动的独立数据库迁移机制。

## 8. 界面设计事实

界面经过多轮实际截图调整，长期约束如下：

- Lightroom 式左右结构：左侧固定功能区，右侧大面积图库/单张视图。
- 不使用顶部蓝色 banner；品牌和操作收敛在左侧，固定暗黑模式且不显示主题开关。
- 低层级、扁平、圆角并固定使用暗黑视觉。
- 同类按钮统一蓝色，不做无必要的主/次/危险三层配色。
- 左侧固定区不出现纵向滚动条；右侧图库滚动条采用窄深色迷你样式。
- 网格/单张切换图标为圆角按钮。
- 单张缩放控制位于图像下方且与胶片条留出一致间距。
- 照片信息保持单行、左对齐，以节省垂直空间。
- 目录下拉框在暗色模式必须使用暗色输入区和清晰白字。

更完整的视觉与交互验收点见 `docs/UI_SPEC.md`。

## 9. 技术架构

### 9.1 技术栈

- C# 单文件程序。
- WPF 程序化 UI（没有 XAML 文件）。
- .NET Framework 64 位编译器 `v4.0.30319\csc.exe`。
- `System.Windows.Forms.FolderBrowserDialog` 选择目录。
- WPF/WIC 解码 JPG 与 EXIF。
- Shell32 `SHFileOperation` 将文件移入 Windows 回收站。

### 9.2 源码职责

- `Program.Main`：应用启动、全局异常记录。
- `MainWindow.Build*`：创建整个 UI。
- `ScanDirectory` / `OrganizeFiles` / `CheckMissingPairs`：目录业务。
- `RefreshGallery` / `CreateTile`：网格和缩略图。
- `BuildSingleView` / `LoadSinglePhoto`：单张浏览、缓存、预载。
- `BuildNavigator` / `QueueNavigatorUpdate`：导航器与视口。
- `LoadOrientedJpeg` / `ReadExifOrientation`：方向处理。
- `ShowPhotoInfo`：EXIF 显示。
- `LoadSettings` / `SaveSettings`：会话状态。
- `LoadCullMarks` / `SaveCullMarks`：旗标与评分。
- `Recycle*` / `MoveToRecycleBin`：可恢复清理。

详见 `docs/ARCHITECTURE.md`。

## 10. 构建与发布事实

- `BUILD_RAWMate-Test.cmd` 固定生成 `RAWMate-Test.exe`，用于每次验证。
- `BUILD_RAWMate.cmd` 生成并覆盖正式 `RAWMate.exe`；只有用户验收测试版后才运行。
- EXE 为自包含的 WPF 可执行文件，但仍依赖 Windows/.NET Framework 运行环境。
- `RAWMate.ico` 必须嵌入 EXE；运行目录保留 `RAWMateHeader.png` 可显示界面徽标。
- 正式桌面快捷方式名称固定为 `RAWMate`。
- Git 仓库不跟踪 EXE；GitHub Actions 构建测试产物，正式 EXE应通过 Release 发布。
- GitHub 正式标签使用三段语义版本（如 `v1.0.0`）；Windows 文件属性使用四段版本（如 `1.0.0.0`）。

## 11. 当前已知限制与风险

- 仅浏览 JPG/JPEG；不解码或预览 ARW。
- 当前是单文件架构，便于分发但增加 UI 与业务耦合；在有测试覆盖前不应为了“整洁”贸然大拆分。
- 没有自动化 WPF UI 测试，关键界面必须实际启动确认。
- WIC 色彩配置被忽略，适合当前 sRGB 工作流，但不等同于完整的显示器 ICC 色彩管理。
- `SHFileOperation` 是旧式但兼容性良好的回收站 API；若迁移到新 API，必须验证多文件、取消和失败报告。
- EXIF 元数据因相机/软件写法差异可能缺失，缺失时显示 `—`。
- 大目录依靠延迟缩略图和 5 张完整图缓存；超大图库仍需关注 UI 线程和内存。
- 没有安装器或自动更新；目前通过单一 EXE 和快捷方式交付。

## 12. 历史脉络

- 2026-07-29：先有 Python/Tk 原型，验证分类、JPG 浏览和配对清理流程。
- 随后迁移为原生 WPF 单 EXE，命名为 RAWMate。
- UI 从顶部 banner/大卡片逐步收敛为 Lightroom 风格左右结构。
- 持续加入暗黑模式、最近路径、状态恢复、右键菜单、EXIF、网格/单张视图、缩放、胶片条、旗标/星级、筛选排序、导航器、方向处理和性能缓存。
- 当前 PC 源码快照最后修改时间为 2026-07-31；未来版本应通过 Git 标签和 `CHANGELOG.md` 延续，而不是依赖聊天记录。

## 13. 新 AI 会话的正确开始方式

1. 确认当前任务只针对 Windows PC 版。
2. 读取 `AGENTS.md`、本文件、`README.md`、`CHANGELOG.md`。
3. 运行 `git status` 并检查 `RAWMate.cs`、构建脚本和资产是否完整。
4. 将用户描述映射到本文件的功能基线与安全边界。
5. 若是问题诊断，先复现和定位，不擅自修改；若明确要求修复，再改测试版。
6. 固定生成 `RAWMate-Test.exe` 并按 `docs/TESTING.md` 验证。
7. 等用户确认后才覆盖 `RAWMate.exe` 和创建/更新桌面快捷方式。

可直接复制的 Web ChatGPT 交接文本见 `docs/AI_HANDOFF.md`。
