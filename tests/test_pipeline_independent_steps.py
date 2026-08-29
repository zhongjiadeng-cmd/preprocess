import unittest
from pathlib import Path


SOURCE = (
    Path(__file__).resolve().parents[1]
    / "GrayscaleLayersMac"
    / "MainWindow.cs"
).read_text(encoding="utf-8")
THEME_SOURCE = (
    Path(__file__).resolve().parents[1]
    / "GrayscaleLayersMac"
    / "UiTheme.cs"
).read_text(encoding="utf-8")


class PipelineIndependentStepsTests(unittest.TestCase):
    def test_main_pipeline_exposes_all_four_entry_points_in_an_upward_split_button(self):
        self.assertIn("private readonly SplitButton _pipelineRunSplitButton", SOURCE)
        self.assertIn('Content = "全部执行"', SOURCE)
        self.assertNotIn('Content = "单步执行 ▾"', SOURCE)
        self.assertIn("Placement = PlacementMode.TopEdgeAlignedLeft", SOURCE)
        self.assertIn('"第 1 步：灰度分层"', SOURCE)
        self.assertIn('"第 2 步：生成 DXF"', SOURCE)
        self.assertIn('"第 3 步：生成加工文件"', SOURCE)
        self.assertIn("RunPipelineAsync(PipelineRunMode.All)", SOURCE)
        self.assertIn('button.Resources["SplitButtonSecondaryButtonSize"] = 40d', THEME_SOURCE)
        self.assertIn("arrow.RenderTransform = new RotateTransform(180)", THEME_SOURCE)

    def test_main_pipeline_exposes_page_level_intermediate_artifact_imports(self):
        subtitle = 'UiTheme.PageSubtitle("先输出灰度分层 TIFF，再逐层生成 DXF，最后打包为机器加工文件。")'
        import_button = "_pipelineImportButton,"
        first_section = 'MakeInspectorSection(\n                    "输入与分层"'
        self.assertIn('private readonly DropDownButton _pipelineImportButton = new() { Content = "导入"', SOURCE)
        self.assertIn('"导入分层 TIFF 文件夹"', SOURCE)
        self.assertIn('"导入 DXF 文件夹"', SOURCE)
        self.assertLess(SOURCE.index(subtitle), SOURCE.index(import_button))
        self.assertLess(SOURCE.index(import_button), SOURCE.index(first_section))
        self.assertIn('"分层 TIFF 目录"', SOURCE)
        self.assertIn('"DXF 目录"', SOURCE)

    def test_single_step_modes_have_explicit_dependencies(self):
        self.assertIn("PipelineRunMode.GrayscaleOnly", SOURCE)
        self.assertIn("PipelineRunMode.DxfOnly", SOURCE)
        self.assertIn("PipelineRunMode.MachineOnly", SOURCE)
        self.assertIn("第 2 步需要先在分层 TIFF 输出目录中生成", SOURCE)
        self.assertIn("第 3 步需要先在 DXF 输出目录中生成", SOURCE)

    def test_single_step_modes_return_before_following_steps(self):
        self.assertIn("if (mode == PipelineRunMode.GrayscaleOnly)\n                    return;", SOURCE)
        self.assertIn("if (mode == PipelineRunMode.DxfOnly)\n                return;", SOURCE)

    def test_all_pipeline_modes_use_the_progress_window_cancel_button(self):
        self.assertIn("var progressWindow = new ProcessingProgressWindow", SOURCE)
        self.assertIn("progressWindow.CancelRequested +=", SOURCE)
        self.assertIn("progressWindow.UpdateMessage(\"正在执行第 1 步：灰度分层…\")", SOURCE)
        self.assertIn("progressWindow.UpdateMessage(\"正在执行第 2 步：生成 DXF…\")", SOURCE)
        self.assertIn("progressWindow.UpdateMessage(\"正在执行第 3 步：生成加工文件…\")", SOURCE)


if __name__ == "__main__":
    unittest.main()
