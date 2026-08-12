# RAWMate PC 发布流程

## 1. 测试候选

1. 确认源码和文档已保存。
2. 运行 `BUILD_RAWMate-Test.cmd`。
3. 按 `docs/TESTING.md` 完成相关测试。
4. 向用户交付固定名称 `RAWMate-Test.exe` 和测试结果。
5. 等待用户明确确认。

不要用带时间戳或随机后缀的测试 EXE，以免 Windows 每次重复询问权限。

## 2. 正式构建

仅在用户确认后：

1. 运行 `BUILD_RAWMate.cmd`。
2. 确认只生成正式 `RAWMate.exe`，图标正确。
3. 计算 SHA-256：

```powershell
Get-FileHash .\RAWMate.exe -Algorithm SHA256
```

4. 再做一次启动和核心流程冒烟测试。
5. 更新 `CHANGELOG.md`，提交源码和文档。

## 3. 桌面快捷方式

- 名称：`RAWMate`
- 目标：正式 `RAWMate.exe`
- 起始位置：EXE 所在目录
- 图标：正式 EXE 内嵌图标
- 不创建“RAWMate 照片工具”等重复名称。

## 4. GitHub

- 源码仓库不提交 EXE。
- 普通 PR 的 CI artifact 是测试构建，不代表正式发布。
- 正式发布使用带版本标签的 GitHub Release，并附：
  - `RAWMate.exe`
  - `RAWMateHeader.png`
  - SHA-256
  - 变更摘要
  - 支持的 Windows/运行要求
  - 已知限制
- 发布后保证 `README.md`、`CHANGELOG.md`、`PROJECT_CONTEXT.md` 与代码一致。

## 5. 回滚

保留上一正式 Git 标签与 Release。出现严重问题时：

1. 停止分发新 EXE。
2. 指向上一正式 Release。
3. 从上一标签重新构建，不从来历不明的本地 EXE 回滚。
4. 新建 Issue 记录复现、影响和修复验证。

