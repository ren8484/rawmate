# RAWMate

RAWMate 是一款本地运行的 Windows JPG + Sony ARW 照片挑选工具。它先把相机照片分到 `jpg` 与 `arw` 文件夹，再用 JPG 快速挑片；淘汰照片时，可以把 JPG 和同名 ARW 一起移入 Windows 回收站。

当前正式版本：`1.1.2`（Windows 文件版本 `1.1.2.0`）。

## 主要功能

- “创建分类”把根目录顶层的 JPG/JPEG 与 ARW 分到 `jpg` / `arw`，“取消分类”可安全还原；两者均不覆盖同名文件。
- Lightroom 风格的网格视图和单张视图。
- 缩略图大小、筛选，以及名称/EXIF 拍摄时间排序。
- `P` 保留、`X/Delete` 废片、`0–5` 星级。
- 可开启标记后自动前进，并用 `Caps Lock` 临时切换。
- 左侧栏显示挑片进度、已处理数量，以及保留、废片、未处理和已评分统计。
- 单张缩放、平移、导航器和底部胶片条。
- 显示日期、机型、ISO、焦段、光圈与快门。
- 检查 JPG/ARW 缺失配对。
- 所有清理进入 Windows 回收站，可恢复。
- 固定暗黑模式；统一深蓝按钮；记忆最近路径、当前视图、照片和缩放状态，并可从路径下拉菜单清除最近路径记录。
- 自动适配显示器工作区，兼容 4K 以及 1920×1080、150% 缩放环境。

## 快速使用

1. 双击 `RAWMate.exe`。
2. 点击“选择目录”，选择相机照片所在的主目录。
3. 可点击“刷新目录”重新扫描；点击“创建分类”会在该目录下创建 `jpg` 和 `arw` 并移动对应文件，需要恢复原结构时点击“取消分类”。
4. 在图库中挑片；双击照片或按空格进入单张视图。
5. 按 `X` 或 `Delete` 标记废片，完成后点击“移除所有废片”。
6. 如果曾在资源管理器中只删除 JPG，点击“检查配对”核对并清理无主 ARW。

## 快捷键

| 按键 | 功能 |
|---|---|
| `P` | 切换保留旗标 |
| `X` / `Delete` | 切换废片旗标，不立即删除 |
| `0` | 清除星级 |
| `1`–`5` | 设置星级 |
| `←` / `→` | 切换照片 |
| `Space` | 切换网格/单张视图 |
| `Caps Lock` | 开启/关闭标记后自动前进 |
| `Ctrl` + 单击 | 扩展多选 |
| `Shift` + 单击 | 范围多选 |

## 安全规则

- 分类只处理所选目录顶层的 `.jpg`、`.jpeg`、`.arw`，不会递归修改其他子目录。
- 同名目标文件不会被覆盖。
- 配对按不区分大小写的文件名主体判断。
- 程序不永久删除照片，清理均进入 Windows 回收站。
- “检查配对”不会自动删除缺少 ARW 的 JPG。

## 运行与构建

正式使用：双击 `RAWMate.exe`。程序不需要 Python。

开发验证：

```bat
BUILD_RAWMate-Test.cmd
```

用户确认测试版后再构建正式版：

```bat
BUILD_RAWMate.cmd
```

构建使用 Windows 自带的 64 位 .NET Framework C# 编译器。完整流程见 [docs/TESTING.md](docs/TESTING.md) 与 [docs/RELEASE.md](docs/RELEASE.md)。

推荐把开发仓库克隆到 `C:\rawmate`。两台电脑均使用相同路径，可以减少脚本、快捷方式和交接说明的差异。

## 项目文档

- [AGENTS.md](AGENTS.md)：AI 和协作者必须遵守的规则。
- [PROJECT_CONTEXT.md](PROJECT_CONTEXT.md)：产品、架构、历史决策与边界。
- [CHANGELOG.md](CHANGELOG.md)：版本变更。
- [docs/AI_HANDOFF.md](docs/AI_HANDOFF.md)：交给 Web ChatGPT 的上下文模板。
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)：代码结构和数据流。
- [docs/UI_SPEC.md](docs/UI_SPEC.md)：界面与交互验收标准。
- [docs/TESTING.md](docs/TESTING.md)：测试矩阵。
- [docs/RELEASE.md](docs/RELEASE.md)：正式发布检查表。
- [docs/GITHUB_SETUP.md](docs/GITHUB_SETUP.md)：首次连接 GitHub 的步骤。

## 本机数据

界面状态、旗标和错误日志保存在 `%LOCALAPPDATA%\RAWMate`。这些文件不包含照片本身，也不应提交到 GitHub。
