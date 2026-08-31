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
        self.assertIn('"第 4 步：生成 LaserPMT"', SOURCE)
        self.assertIn("RunPipelineAsync(PipelineRunMode.All)", SOURCE)
        self.assertIn('button.Resources["SplitButtonSecondaryButtonSize"] = 40d', THEME_SOURCE)
        self.assertIn("arrow.RenderTransform = new RotateTransform(180)", THEME_SOURCE)

    def test_main_pipeline_exposes_page_level_intermediate_artifact_imports(self):
        self.assertIn('private readonly DropDownButton _pipelineImportButton = new() { Content = "导入"', SOURCE)
        self.assertIn('"选择文件夹…"', SOURCE)
        self.assertIn('"选择文件…"', SOURCE)
        self.assertIn("var headerTools = new Border", SOURCE)
        header = SOURCE.split("var headerTools = new Border", 1)[1].split("var appHeader = new Border", 1)[0]
        self.assertIn("_pipelineImportButton", header)
        self.assertIn("_pipelineClearButton", header)
        self.assertIn("_appearanceButton", header)
        self.assertIn('"分层 TIFF 目录"', SOURCE)
        self.assertIn('"DXF 目录"', SOURCE)

    def test_main_pipeline_keeps_the_original_single_workspace_structure(self):
        self.assertIn('MakeInspectorSection(\n                    "灰度分层"', SOURCE)
        self.assertIn('MakeInspectorSection(\n                    "Hatch 与 DXF"', SOURCE)
        self.assertIn('"Voronoi 分块与边界扩散"', SOURCE)
        self.assertIn('MakeInspectorSection(\n                    "机器加工文件"', SOURCE)
        self.assertIn("var pipelinePreviewPanel = MakeSharedPreviewPanel", SOURCE)
        self.assertIn('var textureTab = new ToggleButton { Content = "纹理" }', SOURCE)
        self.assertIn('var dxfTab = new ToggleButton { Content = "DXF" }', SOURCE)
        self.assertIn('var pmtTab = new ToggleButton { Content = "PMT" }', SOURCE)
        self.assertIn('MakeInspectorSection(\n                    "LaserPMT 参数矩阵"', SOURCE)
        self.assertNotIn("PipelineStepNavigator", SOURCE)
        self.assertNotIn("InspectorCategoryTabs", SOURCE)
        self.assertNotIn('Content = "选择纹理图"', SOURCE)

    def test_texture_inspection_resolves_scripts_for_packaged_app_layout(self):
        method = SOURCE.split(
            "private static async Task<TextureImageInspection> InspectTextureImageAsync(",
            1,
        )[1].split(
            "private static async Task WaitForExitOrKillAsync(",
            1,
        )[0]
        self.assertIn("ApplicationLayout.GetScriptPath(", method)
        self.assertNotIn(
            'Path.Combine(AppContext.BaseDirectory, "texture_to_hatch_dxf.py")',
            method,
        )

    def test_single_step_modes_have_explicit_dependencies(self):
        self.assertIn("PipelineRunMode.GrayscaleOnly", SOURCE)
        self.assertIn("PipelineRunMode.DxfOnly", SOURCE)
        self.assertIn("PipelineRunMode.MachineOnly", SOURCE)
        self.assertIn("PipelineRunMode.LaserPmtOnly", SOURCE)
        self.assertIn("第 2 步需要先在分层 TIFF 输出目录中生成", SOURCE)
        self.assertIn("第 3 步需要先在 DXF 输出目录中生成", SOURCE)
        self.assertIn("第 4 步需要先生成或导入有效的基础加工目录", SOURCE)

    def test_single_step_modes_return_before_following_steps(self):
        self.assertIn("if (mode == PipelineRunMode.GrayscaleOnly)", SOURCE)
        self.assertIn("if (mode == PipelineRunMode.DxfOnly)", SOURCE)
        self.assertIn("if (mode == PipelineRunMode.MachineOnly)", SOURCE)

    def test_all_pipeline_modes_use_the_progress_overlay_cancel_button(self):
        self.assertIn("private readonly ImportProgressOverlay _pipelineRunProgress", SOURCE)
        self.assertIn("cancelRequested: () => _cancellation?.Cancel()", SOURCE)
        self.assertIn('"正在执行第 1 步：灰度分层…"', SOURCE)
        self.assertIn('"正在执行第 2 步：生成 DXF…"', SOURCE)
        self.assertIn('"正在执行第 3 步：生成加工文件…"', SOURCE)
        self.assertIn('"正在执行第 4 步：生成 LaserPMT…"', SOURCE)

    def test_laser_pmt_request_is_written_as_utf8_without_bom(self):
        self.assertIn(
            "new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)",
            SOURCE,
        )


if __name__ == "__main__":
    unittest.main()
