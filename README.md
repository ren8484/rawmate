# RAWMate

RAWMate 是一款本地运行的 Windows JPG + RAW 双格式照片去留筛选器。它用 JPG 做判断，并同步处理同名 ARW：留下的两种格式一起保留，淘汰的两种格式一起进入 Windows 回收站。

RAWMate 只负责决定照片留不留；挑完以后，可以继续处理 RAW，也可以直接使用 JPG。

当前正式版本：`1.3.0`（Windows 文件版本 `1.3.0.0`）。

## 一次挑片怎么走

相机同时保存 JPG 和 ARW 时，可以把整批照片放进同一个目录，然后交给 RAWMate：

1. 选择照片目录，点击“创建分类”。RAWMate 会把 JPG/JPEG 放进 `jpg`，把 ARW 放进 `arw`。
2. 在图库里只看 JPG。按 `P` 留下想要的照片，按 `X` 或 `Delete` 标记废片；程序会自动跳到下一张未决定照片。
3. 全部照片都有决定后会显示“挑片完成”汇总。确认数量无误，再点击“移除所有废片”，废片 JPG 和同名 ARW 会一起进入 Windows 回收站。
4. 清理完成后，`jpg` 和 `arw` 中留下的是同一组照片：一份方便查看和分享，一份保留后期空间。

挑完以后按自己的需要继续：

- 想做后期：把 `arw` 目录导入 Lightroom 或其他 RAW 软件。这里的 Lightroom 只是后续选择之一。
- 想直接分享：从 `jpg` 目录复制照片到手机或社交平台。

如果曾在资源管理器里单独删除过 JPG，可以用“检查配对”找出没有同名 JPG 的 ARW，再决定是否移入回收站。

## 主要功能

- “创建分类”把根目录顶层的 JPG/JPEG 与 ARW 分到 `jpg` / `arw`，“取消分类”可安全还原；两者均不覆盖同名文件。
- 低干扰的网格视图和单张视图。
- 缩略图大小、筛选，以及名称/EXIF 拍摄时间排序。
- 纯粹的三态挑片：未决定、`P` 保留、`X/Delete` 废片；保留与废片互斥，再按一次可取消旗标。
- 在“全部 / 未决定”筛选下，按 `P`、`X/Delete` 后自动跳到下一张未决定照片；已经决定的照片会被跳过，查找到末尾后会从开头继续。
- 重复当前旗标会取消决定并停留原图；“保留 / 废片”筛选只用于复查，不自动跳转。
- 左侧栏显示“已决定 / 总数”进度，以及紧凑的 `P 保留`、`X 废片`结果；未决定数量可由总数减去已决定数直接得出。
- 单张缩放、平移、导航器和底部胶片条。
- 显示日期、机型、ISO、焦段、光圈与快门。
- 检查 JPG/ARW 缺失配对。
- 所有清理进入 Windows 回收站，可恢复。
- 固定暗黑模式；统一深蓝按钮；记忆最近路径、当前视图、照片和缩放状态，并可从路径下拉菜单清除最近路径记录。
- 自动适配显示器工作区，兼容 4K 以及 1920×1080、150% 缩放环境。

## 快捷键

| 按键 | 功能 |
|---|---|
| `P` | 切换保留旗标 |
| `X` / `Delete` | 切换废片旗标，不立即删除 |
| `←` / `→` | 切换照片 |
| `Space` | 切换网格/单张视图 |
| `Ctrl` + 单击 | 扩展多选 |
| `Shift` + 单击 | 范围多选 |

## 安全规则

- 分类只处理所选目录顶层的 `.jpg`、`.jpeg`、`.arw`，不会递归修改其他子目录。
- 同名目标文件不会被覆盖。
- 配对按不区分大小写的文件名主体判断。
- 程序不永久删除照片，清理均进入 Windows 回收站。
- “检查配对”不会自动删除缺少 ARW 的 JPG。

## 运行与构建

正式使用：双击 `RAWMate.exe`。程序不需要 Python，也不需要把图标或界面图片放在 EXE 旁边。

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

界面状态、旗标和错误日志保存在 `%LOCALAPPDATA%\RAWMate`。这些文件不包含照片本身，也不应提交到 GitHub。1.3.0 会继续读取旧数据中的 P/X 旗标；旧 `auto_advance` 设置会被安全忽略，并在下次保存设置时移除。
