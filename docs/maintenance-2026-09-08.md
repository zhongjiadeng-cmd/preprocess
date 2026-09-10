# 项目整理记录：2026-09-08

## 已完成

- 删除 10 个提交已包含在 `main` 中的本地分支，使用 `git branch -d`，没有强制删除或修改远端。
- 移除无未提交改动的 `.worktrees/large-tiff-pillow-limit` 工作副本及其构建、打包产物（约 950 MB），以及空的 `rename-log-title` 残留目录。
- 清理根目录和 Python 测试目录中的 Python 缓存，以及根目录、工作副本容器的 Finder 缓存文件。
- 将根目录 `design-qa.md` 移至 `docs/archive/design-qa-2026-07-28.md`，标注历史状态并调整图片引用。
- 将本地 `design-audit/` 和 `已粘贴 2026-08-20 下午4.29.26.png` 原样移至 `artifacts/design-history/`，保留内容。
- 为 `.workbuddy/` 增加忽略规则，保留本地内容；补充项目与文档入口说明。

## 删除的已合并分支

```text
agent/windows-portable-release-plan
codex/desktop-ui-dual-theme
codex/editable-process-params
codex/large-tiff-pillow-limit
codex/laserpmt-node-workflow
codex/laserpmt-shared-patch-groups
codex/light-theme-warm-neutral-polish
codex/machine-params-edit
codex/pmt-mode-extraction-v2
codex/preview-layout-polish
```

## 保留项目

- 后续经用户确认，已删除 `codex/import-progress-overlay`、`codex/output-path-edit`、`codex/pmt-mode-extraction`。前两个分支的有效改动在主分支存在等价补丁，其余菜单边框提交相互抵消；第三个为被 v2 和节点工作流替代的旧实现。使用 `git branch -D` 删除引用，原提交仍可从备份恢复。目前本地仅保留 `main`，主分支代码未改动。
- Codex 管理的 `/Users/ccc/.codex/worktrees/7841/preprocess` 为 detached HEAD，未纳入本次清理。
- 全部 C# / Python 测试源码仍覆盖当前功能，没有发现足够依据认定为废弃的测试文件。
- 根目录原始 TIFF、加工数据、当前主工作区构建输出、发布包和最新 QA 截图保留。
- 历史设计方案和实施计划保留，以便追溯决策。

## 分支恢复

删除前已备份全部 14 个本地分支，并通过 `git bundle verify` 校验：

- `artifacts/cleanup/2026-09-08/branches-before.bundle`
- `artifacts/cleanup/2026-09-08/branches-before.txt`

如需恢复某个分支，在项目根目录执行以下命令，将示例分支名替换为目标分支：

```bash
git fetch artifacts/cleanup/2026-09-08/branches-before.bundle refs/heads/codex/large-tiff-pillow-limit:refs/heads/codex/large-tiff-pillow-limit
```

该备份仅包含 Git 历史和分支，不包含未跟踪文件或已删除的生成产物。`artifacts/` 不受 Git 跟踪，后续清理该目录时应保留这份备份。

## 验证

- Python：248 项通过。
- C#：386 项通过，1 项跳过，0 项失败。
- `git diff --check` 通过。
- 当前仍在 `main`；没有创建提交或推送。

## 后续目录结构优化

- 桌面应用移至 `src/GrayscaleLayersMac/`，Python 脚本移至 `src/python/`，浏览器预览资源移至 `src/overlay_viewer/`。
- C# 测试移至 `tests/GrayscaleLayersMac.Tests/`，Python 测试移至 `tests/python/`；同步修正源码检查和子进程测试路径。
- 增加 `Preprocess.slnx`、`Makefile`、Python 依赖清单及 `pytest.ini`，提供统一构建、运行、测试与打包入口。
- 同步更新 VS Code、项目引用、macOS 打包脚本、QA 工具和当前使用文档。历史设计计划保留原记录中的路径。
- Python 和浏览器预览源码内容未变；原始素材和加工文件保留原位置。
- 目录迁移后：Python 248 项通过；C# 386 项通过、1 项可选集成测试默认跳过；macOS 应用打包及包结构校验通过；工作流界面 QA 通过。
- 已单独启用并通过真实 Python 加工集成测试，合计验证全部 387 项 C# 测试。
