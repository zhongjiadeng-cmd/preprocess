# 打包应用脚本路径修复实施计划

1. 在源代码回归测试中断言图片检查使用 `ApplicationLayout.GetScriptPath`，先确认测试失败。
2. 修改 `InspectTextureImageAsync`，统一通过 `ApplicationLayout` 获取 `texture_to_hatch_dxf.py`。
3. 运行目标测试、全部 Python 测试和全部 .NET 测试。
4. 重新构建 macOS 应用包，检查脚本目录，并用用户提供的 TIFF 文件夹执行打包脚本检查。
5. 提交修复，确认 worktree 干净并交付新应用包。
