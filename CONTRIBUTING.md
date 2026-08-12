# Contributing to RAWMate PC

## 开始前

依次阅读：

1. `AGENTS.md`
2. `PROJECT_CONTEXT.md`
3. `README.md`
4. `CHANGELOG.md`
5. 与改动有关的 `docs/`

本仓库只处理 Windows PC 版，其他平台不在当前范围。

## 提交一个变更

1. 用 GitHub Issue 或清晰任务描述说明现状、预期和复现步骤。
2. 保持改动最小，不顺手重构无关代码。
3. 所有文件操作变更都要先检查回收站、配对、不覆盖和失败报告边界。
4. 运行 `BUILD_RAWMate-Test.cmd`。
5. 按 `docs/TESTING.md` 完成相关核验。
6. UI 变更附上暗色/浅色和必要的横构图/竖构图截图。
7. 更新 `CHANGELOG.md`；若改变长期事实，更新 `PROJECT_CONTEXT.md`。
8. 用户明确验收后才构建 `RAWMate.exe`。

## PR 内容

PR 至少要说明：

- 解决什么问题。
- 哪些文件和行为改变。
- 是否涉及移动/回收站/配对。
- 完成了哪些测试。
- 已知限制或未覆盖场景。

不提交编译出的 EXE、本机设置、日志或测试照片。
