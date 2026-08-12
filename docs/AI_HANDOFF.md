# Web ChatGPT / AI 交接模板

## 最短用法

把整个 PC 仓库上传或连接到 GitHub，然后把下面这段发给新的 ChatGPT/Codex 会话：

```text
这是 Windows PC 版 RAWMate 项目，其他平台不在范围内。

标准本地仓库路径是 C:\rawmate；旧开发目录仅为历史快照。

请先完整阅读并遵守：
1. AGENTS.md
2. PROJECT_CONTEXT.md
3. README.md
4. CHANGELOG.md
5. 与任务相关的 docs/

RAWMate.cs 是唯一正式源码。先诊断/实现到固定的 RAWMate-Test.exe，按 docs/TESTING.md 验证；在我明确确认之前，不要覆盖 RAWMate.exe、不要创建正式 Release、不要改桌面快捷方式，也不要做永久删除。

本次任务：<填写问题或需求>
复现目录/照片：<填写，若没有则说明>
期望结果：<填写>
允许的操作：<只读诊断 / 修改测试版 / 正式发布>
```

## 提交 Bug 时

同时提供：

- Windows 版本和显示缩放比例。
- 网格/单张、窗口状态；程序固定使用暗黑模式。
- JPG 是横构图还是竖构图，EXIF Orientation（若知道）。
- 操作步骤、预期和实际结果。
- `%LOCALAPPDATA%\RAWMate\error.log` 中相关片段（注意先检查隐私）。
- 截图或短视频。
- 是否涉及文件移动；测试文件是否可从回收站恢复。

## 请求新功能时

说明：

- 它应放在网格、单张、左侧栏还是右键菜单。
- 是否影响配对、回收站或现有快捷键。
- 期望的状态记忆行为。
- 横/竖构图是否有不同要求；界面固定使用暗黑模式。
- 是否接受第三方依赖或仍需单 EXE。

## GitHub 对接建议

1. 将本目录作为独立仓库根目录。
2. 首次提交只包含源码、资产、构建脚本和文档，不提交 EXE。
3. 打开 GitHub Actions，确认 `windows-build.yml` 可生成 `RAWMate-Test` artifact。
4. 后续需求用 Issue，代码变更用 PR，正式版用 Release + tag。
5. Web ChatGPT 每次开始任务时读取仓库文件，不把旧聊天记忆当成唯一事实源。
