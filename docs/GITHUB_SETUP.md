# RAWMate PC 首次连接 GitHub

本地目录已经具备 `.gitignore`、`.gitattributes`、Issue/PR 模板和 Windows CI。首次上传前建议先创建私有仓库；在明确许可证之前不要直接公开。

## 1. 检查首个提交范围

```powershell
git status --short --ignored
git diff --no-index -- NUL AGENTS.md
```

首个提交应包含：

- `RAWMate.cs`
- `RAWMate.ico`
- `RAWMateHeader.png`
- 两个构建脚本
- README、项目上下文和 `docs/`
- `.github/`、`.gitignore`、`.gitattributes`

不应包含：

- `RAWMate.exe` / `RAWMate-Test.exe`
- Python 原型和旧启动脚本
- 本机设置、日志、测试照片

## 2. 创建首个本地提交

```powershell
git add .
git status --short
git commit -m "docs: establish RAWMate PC project context"
```

在 `git add` 后务必再次检查列表，确认没有原片或个人路径数据。

## 3. 连接远程仓库

使用 GitHub 网页创建一个空的私有仓库（不要自动生成 README），然后：

```powershell
git remote add origin https://github.com/<你的账号>/RAWMate.git
git push -u origin main
```

若使用 GitHub CLI，也可在确认提交内容后执行：

```powershell
gh repo create RAWMate --private --source . --remote origin --push
```

这些命令会对外创建/推送仓库，应由用户明确授权后执行。

## 两台电脑的标准路径

家里电脑和公司备用个人电脑都统一使用：

```text
C:\rawmate
```

公司电脑首次使用时，在管理员或有权写入 `C:\` 的 PowerShell 中执行：

```powershell
git clone https://github.com/ren8484/rawmate.git C:\rawmate
cd C:\rawmate
.\BUILD_RAWMate-Test.cmd
```

如果 `C:\rawmate` 已存在，先检查其中是否有需要保留的文件；不要直接克隆覆盖非空目录。

测试版确认能够启动后，在公司电脑本地生成正式 EXE 并创建快捷方式：

```powershell
.\BUILD_RAWMate.cmd

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'RAWMate.lnk'))
$shortcut.TargetPath = 'C:\rawmate\RAWMate.exe'
$shortcut.WorkingDirectory = 'C:\rawmate'
$shortcut.IconLocation = 'C:\rawmate\RAWMate.exe,0'
$shortcut.Description = 'RAWMate'
$shortcut.Save()
```

## 4. 首次远程核验

1. Actions 中 `Windows test build` 成功。
2. Workflow artifact 只包含 `RAWMate-Test.exe`；界面徽标已经嵌入 EXE，不需要旁置 `RAWMateHeader.png`。
3. Issue 页面可选择 Bug/Feature 模板。
4. 新建 PR 时自动出现测试和安全清单。
5. 仓库文件列表中没有 EXE、日志、照片或本机状态文件。

## 5. 与 Web ChatGPT 协作

连接 GitHub 后，让 Web ChatGPT 先阅读 `AGENTS.md` 和 `PROJECT_CONTEXT.md`。每次任务使用 `docs/AI_HANDOFF.md` 模板，明确本轮是只读诊断、测试版修改还是正式发布。
