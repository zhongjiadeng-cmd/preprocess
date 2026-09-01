# DXF 目录默认跟随分层 TIFF 目录

> Worktree: `codex/dxf-dir-follows-layers`
> 关联模块：四步流程检查器「灰度分层 / Hatch 与 DXF」两个输出目录字段。

## 背景

四步流程有两个彼此独立的输出目录字段：

- 「分层 TIFF 目录」：第 1 步灰度分层产出的 `layer_*.tiff`；
- 「DXF 目录」：第 2 步 hatch 产出的 `layer_*.dxf`。

在此之前两者毫无关联，用户每次都要把同一个目录选两遍。实际用法中绝大多数情况下两者就是同一个目录
（第 2 步读的就是第 1 步的产物），重复选择纯属负担。

用户反馈需要：

1. 选定分层 TIFF 目录后，DXF 目录**默认**变成同一个目录；
2. DXF 目录仍然可以单独改成别的目录；
3. 改过之后，再动分层目录时 DXF 目录**不能**被悄悄覆盖回去。

第 3 点是关键：如果只是"每次选分层目录都无脑同步"，那么用户手动指定的 DXF 目录会在下次调整分层目录时丢失。

## 目标

1. 新增纯逻辑类 `PipelineOutputDirectorySync`，承载"是否允许同步"的判定，不依赖 Avalonia，可单测。
2. `MainWindow` 记录"上一次自动同步过去的路径"，据此决定是否继续跟随。
3. 用户显式指定 DXF 目录的三条路径（单独选择、手动输入、导入 DXF）都解除跟随。
4. 测试覆盖：空值、尾部分隔符、大小写、`..` 解析、手动改过不再跟随、空路径永不相等。

## 设计要点

### 判定规则

`PipelineOutputDirectorySync.ShouldFollowLayerDirectory(dxfOutputPath, lastSyncedPath)`：

| DXF 目录当前值 | 上一次同步值 | 是否同步 |
| --- | --- | --- |
| 空 | 任意 | 是 |
| 非空 | 与当前值相等 | 是 |
| 非空 | 与当前值不等 | 否 |
| 非空 | 从未同步（null） | 否 |

路径比较 `PathsEqual` 会规范化后再比： Trim → 去掉结尾分隔符 → `Path.GetFullPath` 解析 `.`/`..` →
`OrdinalIgnoreCase`（macOS/Windows 卷默认不区分大小写）。任一侧为空返回 `false`——空路径没有可比对象。

### 状态

`MainWindow._pipelineDxfAutoSyncedPath` 保存上一次由分层目录自动同步过去的值。它的语义是：

- 非 null → DXF 目录目前是"跟随着的默认值"，可以继续跟随；
- null → DXF 目录是用户（或导入）显式指定的，停止跟随。

`MarkDxfOutputDirectoryExplicit()` 就是把它置 null。

### 触发点

- `PickPipelineLayerDirectoryAsync()`：分层目录「选择目录…」按钮，选完即同步；
- `OnPipelineLayerDirectoryBoxLostFocus()`：分层目录框手动输入，规范化路径后同步；
- `PickPipelineInputAsync()`：选择原始灰度图时，原本"两个目录都填图片所在目录"的逻辑改为
  "填分层目录 + 走同一套同步"，保证两个入口行为一致；
- `OnPipelineDxfDirectoryBoxLostFocus()`：DXF 框失焦时规范化，并与记录值比对，不等则解除跟随
  （用 `PathsEqual` 比较，所以 `/out/` 与 `/out` 不会误判成用户改动）；
- `CommitPipelineDxfImports()`：导入 DXF 视为显式指定，解除跟随。

### 关键不变量

- 同步只写 `_pipelineDxfOutputBox.Text`，不触碰任何已生成的产物目录解析
  （`ResolveLayerOutputDirectory` / `ResolveDxfOutputDirectory` 的 `_layers` / `_dxf` 子目录规则不变）。
- 分层目录为空时不触发同步（避免清空分层目录时把 DXF 目录也清空）。
- 跟随关系不持久化：重启应用后 `_pipelineDxfAutoSyncedPath` 归 null，此时若 DXF 框有值则视为用户指定。

### 不在本次改动范围

- 加工文件输出目录（第 3 步）与 LaserPMT 输出目录（第 4 步）的取值逻辑不变。
- 导入流程中"导入 TIFF 后回填分层目录"的行为不变——它不参与联动，因为它代表已有产物的位置而非新产物的默认位置。

## 验收

1. `dotnet build` 0 警告 0 错误，`dotnet test` 全通过（新增 `PipelineOutputDirectorySyncTests` 8 例）。
2. 空状态选分层目录 → DXF 目录同时被填上同一个路径。
3. 再单独选一次 DXF 目录 → 此后改分层目录，DXF 目录保持不变。
4. 分层目录框手输 `/a/b/` 后失焦 → 框内变成 `/a/b`，DXF 目录同步且未被误判为用户改动。
