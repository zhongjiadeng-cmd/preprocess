# 纹理预处理工具

基于 C# / Avalonia 的桌面应用，配合 Python 完成 TIFF 灰度分层、Hatch DXF、机器加工文件和 LaserPMT 参数矩阵生成。默认入口为 PMT 节点工作流。

## 常用入口

- [应用使用与开发说明](src/GrayscaleLayersMac/README.md)
- [PMT 节点工作流](docs/pmt-processing-workflow.md)
- [文档目录](docs/README.md)
- [本次项目整理记录](docs/maintenance-2026-09-08.md)

## 目录约定

| 路径 | 用途 |
| --- | --- |
| `src/GrayscaleLayersMac/` | 桌面应用源码和资源 |
| `tests/GrayscaleLayersMac.Tests/` | C# 自动化回归测试 |
| `src/python/` | Python 处理脚本，构建时复制至应用输出目录 |
| `tests/python/` | Python 自动化回归测试 |
| `scripts/` | macOS 打包和验证脚本 |
| `tools/ProcessingWorkflowQa/` | 工作流界面验证工具 |
| `src/overlay_viewer/` | 浏览器预览资源 |
| `docs/` | 使用说明、设计记录和维护说明 |
| `artifacts/` | 本地发布包、截图、归档和清理备份，不提交 Git |
| `design-evidence/` | 历史设计图片，保留原路径供旧文档引用，不提交 Git |

本地 TIFF 原图和加工文件保留原位置，避免影响已有工作流中的绝对路径。新增临时截图和检查输出请放入 `artifacts/`，避免散落根目录。

## 开发和测试

需要 .NET 10 SDK、Python 3，以及 Python 的 `numpy`、`pillow` 和 `pytest`。在项目根目录运行：

```bash
python3 -m pip install -r requirements-dev.txt
make run       # 启动桌面应用
make test      # Python 与 C# 测试
make build     # 构建解决方案
make package   # 打包并验证 macOS 应用
make qa        # 工作流界面验证
```

也可直接使用 `dotnet build Preprocess.slnx`、`dotnet test Preprocess.slnx` 和 `python3 -m pytest -q`。`pytest.ini` 统一配置测试发现和 Python 导入路径。VS Code 的构建、运行和调试入口已同步更新。

C# 的真实加工集成测试默认跳过；要一并运行，可执行 `PMT_WORKFLOW_PYTHON=/usr/bin/python3 make test`，将路径替换为已安装 Python 依赖的解释器。

命令行脚本示例：`python3 src/python/grayscale_layers.py --help`。处理脚本仍以原文件名随应用发布，macOS 包内路径仍为 `Contents/Resources/scripts/`。

测试源码是回归保障，不按文件日期删除。`bin/`、`obj/`、`__pycache__/` 和 `.pytest_cache/` 是可重新生成的产物；清理前先结束相关构建、测试或运行进程。删除分支前确认已经合并，工作副本中没有未提交或需要保留的忽略文件。
