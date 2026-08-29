using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace GrayscaleLayersMac;

public sealed class MainWindow : Window
{
    private enum PipelineRunMode
    {
        All,
        GrayscaleOnly,
        DxfOnly,
        MachineOnly
    }

    private const int InspectionJsonOverheadCharacters = 4 * 1024;
    private const int MaximumInspectionStandardErrorCharacters = 1024 * 1024;
    private static readonly int MaximumInspectionStandardOutputCharacters = checked(
        TextureImageInspection.GetMaximumBase64CharacterCount(
            TextureImageInspection.DefaultMaximumPreviewBytes) + InspectionJsonOverheadCharacters);

    private sealed record SharedPreviewView(
        ToggleButton TextureTab,
        ToggleButton DxfTab,
        Control TextureContent,
        Control DxfContent,
        SharedPreviewSelection Selection,
        Action UpdateDxfOverlayControls);

    private sealed record WorkspaceColumns(
        ColumnDefinition Preview,
        ColumnDefinition Inspector);

    // 日志面板折叠状态的存储 key：三个面板各自记住自己的状态。
    private const string LayerLogKey = "layer";
    private const string HatchLogKey = "hatch";
    private const string PipelineLogKey = "pipeline";

    private readonly WorkspaceSplitSettings _workspaceSplitSettings =
        WorkspaceSplitSettings.CreateDefault();
    private readonly List<WorkspaceColumns> _workspaceColumns = [];
    private double _workspacePreviewRatio = WorkspaceSplitSettings.DefaultPreviewRatio;

    private readonly TextBox _inputBox = new() { Watermark = "请选择一张灰度纹理图", IsReadOnly = true };
    private readonly TextBox _outputBox = new() { Watermark = "请选择结果保存目录", IsReadOnly = true };
    private readonly NumericUpDown _layersBox = new()
    {
        Minimum = 1, Maximum = 255, Value = 10, Increment = 1,
        ShowButtonSpinner = false,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly NumericUpDown _minLevelBox = MakeNumberBox(0, 1, 254, 0, showButtons: false);
    private readonly NumericUpDown _maxLevelBox = MakeNumberBox(255, 1, 255, 0, showButtons: false);
    private readonly CheckBox _belowIsWhite = new()
    {
        Content = "低于阈值的区域设为白色（默认设为黑色）"
    };
    private readonly TextBox _logBox = UiTheme.CreateLogBox(190);
    private readonly Button _runButton = new() { Content = "开始处理", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _openOutputButton = new() { Content = "打开输出目录", IsEnabled = false };
    private readonly ProgressBar _progress = UiTheme.CreateProgress();
    private readonly TextBox _hatchInputBox = new() { Watermark = "请选择一张黑白纹理图", IsReadOnly = true };
    private readonly TextBox _hatchOutputBox = new() { Watermark = "可输入文件名或完整路径" };
    private readonly NumericUpDown _widthBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _heightBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _spacingBox = MakeNumberBox(0.02m, 0.001m, 1000);
    private readonly TextBox _dpiBox = new() { Watermark = "可选；图片无 DPI 时填写" };
    private readonly GrayscaleLayerPreviewControl _hatchTextureSurface = new();
    private readonly ComboBox _anchorBox = new()
    {
        ItemsSource = new[] { "居中裁剪", "左上角裁剪" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly CheckBox _includeBorder = new() { Content = "在 DXF 中写入加工区域边框" };
    private readonly CheckBox _bidirectionalHatch = new() { Content = "往返填充" };
    private readonly NumericUpDown _blocksBox = MakeNumberBox(9, 1, 100, 0);
    private readonly NumericUpDown _minBlockPercentBox = MakeNumberBox(5, 0.5m, 100, 1);
    private readonly NumericUpDown _maxBlockPercentBox = MakeNumberBox(18, 0.5m, 100, 1);
    private readonly NumericUpDown _boundaryBlurBox = MakeNumberBox(3, 0.1m, 100);
    private readonly NumericUpDown _boundaryCorrelationBox = MakeNumberBox(1, 0.1m, 100);
    private readonly NumericUpDown _voronoiSeedBox = MakeNumberBox(12345, 1, int.MaxValue, 0);
    private readonly DxfPreviewControl _hatchDxfPreview = new();
    private readonly TextBlock _hatchDxfPreviewStatus = new() { Foreground = UiTheme.TextSecondaryBrush };
    private readonly TextBox _hatchLogBox = MakeLogBox();
    private readonly Button _hatchRunButton = new() { Content = "生成 DXF", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _hatchOpenButton = new() { Content = "打开输出位置", IsEnabled = false };
    private readonly ProgressBar _hatchProgress = UiTheme.CreateProgress();
    private readonly TextBox _pipelineInputBox = new() { Watermark = "请选择一张灰度纹理图", IsReadOnly = true };
    private readonly TextBox _pipelineLayerOutputBox = new() { Watermark = "请选择分层 TIFF 保存目录", IsReadOnly = true };
    private readonly TextBox _pipelineDxfOutputBox = new() { Watermark = "请选择 DXF 保存目录", IsReadOnly = true };
    private readonly NumericUpDown _pipelineLayersBox = MakeNumberBox(10, 1, 255, 0, showButtons: false);
    private readonly NumericUpDown _pipelineMinLevelBox = MakeNumberBox(0, 1, 254, 0, showButtons: false);
    private readonly NumericUpDown _pipelineMaxLevelBox = MakeNumberBox(255, 1, 255, 0, showButtons: false);
    private readonly CheckBox _pipelineBelowIsWhite = new() { Content = "低于阈值的区域设为白色（默认设为黑色）" };
    private readonly NumericUpDown _pipelineWidthBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _pipelineHeightBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _pipelineSpacingBox = MakeNumberBox(0.02m, 0.001m, 1000);
    private readonly NumericUpDown _pipelineHatchAngleStepBox = MakeNumberBox(0, 0.1m, 180, 2, showButtons: false);
    private readonly TextBox _pipelineDpiBox = new() { Watermark = "可选；图片无 DPI 时填写" };
    // 三步流程页的纹理界面：第 0 层是导入的源纹理，之后是各灰度分层。
    private readonly GrayscaleLayerPreviewControl _pipelineTextureSurface =
        new(InspectTextureImageAsync);
    private readonly ComboBox _pipelineAnchorBox = new()
    {
        ItemsSource = new[] { "居中裁剪", "左上角裁剪" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly CheckBox _pipelineIncludeBorder = new() { Content = "在 DXF 中写入加工区域边框" };
    private readonly CheckBox _pipelineBidirectionalHatch = new() { Content = "往返填充" };
    private readonly NumericUpDown _pipelineBlocksBox = MakeNumberBox(9, 1, 100, 0);
    private readonly NumericUpDown _pipelineMinBlockPercentBox = MakeNumberBox(5, 0.5m, 100, 1);
    private readonly NumericUpDown _pipelineMaxBlockPercentBox = MakeNumberBox(18, 0.5m, 100, 1);
    private readonly NumericUpDown _pipelineBoundaryBlurBox = MakeNumberBox(3, 0.1m, 100);
    private readonly NumericUpDown _pipelineBoundaryCorrelationBox = MakeNumberBox(1, 0.1m, 100);
    private readonly NumericUpDown _pipelineVoronoiSeedBox = MakeNumberBox(12345, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineLayerStepBox =
        MakeNumberBox(3, 1, 100000, 0, minimum: 1);
    private readonly CheckBox _pipelineBlockCenterMotionBox = new()
    {
        Content = "按加工块中心移动 XY",
        IsChecked = true
    };
    private readonly TextBox _pipelineMachineNameBox = new()
    {
        Watermark = "留空则自动生成 machine_file_时间戳"
    };
    private readonly NumericUpDown _pipelinePowerBox = MakeNumberBox(38, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineFrequencyBox = MakeNumberBox(350, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelinePulseWidthIdxBox = MakeNumberBox(3, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineScanSpeedBox = MakeNumberBox(2100, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineJumpVelocityBox = MakeNumberBox(6000, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineJumpDelayBox = MakeNumberBox(50, 1, int.MaxValue, 0);
    private readonly CheckBox _pipelineScanAheadBox = new()
    {
        Content = "预扫描（scan_ahead）",
        IsChecked = true
    };
    private readonly NumericUpDown _pipelineAccScaleBox = MakeNumberBox(50, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineCornerScaleBox = MakeNumberBox(100, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineEndScaleBox = MakeNumberBox(100, 1, int.MaxValue, 0);
    private readonly CheckBox _pipelineSkyWritingBox = new()
    {
        Content = "空写（sky_writing）",
        IsChecked = true
    };
    private readonly NumericUpDown _pipelineTimeLagBox = MakeNumberBox(100, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineLaserOnShiftBox = MakeNumberBox(18, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineDelayLaserOffBox = MakeNumberBox(32, 1, int.MaxValue, 0);
    private readonly NumericUpDown _pipelineDelayLaserOnBox = MakeNumberBox(0, 1, int.MaxValue, 0);
    private readonly DxfPreviewControl _pipelineDxfPreview = new(startInTopView: true);
    private readonly TextBlock _pipelineDxfPreviewStatus = new() { Foreground = UiTheme.TextSecondaryBrush };
    private readonly ObservableCollection<DxfLayerPreviewItem> _pipelineDxfFiles = [];
    private readonly ComboBox _pipelineDxfSelector = new()
    {
        MinWidth = 240,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        PlaceholderText = "生成后选择要预览的层"
    };
    private readonly TextBox _pipelineLogBox = MakeLogBox();
    // 全部执行按钮：执行整个三步流程（默认最高优先级操作）。
    private readonly Button _pipelineRunButton = new() { Content = "全部执行", HorizontalAlignment = HorizontalAlignment.Stretch };
    // 单步可选框：仅展示可选的步骤，列表项与 PipelineRunMode 的单步枚举一一对应。
    // 选中后立即执行对应步骤，然后清空选择让 placeholder 重新出现，方便下一次再选。
    // ComboBox 的弹出方向（上拉/下拉）由 ApplyUpwardPlacementAfterTemplate 统一把 PART_Popup 设成 TopEdgeAlignedLeft。
    private static readonly (string Label, PipelineRunMode Mode)[] PipelineStepOptions =
    {
        ("第 1 步：灰度分层", PipelineRunMode.GrayscaleOnly),
        ("第 2 步：生成 DXF", PipelineRunMode.DxfOnly),
        ("第 3 步：生成加工文件", PipelineRunMode.MachineOnly),
    };
    private readonly ComboBox _pipelineStepSelector = new()
    {
        ItemsSource = PipelineStepOptions.Select(o => o.Label).ToArray(),
        SelectedIndex = -1,
        PlaceholderText = "单步执行…",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center,
        MinWidth = 200,
    };
    private readonly Button _pipelineOpenButton = new() { Content = "打开加工文件目录", IsEnabled = false };
    private readonly ProgressBar _pipelineProgress = UiTheme.CreateProgress();
    private readonly TexturePreviewController _hatchPreviewController;
    private readonly TexturePreviewController _pipelinePreviewController;
    private readonly SharedPreviewView _hatchSharedPreview;
    private readonly SharedPreviewView _pipelineSharedPreview;
    private string? _lastMachineOutputPath;
    private CancellationTokenSource? _cancellation;

    private void ConfigurePipelineDxfSelector()
    {
        _pipelineDxfSelector.ItemsSource = _pipelineDxfFiles;
        _pipelineDxfSelector.SelectionChanged += (_, _) =>
        {
            if (_pipelineDxfSelector.SelectedItem is DxfLayerPreviewItem item)
                LoadPipelineLayerPreview(item);
        };
    }

    private bool LoadPipelineLayerPreview(DxfLayerPreviewItem item)
    {
        _pipelineDxfPreview.ClearTexture();
        _pipelineSharedPreview.UpdateDxfOverlayControls();
        if (!LoadDxfPreview(
                _pipelineDxfPreview,
                _pipelineDxfPreviewStatus,
                item.DxfPath))
        {
            _pipelineSharedPreview.UpdateDxfOverlayControls();
            return false;
        }

        if (item.HasTexture)
        {
            try
            {
                _pipelineDxfPreview.LoadTexture(
                    item.TexturePath!, item.TextureRegistration!);
            }
            catch (Exception error)
            {
                _pipelineDxfPreview.ClearTexture();
                _pipelineDxfPreviewStatus.Text = $"无法加载配准纹理：{error.Message}";
                _pipelineDxfPreviewStatus.Foreground = Brushes.OrangeRed;
                _pipelineSharedPreview.UpdateDxfOverlayControls();
                return false;
            }
        }

        _pipelineSharedPreview.UpdateDxfOverlayControls();
        _pipelineSharedPreview.Selection.CompleteDxfLoad();
        SelectSharedPreview(_pipelineSharedPreview, SharedPreviewKind.Dxf);
        return true;
    }

    public MainWindow()
    {
        Title = "纹理预处理工具";
        Icon = new WindowIcon(
            AssetLoader.Open(
                new Uri("avares://GrayscaleLayersMac/Assets/AppIcon.png")));
        Width = 1440;
        Height = 940;
        MinWidth = 1080;
        MinHeight = 720;
        Background = UiTheme.RootBrush;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _hatchPreviewController = new TexturePreviewController(
            source => _hatchTextureSurface.SetSourceTexture(source as TexturePreviewPayload),
            update => ApplyTextureSizeUpdate(update, _widthBox, _heightBox));
        _pipelinePreviewController = new TexturePreviewController(
            source => _pipelineTextureSurface.SetSourceTexture(source as TexturePreviewPayload),
            update => ApplyTextureSizeUpdate(
                update,
                _pipelineWidthBox,
                _pipelineHeightBox));
        Styles.Add(UiTheme.CreateGlobalStyles());
        UiTheme.ApplyFluentResourceOverrides(this);
        _workspacePreviewRatio = _workspaceSplitSettings.LoadPreviewRatio();
        // 三步流程页的图层缩略图侧栏恢复上次收起状态。
        // 先恢复、再订阅，恢复动作本身不会触发一次多余的写入。
        _pipelineTextureSurface.SetThumbnailsCollapsed(
            _workspaceSplitSettings.LoadThumbnailCollapsed());
        _pipelineTextureSurface.ThumbnailsCollapsedChanged += (_, _) =>
            _workspaceSplitSettings.TrySaveThumbnailCollapsed(
                _pipelineTextureSurface.IsThumbnailsCollapsed);
        foreach (var primaryButton in new[] { _pipelineRunButton, _hatchRunButton, _runButton })
            UiTheme.ApplyPrimaryStyle(primaryButton);
        ConfigurePipelineDxfSelector();
        _dpiBox.TextChanged += (_, _) =>
        {
            _hatchPreviewController.ApplyFallbackDpiEdit(
                _dpiBox.Text,
                _widthBox.Minimum,
                _widthBox.Maximum);
            RenderTexturePreview(_hatchTextureSurface, _hatchPreviewController.State);
        };
        _pipelineDpiBox.TextChanged += (_, _) =>
        {
            _pipelinePreviewController.ApplyFallbackDpiEdit(
                _pipelineDpiBox.Text,
                _pipelineWidthBox.Minimum,
                _pipelineWidthBox.Maximum);
            RenderTexturePreview(_pipelineTextureSurface, _pipelinePreviewController.State);
        };
        Closed += (_, _) => DisposeTexturePreviews();

        var inputButton = new Button { Content = "选择图片…" };
        var outputButton = new Button { Content = "选择目录…" };
        var cancelButton = new Button { Content = "取消", IsEnabled = false };

        inputButton.Click += async (_, _) => await PickInputAsync();
        outputButton.Click += async (_, _) => await PickOutputAsync();
        _runButton.Click += async (_, _) =>
        {
            if (_cancellation is null)
            {
                cancelButton.IsEnabled = true;
                await RunAsync();
                cancelButton.IsEnabled = false;
            }
        };
        cancelButton.Click += (_, _) => _cancellation?.Cancel();
        _openOutputButton.Click += (_, _) => OpenOutputDirectory();
        LinkGrayLevelBounds(_minLevelBox, _maxLevelBox);
        LinkGrayLevelBounds(_pipelineMinLevelBox, _pipelineMaxLevelBox);

        var layerContent = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                UiTheme.PageTitle("灰度图分层"),
                UiTheme.PageSubtitle("将灰度纹理图按累计阈值生成多张黑白 TIFF 图像；可限定灰阶上下限，只让区间内的灰阶参与分层。"),
                MakeInspectorSection(
                    "输入与参数",
                    MakeField("输入图片", _inputBox, inputButton),
                    MakeField("输出目录", _outputBox, outputButton),
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*"),
                        ColumnSpacing = 12,
                        Children =
                        {
                            MakeLabeledControl("灰阶下限（0–254）", _minLevelBox, 0),
                            MakeLabeledControl("灰阶上限（1–255）", _maxLevelBox, 1),
                            MakeLabeledControl("分层数量（1–255）", _layersBox, 2)
                        }
                    },
                    new StackPanel { Children = { _belowIsWhite } }),
                _progress,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        Place(_runButton, 0),
                        Place(cancelButton, 1),
                        Place(_openOutputButton, 2)
                    }
                },
                PersistLogCollapse(UiTheme.LogPanel(_logBox, "运行日志"), LayerLogKey).Root
            }
        };

        var hatchInputButton = new Button { Content = "选择图片…" };
        var hatchOutputButton = new Button { Content = "保存为…" };
        var hatchCancelButton = new Button { Content = "取消", IsEnabled = false };
        var hatchImportDxfButton = new Button { Content = "导入 DXF…" };
        hatchInputButton.Click += async (_, _) => await PickHatchInputAsync();
        hatchOutputButton.Click += async (_, _) => await PickHatchOutputAsync();
        _hatchRunButton.Click += async (_, _) =>
        {
            if (_cancellation is null)
            {
                hatchCancelButton.IsEnabled = true;
                await RunHatchAsync();
                hatchCancelButton.IsEnabled = false;
            }
        };
        hatchCancelButton.Click += (_, _) => _cancellation?.Cancel();
        _hatchOpenButton.Click += (_, _) => OpenHatchOutput();
        hatchImportDxfButton.Click += async (_, _) =>
            await ImportDxfPreviewAsync(
                _hatchDxfPreview,
                _hatchDxfPreviewStatus,
                addToPipelineSelector: false,
                _hatchSharedPreview!);

        var hatchInspector = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                UiTheme.PageTitle("Texture to Hatch"),
                UiTheme.PageSubtitle("自动识别最小重复单元，只排列完整单元，再把黑色区域转换为 DXF 水平阴影线。"),
                MakeInspectorSection(
                    "输入输出",
                    MakeField("输入纹理图", _hatchInputBox, hatchInputButton),
                    MakeField("输出 DXF", _hatchOutputBox, hatchOutputButton)),
                MakeInspectorSection(
                    "Hatch 参数",
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("目标宽度（mm）", _widthBox, 0),
                            MakeLabeledControl("目标高度（mm）", _heightBox, 1),
                            MakeLabeledControl("阴影线间距（mm）", _spacingBox, 2)
                        }
                    },
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("设置 DPI", _dpiBox, 0),
                            MakeLabeledControl("单元阵列对齐", _anchorBox, 1)
                        }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 20,
                        Children = { _includeBorder, _bidirectionalHatch }
                    }),
                MakeVoronoiPanel(
                    _blocksBox,
                    _minBlockPercentBox,
                    _maxBlockPercentBox,
                    _boundaryBlurBox,
                    _boundaryCorrelationBox,
                    _voronoiSeedBox),
                _hatchProgress,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        Place(_hatchRunButton, 0),
                        Place(hatchCancelButton, 1),
                        Place(_hatchOpenButton, 2)
                    }
                }
            }
        };
        var hatchPreviewPanel = MakeSharedPreviewPanel(
            _hatchTextureSurface,
            _hatchDxfPreview,
            _hatchDxfPreviewStatus,
            hatchImportDxfButton,
            fileSelector: null,
            enableLayerOverlay: false,
            out _hatchSharedPreview);
        var hatchContent = MakeWorkspace(
            hatchInspector,
            hatchPreviewPanel,
            _hatchLogBox,
            "运行日志",
            HatchLogKey);

        var pipelineInputButton = new Button { Content = "选择图片…" };
        var pipelineLayerOutputButton = new Button { Content = "选择目录…" };
        var pipelineDxfOutputButton = new Button { Content = "选择目录…" };
        var pipelineCancelButton = new Button { Content = "取消", IsEnabled = false };
        var pipelineImportDxfButton = new Button { Content = "导入 DXF…" };
        pipelineInputButton.Click += async (_, _) => await PickPipelineInputAsync();
        pipelineLayerOutputButton.Click += async (_, _) =>
            await PickPipelineFolderAsync(_pipelineLayerOutputBox, "选择分层 TIFF 保存目录");
        pipelineDxfOutputButton.Click += async (_, _) =>
            await PickPipelineFolderAsync(_pipelineDxfOutputBox, "选择 DXF 保存目录");
        _pipelineRunButton.Click += async (_, _) =>
        {
            if (_cancellation is null)
            {
                pipelineCancelButton.IsEnabled = true;
                await RunPipelineAsync(PipelineRunMode.All);
                pipelineCancelButton.IsEnabled = false;
            }
        };
        // 把单步执行的 Flyout 替换为 ComboBox 自身的事件：选中即触发，结束后清空选择。
        // TemplateApplied 时把 PART_Popup 改成上拉（TopEdgeAlignedLeft），避免下拉打开时被窗口底部裁掉。
        ConfigurePipelineStepSelector(pipelineCancelButton);
        pipelineCancelButton.Click += (_, _) => _cancellation?.Cancel();
        _pipelineOpenButton.Click += (_, _) => OpenDirectory(_lastMachineOutputPath);
        pipelineImportDxfButton.Click += async (_, _) =>
            await ImportDxfPreviewAsync(
                _pipelineDxfPreview,
                _pipelineDxfPreviewStatus,
                addToPipelineSelector: true,
                _pipelineSharedPreview!);
        _pipelineBlocksBox.ValueChanged += (_, _) => UpdateBlockCenterMotionAvailability();
        UpdateBlockCenterMotionAvailability();

        var pipelineInspector = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                UiTheme.PageTitle("灰度分层 → Hatch DXF → 加工文件"),
                UiTheme.PageSubtitle("先输出灰度分层 TIFF，再逐层生成 DXF，最后打包为机器加工文件。"),
                MakeInspectorSection(
                    "输入与分层",
                    MakeField("原始灰度图", _pipelineInputBox, pipelineInputButton),
                    MakeField("分层 TIFF 输出目录", _pipelineLayerOutputBox, pipelineLayerOutputButton),
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 12,
                        Children =
                        {
                            MakeLabeledControl("灰阶下限（0–254）", _pipelineMinLevelBox, 0),
                            MakeLabeledControl("灰阶上限（1–255）", _pipelineMaxLevelBox, 1),
                            MakeLabeledControl("分层数量（1–255）", _pipelineLayersBox, 2)
                        }
                    },
                    new StackPanel { Children = { _pipelineBelowIsWhite } }),
                MakeInspectorSection(
                    "Hatch 与 DXF",
                    MakeField("DXF 输出目录", _pipelineDxfOutputBox, pipelineDxfOutputButton),
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("目标宽度（mm）", _pipelineWidthBox, 0),
                            MakeLabeledControl("目标高度（mm）", _pipelineHeightBox, 1),
                            MakeLabeledControl("阴影线间距（mm）", _pipelineSpacingBox, 2)
                        }
                    },
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("设置 DPI", _pipelineDpiBox, 0),
                            MakeLabeledControl("单元阵列对齐", _pipelineAnchorBox, 1),
                            MakeLabeledControl("层间角度递进（°）", _pipelineHatchAngleStepBox, 2)
                        }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 20,
                        Children = { _pipelineIncludeBorder, _pipelineBidirectionalHatch }
                    }),
                MakeVoronoiPanel(
                    _pipelineBlocksBox,
                    _pipelineMinBlockPercentBox,
                    _pipelineMaxBlockPercentBox,
                    _pipelineBoundaryBlurBox,
                    _pipelineBoundaryCorrelationBox,
                    _pipelineVoronoiSeedBox),
                MakeInspectorSection(
                    "机器加工文件",
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("每层下降深度（μm）", _pipelineLayerStepBox, 0),
                            MakeLabeledControl("加工文件名", _pipelineMachineNameBox, 1)
                        }
                    },
                    _pipelineBlockCenterMotionBox,
                    new Expander
                    {
                        Header = new TextBlock
                        {
                            Text = "第一组激光参数",
                            FontSize = 12.5,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = UiTheme.TextSecondaryBrush
                        },
                        IsExpanded = true,
                        Background = Brushes.Transparent,
                        Content = new StackPanel
                        {
                            Margin = new Thickness(0, 10, 0, 0),
                            Spacing = 12,
                            Children =
                            {
                                new Grid
                                {
                                    ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                                    ColumnSpacing = 12,
                                    Children =
                                    {
                                        MakeLabeledControl("功率（power）", _pipelinePowerBox, 0),
                                        MakeLabeledControl("频率（frequency）", _pipelineFrequencyBox, 1),
                                        MakeLabeledControl("脉宽索引（pulseWidthIdx）", _pipelinePulseWidthIdxBox, 2)
                                    }
                                },
                                new Grid
                                {
                                    ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                                    ColumnSpacing = 12,
                                    Children =
                                    {
                                        MakeLabeledControl("扫描速度（scanSpeed）", _pipelineScanSpeedBox, 0),
                                        MakeLabeledControl("跳转速度（jump_vel）", _pipelineJumpVelocityBox, 1),
                                        MakeLabeledControl("跳转延迟（jump_delay）", _pipelineJumpDelayBox, 2)
                                    }
                                },
                                new Grid
                                {
                                    ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                                    ColumnSpacing = 12,
                                    Children =
                                    {
                                        MakeLabeledControl("加速度比例（accScale）", _pipelineAccScaleBox, 0),
                                        MakeLabeledControl("转角比例（cornerScale）", _pipelineCornerScaleBox, 1),
                                        MakeLabeledControl("结束比例（endScale）", _pipelineEndScaleBox, 2)
                                    }
                                },
                                new Grid
                                {
                                    ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                                    ColumnSpacing = 12,
                                    Children =
                                    {
                                        MakeLabeledControl("时间滞后（timeLag）", _pipelineTimeLagBox, 0),
                                        MakeLabeledControl("开光偏移（laserOnShift）", _pipelineLaserOnShiftBox, 1),
                                        MakeLabeledControl("关光延迟（delaseroff）", _pipelineDelayLaserOffBox, 2)
                                    }
                                },
                                new Grid
                                {
                                    ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                                    ColumnSpacing = 12,
                                    Children =
                                    {
                                        MakeLabeledControl("开光延迟（delaseron）", _pipelineDelayLaserOnBox, 0)
                                    }
                                },
                                new StackPanel
                                {
                                    Orientation = Orientation.Horizontal,
                                    Spacing = 20,
                                    Children = { _pipelineScanAheadBox, _pipelineSkyWritingBox }
                                }
                            }
                        }
                    }),
                _pipelineProgress,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        Place(new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children =
                            {
                                _pipelineStepSelector,
                                _pipelineRunButton
                            }
                        }, 0),
                        Place(_pipelineOpenButton, 1)
                    }
                }
            }
        };
        var pipelinePreviewPanel = MakeSharedPreviewPanel(
            _pipelineTextureSurface,
            _pipelineDxfPreview,
            _pipelineDxfPreviewStatus,
            pipelineImportDxfButton,
            _pipelineDxfSelector,
            enableLayerOverlay: true,
            out _pipelineSharedPreview);
        var pipelineContent = MakeWorkspace(
            pipelineInspector,
            pipelinePreviewPanel,
            _pipelineLogBox,
            "流程日志",
            PipelineLogKey);

        foreach (var secondaryButton in new[]
        {
            inputButton, outputButton, cancelButton,
            hatchInputButton, hatchOutputButton, hatchCancelButton,
            pipelineInputButton, pipelineLayerOutputButton, pipelineDxfOutputButton,
            pipelineCancelButton,
            _openOutputButton, _hatchOpenButton, _pipelineOpenButton
        })
            UiTheme.ApplyGhostStyle(secondaryButton);
        UiTheme.MarkDanger(cancelButton);
        UiTheme.MarkDanger(hatchCancelButton);
        UiTheme.MarkDanger(pipelineCancelButton);

        var workflowTabs = new TabControl
        {
            SelectedIndex = 0,
            Margin = new Thickness(16, 0, 16, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Items =
            {
                new TabItem
                {
                    Header = "三步流程",
                    Content = pipelineContent
                },
                new TabItem
                {
                    Header = "灰度图分层",
                    Content = new ScrollViewer
                    {
                        Padding = new Thickness(28),
                        Content = new Border
                        {
                            MaxWidth = 1240,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Child = layerContent
                        }
                    }
                },
                new TabItem
                {
                    Header = "纹理转 Hatch DXF",
                    Content = hatchContent
                }
            }
        };

        var appHeader = new Border
        {
            Padding = new Thickness(20, 8),
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(18, 22, 30), 0),
                    new GradientStop(Color.FromRgb(13, 16, 21), 1)
                }
            },
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 12,
                Children =
                {
                    Place(new Border
                    {
                        Width = 44,
                        Height = 44,
                        Padding = new Thickness(7),
                        CornerRadius = new CornerRadius(10),
                        Background = UiTheme.CardBrush,
                        BorderBrush = UiTheme.BorderSubtleBrush,
                        BorderThickness = new Thickness(1),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new Image
                        {
                            Source = new Bitmap(
                                AssetLoader.Open(
                                    new Uri("avares://GrayscaleLayersMac/Assets/AppIcon.png"))),
                            Width = 28,
                            Height = 28
                        }
                    }, 0),
                    Place(new StackPanel
                    {
                        Spacing = 2,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "纹理预处理工作台",
                                FontSize = 16,
                                FontWeight = FontWeight.SemiBold,
                                LetterSpacing = 0.3
                            },
                            new TextBlock
                            {
                                Text = "GRAYSCALE · HATCH · DXF",
                                FontSize = 9.5,
                                Foreground = UiTheme.TextFaintBrush,
                                LetterSpacing = 2.2
                            }
                        }
                    }, 1)
                }
            }
        };

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("64,*"),
            Children =
            {
                AtRow(appHeader, 0),
                AtRow(workflowTabs, 1)
            }
        };
    }

    private static NumericUpDown MakeNumberBox(
        decimal value,
        decimal increment,
        decimal maximum,
        int decimalPlaces = 3,
        decimal minimum = 0,
        bool showButtons = true) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        Increment = increment,
        FormatString = decimalPlaces == 0 ? "0" : $"0.{new string('#', decimalPlaces)}",
        ShowButtonSpinner = showButtons,
        FontFamily = UiTheme.MonoFont,
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static TextBox MakeLogBox() => UiTheme.CreateLogBox();

    private static Control MakeVoronoiPanel(
        NumericUpDown blocks,
        NumericUpDown minimumPercent,
        NumericUpDown maximumPercent,
        NumericUpDown blur,
        NumericUpDown correlation,
        NumericUpDown seed)
    {
        return UiTheme.CardExpander(
            "Voronoi 分块与边界扩散",
            new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "设置为 0 块可关闭分块。面积使用整个加工幅面的百分比；最外层边界保持不变。",
                        FontSize = 12,
                        Foreground = UiTheme.TextSecondaryBrush,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("加工块数量", blocks, 0),
                            MakeLabeledControl("单块最小面积（%）", minimumPercent, 1),
                            MakeLabeledControl("单块最大面积（%）", maximumPercent, 2)
                        }
                    },
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("边界扩散宽度（mm）", blur, 0),
                            MakeLabeledControl("连续变化长度（mm）", correlation, 1),
                            MakeLabeledControl("随机种子", seed, 2)
                        }
                    }
                }
            });
    }

    private static Control MakeInspectorSection(string title, params Control[] controls)
    {
        var content = new Grid
        {
            RowSpacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var control in controls)
        {
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(control, content.RowDefinitions.Count);
            content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            content.Children.Add(control);
        }
        return UiTheme.CardExpander(title, content);
    }

    /// <summary>
    /// 预览区只有「纹理」与「DXF」两个标签页：纹理界面内部用第 0 层承载源纹理，
    /// 1..N 承载灰度分层，所以不再需要单独的分层标签页。
    /// </summary>
    private static Control MakeSharedPreviewPanel(
        GrayscaleLayerPreviewControl texture,
        DxfPreviewControl dxfPreview,
        TextBlock dxfStatus,
        Button importButton,
        ComboBox? fileSelector,
        bool enableLayerOverlay,
        out SharedPreviewView view)
    {
        var textureContent = MakeTexturePreviewContent(texture);
        Control dxfContent;
        Action updateDxfOverlayControls;
        if (enableLayerOverlay)
        {
            dxfContent = MakePipelineDxfPreviewContent(
                dxfPreview,
                dxfStatus,
                importButton,
                fileSelector,
                out updateDxfOverlayControls);
        }
        else
        {
            dxfContent = MakeDxfPreviewContent(
                dxfPreview,
                dxfStatus,
                importButton,
                fileSelector);
            updateDxfOverlayControls = static () => { };
        }
        var textureTab = new ToggleButton { Content = "纹理" };
        var dxfTab = new ToggleButton { Content = "DXF" };
        var sharedView = new SharedPreviewView(
            textureTab,
            dxfTab,
            textureContent,
            dxfContent,
            new SharedPreviewSelection(),
            updateDxfOverlayControls);
        textureTab.Click += (_, _) => SelectSharedPreview(sharedView, SharedPreviewKind.Texture);
        dxfTab.Click += (_, _) => SelectSharedPreview(sharedView, SharedPreviewKind.Dxf);
        SelectSharedPreview(sharedView, SharedPreviewKind.Texture);
        view = sharedView;

        return new Grid
        {
            Margin = new Thickness(0, 12, 12, 12),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            Children =
            {
                AtRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        Place(textureTab, 1),
                        Place(dxfTab, 2)
                    }
                }, 0),
                AtRow(new Grid
                {
                    Children = { textureContent, dxfContent }
                }, 1)
            }
        };
    }

    private static Control MakeTexturePreviewContent(GrayscaleLayerPreviewControl view) => view;

    /// <summary>
    /// 把分层结果接到纹理界面里（第 1..N 层），第 0 层的源纹理保持不变。
    /// 分层跑完自动切回纹理界面，让用户直接看到结果。
    /// </summary>
    private async Task RefreshPipelineLayersAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        await _pipelineTextureSurface.LoadLayersAsync(directory, cancellationToken);
        SelectSharedPreview(_pipelineSharedPreview, SharedPreviewKind.Texture);
    }

    /// <summary>把纹理导入状态（尺寸、DPI、物理尺寸、错误）写到纹理界面。</summary>
    private static void RenderTexturePreview(
        GrayscaleLayerPreviewControl view,
        TexturePreviewState state)
    {
        view.SetMetadata(state.MetadataText, isError: state.Phase == TexturePreviewPhase.Failed);
        view.SetPhysicalSize(state.PhysicalSizeText);
    }

    private static Control MakeDxfPreviewContent(
        DxfPreviewControl preview,
        TextBlock status,
        Button importButton,
        ComboBox? fileSelector)
    {
        var fitButton = new Button { Content = "适应窗口" };
        fitButton.Click += (_, _) => preview.FitToView();
        var topButton = new Button { Content = "顶视图" };
        topButton.Click += (_, _) => preview.SetTopView();
        var isometricButton = new Button { Content = "等轴测" };
        isometricButton.Click += (_, _) => preview.SetIsometricView();
        UiTheme.ApplyGhostStyle(importButton, small: true);
        UiTheme.ApplyGhostStyle(topButton, small: true);
        UiTheme.ApplyGhostStyle(isometricButton, small: true);
        UiTheme.ApplyGhostStyle(fitButton, small: true);
        status.FontFamily = UiTheme.MonoFont;
        status.FontSize = 11;
        var arrowCheckBox = new CheckBox
        {
            Content = "显示方向箭头",
            IsChecked = preview.ShowDirectionArrows,
            VerticalAlignment = VerticalAlignment.Center
        };
        arrowCheckBox.IsCheckedChanged += (_, _) =>
            preview.ShowDirectionArrows = arrowCheckBox.IsChecked == true;
        status.Text = preview.Summary;
        return new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 10,
            Children =
            {
                AtRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        Place(importButton, 0),
                        Place(topButton, 1),
                        Place(isometricButton, 2),
                        Place(fitButton, 3)
                    }
                }, 0),
                AtRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        fileSelector is null
                            ? Place(new TextBlock
                            {
                                Text = "左键拖拽环视 · 滚轮缩放 · 中键平移 · Shift + 中键环视 · 双击中键适应窗口",
                                Foreground = UiTheme.TextFaintBrush,
                                FontSize = 11,
                                VerticalAlignment = VerticalAlignment.Center
                            }, 0)
                            : Place(new StackPanel
                            {
                                Spacing = 5,
                                Children =
                                {
                                    fileSelector,
                                    new TextBlock
                                    {
                                        Text = "左键拖拽环视 · 滚轮缩放 · 中键平移 · Shift + 中键环视 · 双击中键适应窗口",
                                        Foreground = UiTheme.TextFaintBrush,
                                        FontSize = 11
                                    }
                                }
                            }, 0),
                        Place(arrowCheckBox, 1)
                    }
                }, 1),
                AtRow(UiTheme.CanvasCard(preview), 2),
                AtRow(status, 3)
            }
        };
    }

    private static Control MakePipelineDxfPreviewContent(
        DxfPreviewControl preview,
        TextBlock status,
        Button importButton,
        ComboBox? fileSelector,
        out Action updateOverlayControlAvailability)
    {
        var fitButton = new Button { Content = "适应窗口" };
        fitButton.Click += (_, _) => preview.FitToView();
        var topButton = new Button { Content = "顶视图" };
        var isometricButton = new Button { Content = "等轴测" };
        var textureCheckBox = new CheckBox
        {
            Content = "显示灰度纹理",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        var linesCheckBox = new CheckBox
        {
            Content = "显示 DXF 填充线",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        var textureOpacity = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0.55,
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center
        };
        var arrowCheckBox = new CheckBox
        {
            Content = "显示方向箭头",
            IsChecked = preview.ShowDirectionArrows,
            VerticalAlignment = VerticalAlignment.Center
        };

        string? previousOverlayStatus = null;
        var overlayControlUpdateQueued = false;
        void UpdateOverlayControlAvailability()
        {
            textureCheckBox.IsEnabled = preview.HasTexture;
            textureOpacity.IsEnabled = preview.HasTexture && preview.ShowTexture;
            arrowCheckBox.IsEnabled = preview.ShowLines;

            var statusText = status.Text ?? string.Empty;
            if (previousOverlayStatus is not null)
            {
                var previousSuffix = $" · {previousOverlayStatus}";
                if (statusText.EndsWith(previousSuffix, StringComparison.Ordinal))
                    statusText = statusText[..^previousSuffix.Length];
            }

            previousOverlayStatus = preview.TextureStatus;
            status.Text = string.IsNullOrWhiteSpace(statusText)
                ? previousOverlayStatus
                : $"{statusText} · {previousOverlayStatus}";
        }

        void QueueOverlayControlUpdate()
        {
            if (overlayControlUpdateQueued)
                return;

            overlayControlUpdateQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                overlayControlUpdateQueued = false;
                UpdateOverlayControlAvailability();
            });
        }

        topButton.Click += (_, _) =>
        {
            preview.SetTopView();
            UpdateOverlayControlAvailability();
        };
        isometricButton.Click += (_, _) =>
        {
            preview.SetIsometricView();
            UpdateOverlayControlAvailability();
        };
        preview.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, _) => QueueOverlayControlUpdate(),
            RoutingStrategies.Direct | RoutingStrategies.Bubble,
            handledEventsToo: true);
        preview.AddHandler(
            InputElement.PointerCaptureLostEvent,
            (_, _) => QueueOverlayControlUpdate(),
            RoutingStrategies.Direct | RoutingStrategies.Bubble,
            handledEventsToo: true);
        textureCheckBox.IsCheckedChanged += (_, _) =>
        {
            preview.ShowTexture = textureCheckBox.IsChecked == true;
            UpdateOverlayControlAvailability();
        };
        linesCheckBox.IsCheckedChanged += (_, _) =>
        {
            preview.ShowLines = linesCheckBox.IsChecked == true;
            UpdateOverlayControlAvailability();
        };
        textureOpacity.ValueChanged += (_, _) =>
            preview.TextureOpacity = textureOpacity.Value;
        arrowCheckBox.IsCheckedChanged += (_, _) =>
            preview.ShowDirectionArrows = arrowCheckBox.IsChecked == true;
        status.Text = preview.Summary;
        updateOverlayControlAvailability = UpdateOverlayControlAvailability;
        UpdateOverlayControlAvailability();
        return new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 10,
            Children =
            {
                AtRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        Place(importButton, 0),
                        Place(topButton, 1),
                        Place(isometricButton, 2),
                        Place(fitButton, 3)
                    }
                }, 0),
                AtRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        fileSelector is null
                            ? Place(new TextBlock
                            {
                                Text = "左键拖拽环视 · 滚轮缩放 · 中键平移 · Shift + 中键环视 · 双击中键适应窗口",
                                Foreground = UiTheme.TextFaintBrush,
                                VerticalAlignment = VerticalAlignment.Center
                            }, 0)
                            : Place(new StackPanel
                            {
                                Spacing = 5,
                                Children =
                                {
                                    fileSelector,
                                    new TextBlock
                                    {
                                        Text = "左键拖拽环视 · 滚轮缩放 · 中键平移 · Shift + 中键环视 · 双击中键适应窗口",
                                        Foreground = UiTheme.TextFaintBrush,
                                        FontSize = 11
                                    }
                                }
                            }, 0)
                    }
                }, 1),
                AtRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        textureCheckBox,
                        new TextBlock
                        {
                            Text = "纹理透明度",
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = UiTheme.TextFaintBrush
                        },
                        textureOpacity,
                        linesCheckBox,
                        arrowCheckBox
                    }
                }, 2),
                AtRow(UiTheme.CanvasCard(preview), 3),
                AtRow(status, 4)
            }
        };
    }

    private static void SelectSharedPreview(SharedPreviewView view, SharedPreviewKind kind)
    {
        view.Selection.Select(kind);
        view.TextureContent.IsVisible = kind == SharedPreviewKind.Texture;
        view.DxfContent.IsVisible = kind == SharedPreviewKind.Dxf;
        view.TextureTab.IsChecked = kind == SharedPreviewKind.Texture;
        view.DxfTab.IsChecked = kind == SharedPreviewKind.Dxf;
    }

    /// <summary>
    /// 恢复日志面板上次的折叠状态，并在之后每次切换时落盘。
    /// 先恢复、再订阅，这样恢复动作本身不会触发一次多余的写入。
    /// </summary>
    private LogPanelView PersistLogCollapse(LogPanelView panel, string key)
    {
        panel.SetCollapsed(_workspaceSplitSettings.LoadLogCollapsed(key));
        panel.CollapsedChanged += (_, _) =>
            _workspaceSplitSettings.TrySaveLogCollapsed(key, panel.IsCollapsed);
        return panel;
    }

    private Control MakeWorkspace(
        StackPanel inspector,
        Control previewPanel,
        TextBox log,
        string logTitle,
        string logKey)
    {
        var actionRow = inspector.Children[^1];
        inspector.Children.RemoveAt(inspector.Children.Count - 1);
        var progress = inspector.Children[^1];
        inspector.Children.RemoveAt(inspector.Children.Count - 1);
        inspector.Margin = new Thickness(18, 16, 18, 16);
        inspector.Spacing = 14;
        inspector.HorizontalAlignment = HorizontalAlignment.Stretch;
        var inspectorSurface = new Border
        {
            Padding = new Thickness(0),
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Background = UiTheme.PanelBrush,
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    AtRow(new ScrollViewer
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = inspector
                    }, 0),
                    AtRow(new Border
                    {
                        Padding = new Thickness(18, 14, 18, 18),
                        BorderBrush = UiTheme.BorderSubtleBrush,
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Background = UiTheme.BarBrush,
                        Child = new StackPanel
                        {
                            Spacing = 10,
                            Children = { progress, actionRow }
                        }
                    }, 1)
                }
            }
        };
        var logPanel = PersistLogCollapse(UiTheme.LogPanel(log, logTitle), logKey);
        var logSurface = logPanel.Root;
        logSurface.Margin = new Thickness(0, 0, 12, 0);

        previewPanel.MinWidth = 420;

        var previewColumn = new ColumnDefinition
        {
            Width = new GridLength(_workspacePreviewRatio, GridUnitType.Star),
            MinWidth = 420
        };
        var inspectorColumn = new ColumnDefinition
        {
            Width = new GridLength(1 - _workspacePreviewRatio, GridUnitType.Star),
            MinWidth = 460
        };
        var splitter = UiTheme.WorkspaceSplitter();
        splitter.DragCompleted += (_, _) =>
            CompleteWorkspaceSplitDrag(previewColumn, inspectorColumn);

        _workspaceColumns.Add(new WorkspaceColumns(previewColumn, inspectorColumn));

        return AssembleWorkspaceGrid(
            previewColumn,
            inspectorColumn,
            previewPanel,
            logSurface,
            splitter,
            inspectorSurface);
    }

    internal static Grid AssembleWorkspaceGrid(
        ColumnDefinition previewColumn,
        ColumnDefinition inspectorColumn,
        Control previewPanel,
        Control logSurface,
        GridSplitter splitter,
        Control inspectorSurface)
    {
        Grid.SetRow(logSurface, 1);
        Grid.SetColumn(splitter, 1);
        Grid.SetRowSpan(splitter, 2);
        Grid.SetColumn(inspectorSurface, 2);
        Grid.SetRowSpan(inspectorSurface, 2);

        return new Grid
        {
            ColumnDefinitions =
            {
                previewColumn,
                new ColumnDefinition(new GridLength(8)),
                inspectorColumn
            },
            // 底部日志行自适应：面板自身动画收拢高度，行高跟着走，
            // 多出来的空间由上方 Star 行（预览区）自动吃掉。
            RowDefinitions = new RowDefinitions("*,Auto"),
            ColumnSpacing = 0,
            RowSpacing = 12,
            Children =
            {
                previewPanel,
                logSurface,
                splitter,
                inspectorSurface
            }
        };
    }

    private void CompleteWorkspaceSplitDrag(
        ColumnDefinition previewColumn,
        ColumnDefinition inspectorColumn)
    {
        var availableWidth = previewColumn.ActualWidth + inspectorColumn.ActualWidth;
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
            return;

        var ratio = previewColumn.ActualWidth / availableWidth;
        if (!double.IsFinite(ratio))
            return;

        _workspacePreviewRatio = Math.Clamp(
            ratio,
            WorkspaceSplitSettings.MinimumPreviewRatio,
            WorkspaceSplitSettings.MaximumPreviewRatio);
        foreach (var columns in _workspaceColumns)
        {
            columns.Preview.Width = new GridLength(_workspacePreviewRatio, GridUnitType.Star);
            columns.Inspector.Width = new GridLength(1 - _workspacePreviewRatio, GridUnitType.Star);
        }

        _workspaceSplitSettings.TrySavePreviewRatio(_workspacePreviewRatio);
    }

    private static Control MakeField(string label, Control field, Button button)
    {
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        grid.Children.Add(Place(field, 0));
        grid.Children.Add(Place(button, 1));
        return new StackPanel
        {
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { UiTheme.FieldLabel(label), grid }
        };
    }

    private static Control MakeLabeledControl(string label, Control control, int column)
    {
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 7,
            Children = { UiTheme.FieldLabel(label), AtRow(control, 1) }
        };
        Grid.SetColumn(grid, column);
        return grid;
    }

    private static T Place<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T AtRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private async Task PickInputAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择灰度纹理图",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图像文件")
                {
                    Patterns = ["*.tif", "*.tiff", "*.png", "*.jpg", "*.jpeg", "*.bmp"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _inputBox.Text = path;
            if (string.IsNullOrWhiteSpace(_outputBox.Text))
                _outputBox.Text = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}_layers");
        }
    }

    private async Task PickOutputAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择结果保存目录",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            _outputBox.Text = path;
    }

    private async Task PickPipelineInputAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择原始灰度纹理图",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图像文件")
                {
                    Patterns = ["*.tif", "*.tiff", "*.png", "*.jpg", "*.jpeg", "*.bmp"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        _pipelineInputBox.Text = path;
        var parent = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(_pipelineLayerOutputBox.Text))
            _pipelineLayerOutputBox.Text = Path.Combine(parent, $"{name}_layers");
        if (string.IsNullOrWhiteSpace(_pipelineDxfOutputBox.Text))
            _pipelineDxfOutputBox.Text = Path.Combine(parent, $"{name}_dxf");

        await LoadTexturePreviewAsync(
            path,
            _pipelineTextureSurface,
            _pipelineDpiBox,
            _pipelineWidthBox,
            _pipelineHeightBox,
            _pipelinePreviewController,
            _pipelineSharedPreview);
    }

    // 把单步执行按钮的 Flyout 行为折成 ComboBox：选中即触发对应步骤，并清空选择让 placeholder 再次出现。
    // TemplateApplied 时把内部的 PART_Popup 强制设成 TopEdgeAlignedLeft（下拉打开时往"上"展开），避免按钮位于窗口底部时被裁切。
    private void ConfigurePipelineStepSelector(Button cancelButton)
{
    _pipelineStepSelector.SelectionChanged += async (_, _) =>
    {
        if (_pipelineStepSelector.SelectedIndex < 0)
            return;
        var selectedIndex = _pipelineStepSelector.SelectedIndex;
        if (selectedIndex >= PipelineStepOptions.Length)
            return;
        // 立刻关闭下拉、清空选择 —— placeholder 复位后才能再次选同一项。
        _pipelineStepSelector.IsDropDownOpen = false;
        _pipelineStepSelector.SelectedIndex = -1;
        if (_cancellation is not null)
            return;
        var (_, mode) = PipelineStepOptions[selectedIndex];
        cancelButton.IsEnabled = true;
        try
        {
            await RunPipelineAsync(mode);
        }
        finally
        {
            cancelButton.IsEnabled = false;
        }
    };
    _pipelineStepSelector.TemplateApplied += (_, e) =>
    {
        if (e.NameScope.Find<Popup>("PART_Popup") is { } popup)
            popup.Placement = PlacementMode.TopEdgeAlignedLeft;
    };
}

    private async Task PickPipelineFolderAsync(TextBox target, string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            target.Text = path;
    }

    private async Task RunPipelineAsync(PipelineRunMode mode)
    {
        var input = _pipelineInputBox.Text?.Trim();
        var layerOutput = _pipelineLayerOutputBox.Text?.Trim();
        var dxfOutput = _pipelineDxfOutputBox.Text?.Trim();
        var machineName = _pipelineMachineNameBox.Text?.Trim();
        var needsLayers = mode is PipelineRunMode.All or PipelineRunMode.GrayscaleOnly;
        var needsDxf = mode is PipelineRunMode.All or PipelineRunMode.DxfOnly;
        var needsMachine = mode is PipelineRunMode.All or PipelineRunMode.MachineOnly;
        if (needsMachine && string.IsNullOrWhiteSpace(machineName))
        {
            machineName = $"machine_file_{DateTime.Now:yyyyMMdd_HHmmss}";
            _pipelineMachineNameBox.Text = machineName;
        }

        if (needsLayers &&
            (string.IsNullOrWhiteSpace(input) || !File.Exists(input)))
        {
            await ShowMessageAsync("请先选择有效的原始灰度图。");
            return;
        }
        if (((needsLayers || needsDxf) && string.IsNullOrWhiteSpace(layerOutput)) ||
            ((needsDxf || needsMachine) && string.IsNullOrWhiteSpace(dxfOutput)))
        {
            await ShowMessageAsync(
                needsLayers && needsDxf && needsMachine
                    ? "请同时选择分层 TIFF 和 DXF 的输出目录。"
                    : needsLayers || needsDxf
                        ? "请先选择分层 TIFF 输出目录。"
                        : "请先选择 DXF 输出目录。");
            return;
        }
        if (needsMachine &&
            (machineName is "." or ".." ||
             machineName?.Contains('/') == true ||
             machineName?.Contains('\\') == true))
        {
            await ShowMessageAsync("加工文件名不能是“.”或“..”，且不能包含 / 或 \\。");
            return;
        }

        var layerStep = _pipelineLayerStepBox.Value;
        if (needsMachine &&
            (!layerStep.HasValue ||
             layerStep.Value < 1m ||
             layerStep.Value > 100000m ||
             layerStep.Value != decimal.Truncate(layerStep.Value)))
        {
            await ShowMessageAsync(
                "每层下降深度必须是 1–100000 μm 的整数，才能与 0.001 mm 的机器坐标精度一致。");
            return;
        }

        var scriptsDirectory = ApplicationLayout.GetScriptsDirectory(AppContext.BaseDirectory);
        var layerScript = Path.Combine(scriptsDirectory, "grayscale_layers.py");
        var hatchScript = Path.Combine(scriptsDirectory, "texture_to_hatch_dxf.py");
        var machineScript = Path.Combine(scriptsDirectory, "dxf_to_machine_file.py");
        if ((needsLayers && !File.Exists(layerScript)) ||
            (needsDxf && !File.Exists(hatchScript)) ||
            (needsMachine && !File.Exists(machineScript)))
        {
            await ShowMessageAsync(
                "找不到流程所需的 Python 脚本（grayscale_layers.py、texture_to_hatch_dxf.py、" +
                $"dxf_to_machine_file.py）。请重新编译或发布应用。\n脚本目录：{scriptsDirectory}");
            return;
        }

        var python = await FindPythonAsync();
        if (python is null)
        {
            await ShowMessageAsync("找不到带有 numpy 和 Pillow 的 Python 3。");
            return;
        }
        double? dpi = null;
        if (needsDxf && !TextureFallbackDpi.TryParseOptional(_pipelineDpiBox.Text, out dpi))
        {
            await ShowMessageAsync("DPI 必须留空或填写有限且大于 0 的数字。");
            return;
        }

        var layers = (int)(_pipelineLayersBox.Value ?? 10);
        var minLevel = 0;
        var maxLevel = 255;
        var rangeError = "";
        if (needsLayers && !TryReadGrayLevelRange(
                _pipelineMinLevelBox,
                _pipelineMaxLevelBox,
                layers,
                out minLevel,
                out maxLevel,
                out rangeError))
        {
            await ShowMessageAsync(rangeError);
            return;
        }
        var width = _pipelineWidthBox.Value ?? 100;
        var height = _pipelineHeightBox.Value ?? 100;
        var spacing = _pipelineSpacingBox.Value ?? 0.02m;
        var hatchAngleStep = _pipelineHatchAngleStepBox.Value ?? 0;
        var voronoiError = "";
        if (needsDxf && !TryValidateVoronoiSettings(
                _pipelineBlocksBox,
                _pipelineMinBlockPercentBox,
                _pipelineMaxBlockPercentBox,
                _pipelineBoundaryCorrelationBox,
                out voronoiError))
        {
            await ShowMessageAsync(voronoiError);
            return;
        }

        var power = 0;
        var frequency = 0;
        var pulseWidthIdx = 0;
        var scanSpeed = 0;
        var jumpVelocity = 0;
        var jumpDelay = 0;
        var accScale = 0;
        var cornerScale = 0;
        var endScale = 0;
        var timeLag = 0;
        var laserOnShift = 0;
        var delayLaserOff = 0;
        var delayLaserOn = 0;
        var laserError = "";
        if (needsMachine && (!TryGetNonNegativeInt(_pipelinePowerBox, "功率（power）", out power, out laserError) ||
            !TryGetNonNegativeInt(_pipelineFrequencyBox, "频率（frequency）", out frequency, out laserError) ||
            !TryGetNonNegativeInt(_pipelinePulseWidthIdxBox, "脉宽索引（pulseWidthIdx）", out pulseWidthIdx, out laserError) ||
            !TryGetNonNegativeInt(_pipelineScanSpeedBox, "扫描速度（scanSpeed）", out scanSpeed, out laserError) ||
            !TryGetNonNegativeInt(_pipelineJumpVelocityBox, "跳转速度（jump_vel）", out jumpVelocity, out laserError) ||
            !TryGetNonNegativeInt(_pipelineJumpDelayBox, "跳转延迟（jump_delay）", out jumpDelay, out laserError) ||
            !TryGetNonNegativeInt(_pipelineAccScaleBox, "加速度比例（accScale）", out accScale, out laserError) ||
            !TryGetNonNegativeInt(_pipelineCornerScaleBox, "转角比例（cornerScale）", out cornerScale, out laserError) ||
            !TryGetNonNegativeInt(_pipelineEndScaleBox, "结束比例（endScale）", out endScale, out laserError) ||
            !TryGetNonNegativeInt(_pipelineTimeLagBox, "时间滞后（timeLag）", out timeLag, out laserError) ||
            !TryGetNonNegativeInt(_pipelineLaserOnShiftBox, "开光偏移（laserOnShift）", out laserOnShift, out laserError) ||
            !TryGetNonNegativeInt(_pipelineDelayLaserOffBox, "关光延迟（delaseroff）", out delayLaserOff, out laserError) ||
            !TryGetNonNegativeInt(_pipelineDelayLaserOnBox, "开光延迟（delaseron）", out delayLaserOn, out laserError)))
        {
            await ShowMessageAsync(laserError);
            return;
        }

        var dxfOutputAbsolute = "";
        string machineOutputPath = "";
        string machineTempPath = "";
        string machineLockPath = "";
        try
        {
            if (!string.IsNullOrWhiteSpace(dxfOutput))
            {
                dxfOutputAbsolute = Path.GetFullPath(dxfOutput);
                if (needsMachine)
                {
                    var dxfParent = new DirectoryInfo(dxfOutputAbsolute).Parent?.FullName;
                    if (string.IsNullOrWhiteSpace(dxfParent))
                    {
                        await ShowMessageAsync("DXF 输出目录必须有可用的父目录。");
                        return;
                    }
                    machineOutputPath = Path.Combine(dxfParent, machineName!);
                    machineTempPath = Path.Combine(dxfParent, $".{machineName}.building");
                    machineLockPath = Path.Combine(dxfParent, $".{machineName}.lock");
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await ShowMessageAsync($"无法解析加工文件输出路径：{ex.Message}");
            return;
        }

        _lastMachineOutputPath = null;
        _pipelineOpenButton.IsEnabled = false;
        if (needsMachine && string.Equals(
                Path.TrimEndingDirectorySeparator(machineOutputPath),
                Path.TrimEndingDirectorySeparator(dxfOutputAbsolute),
                StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync("加工文件名不能与 DXF 输出目录同名。");
            return;
        }

        foreach (var collisionPath in needsMachine
                     ? new[] { machineOutputPath, machineTempPath, machineLockPath }
                     : Array.Empty<string>())
        {
            if (File.Exists(collisionPath) || Directory.Exists(collisionPath))
            {
                await ShowMessageAsync($"加工文件输出路径已存在，请更换加工文件名或清理后重试：\n{collisionPath}");
                return;
            }
        }

        _cancellation = new CancellationTokenSource();
        var pipelineBlocksBoxWasEnabled = _pipelineBlocksBox.IsEnabled;
        _pipelineRunButton.IsEnabled = false;
        _pipelineBlocksBox.IsEnabled = false;
        _pipelineBlockCenterMotionBox.IsEnabled = false;
        _pipelineProgress.IsIndeterminate = true;
        _pipelineLogBox.Text = "";
        _pipelineDxfPreview.Clear();
        _pipelineDxfPreviewStatus.Text = _pipelineDxfPreview.Summary;
        _pipelineSharedPreview.UpdateDxfOverlayControls();
        _pipelineSharedPreview.Selection.ClearDxf();
        _pipelineDxfFiles.Clear();
        // 全部执行按钮 + 单步可选框在运行期间统一禁用，避免重复触发。
        var pipelineStartButtons = new Control[]
        {
            _pipelineRunButton,
            _pipelineStepSelector
        };
        foreach (var button in pipelineStartButtons)
            button.IsEnabled = false;

        string[] layerFiles = [];
        var currentRunDxfFiles = new List<string>();
        var progressWindow = new ProcessingProgressWindow(
            mode == PipelineRunMode.All ? "执行全部流程" : "执行单步流程",
            mode == PipelineRunMode.All
                ? "正在执行全部流程，请稍候…"
                : "正在执行所选步骤，请稍候…");
        progressWindow.CancelRequested += (_, _) => _cancellation?.Cancel();
        progressWindow.Show(this);
        try
        {
            if (needsLayers)
            {
                progressWindow.UpdateMessage("正在执行第 1 步：灰度分层…");
                Directory.CreateDirectory(layerOutput!);
                AppendPipelineLog(mode == PipelineRunMode.All
                    ? "步骤 1/3：开始生成灰度分层 TIFF…"
                    : "第 1 步：开始生成灰度分层 TIFF…");
                AppendPipelineLog($"输入：{input}");
                AppendPipelineLog($"分层目录：{layerOutput}");
                AppendPipelineLog($"灰阶区间：[{minLevel}, {maxLevel}]，分层数量：{layers}\n");

                var layerStartedAt = DateTime.UtcNow.AddSeconds(-2);
                var layerInfo = CreatePythonProcess(python);
                foreach (var argument in new[]
                {
                    layerScript, input!, layerOutput!,
                    "--layers", layers.ToString(CultureInfo.InvariantCulture)
                })
                    layerInfo.ArgumentList.Add(argument);
                GrayLevelRange.AppendArguments(layerInfo.ArgumentList, minLevel, maxLevel);
                if (_pipelineBelowIsWhite.IsChecked == true)
                    layerInfo.ArgumentList.Add("--below-is-white");

                var layerExitCode = await RunProcessAsync(
                    layerInfo,
                    AppendPipelineLog,
                    _cancellation.Token);
                if (layerExitCode != 0)
                    throw new InvalidOperationException($"灰度分层失败，退出代码：{layerExitCode}");

                layerFiles = Directory
                    .EnumerateFiles(layerOutput!, "layer_*.tiff")
                    .Where(path => File.GetLastWriteTimeUtc(path) >= layerStartedAt)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (layerFiles.Length != layers)
                    throw new InvalidOperationException(
                        $"预期生成 {layers} 个分层 TIFF，实际找到 {layerFiles.Length} 个。");

                AppendPipelineLog(mode == PipelineRunMode.All
                    ? $"\n步骤 1/3 完成：共生成 {layerFiles.Length} 个 TIFF。"
                    : $"\n第 1 步完成：共生成 {layerFiles.Length} 个 TIFF。");
                await RefreshPipelineLayersAsync(
                    layerOutput!,
                    _cancellation.Token);
                if (mode == PipelineRunMode.GrayscaleOnly)
                    return;
            }

            if (needsDxf)
            {
                progressWindow.UpdateMessage("正在执行第 2 步：生成 DXF…");
                if (layerFiles.Length == 0)
                {
                    layerFiles = Directory
                        .EnumerateFiles(layerOutput!, "layer_*.tiff")
                        .Where(IsRegularNonEmptyFile)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (layerFiles.Length == 0)
                        throw new InvalidOperationException(
                            "第 2 步需要先在分层 TIFF 输出目录中生成至少一个 layer_*.tiff 文件。");
                }

                Directory.CreateDirectory(dxfOutput!);
                AppendPipelineLog(mode == PipelineRunMode.All
                    ? "步骤 2/3：开始逐层生成 Hatch DXF…\n"
                    : "第 2 步：开始逐层生成 Hatch DXF…\n");
            var baseVoronoiSeed = (int)(_pipelineVoronoiSeedBox.Value ?? 12345);
                currentRunDxfFiles = new List<string>(layerFiles.Length);

            for (var index = 0; index < layerFiles.Length; index++)
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                var layerFile = layerFiles[index];
                var outputFile = Path.Combine(
                    dxfOutputAbsolute,
                    $"{Path.GetFileNameWithoutExtension(layerFile)}.dxf");
                var previewFile = Path.ChangeExtension(outputFile, ".preview.png");
                // 每层从用户设置的基础种子派生出不同且可复现的种子，避免多层
                // 使用完全相同的 Voronoi 分块。使用质数步长可避免相邻层相关。
                var layerVoronoiSeed = (int)(
                    ((baseVoronoiSeed - 1L + index * 104729L) % int.MaxValue) + 1);
                var layerHatchAngle = ((layerFiles.Length == 1 ? 1 : index) * hatchAngleStep) % 180m;
                AppendPipelineLog(
                    $"[{index + 1}/{layerFiles.Length}] {Path.GetFileName(layerFile)} → {Path.GetFileName(outputFile)}" +
                    $"（填充角度：{Invariant(layerHatchAngle)}°）" +
                    (_pipelineBlocksBox.Value > 0 ? $"（分块种子：{layerVoronoiSeed}）" : ""));

                var hatchInfo = CreatePythonProcess(python);
                foreach (var argument in new[]
                {
                    hatchScript, layerFile, outputFile,
                    "--width", Invariant(width),
                    "--height", Invariant(height),
                    "--spacing", Invariant(spacing),
                    "--angle", Invariant(layerHatchAngle),
                    "--anchor", _pipelineAnchorBox.SelectedIndex == 1 ? "top-left" : "center"
                })
                    hatchInfo.ArgumentList.Add(argument);
                if (dpi.HasValue)
                {
                    hatchInfo.ArgumentList.Add("--dpi");
                    hatchInfo.ArgumentList.Add(dpi.Value.ToString(CultureInfo.InvariantCulture));
                }
                if (_pipelineIncludeBorder.IsChecked == true)
                    hatchInfo.ArgumentList.Add("--border");
                if (_pipelineBidirectionalHatch.IsChecked == true)
                    hatchInfo.ArgumentList.Add("--bidirectional");
                hatchInfo.ArgumentList.Add("--preview-output");
                hatchInfo.ArgumentList.Add(previewFile);
                AddVoronoiArguments(
                    hatchInfo,
                    width,
                    height,
                    _pipelineBlocksBox,
                    _pipelineMinBlockPercentBox,
                    _pipelineMaxBlockPercentBox,
                    _pipelineBoundaryBlurBox,
                    _pipelineBoundaryCorrelationBox,
                    _pipelineVoronoiSeedBox,
                    layerVoronoiSeed);

                DxfTextureRegistration? textureRegistration = null;
                var hatchExitCode = await RunProcessAsync(
                    hatchInfo,
                    line =>
                    {
                        if (DxfTextureRegistration.TryParseProcessOutput(
                                line,
                                out var emittedRegistration))
                            textureRegistration = emittedRegistration;
                        AppendPipelineLog($"    {line}");
                    },
                    _cancellation.Token);
                if (hatchExitCode != 0)
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(layerFile)} 转换失败，退出代码：{hatchExitCode}");
                ValidateGeneratedLayerArtifacts(
                    outputFile,
                    previewFile,
                    (_pipelineBlocksBox.Value ?? 0) > 0);
                if (textureRegistration is null)
                    throw new InvalidOperationException(
                        "Hatch 生成结束，但未返回预览配准信息。");
                currentRunDxfFiles.Add(Path.GetFullPath(outputFile));
                var previewItem = new DxfLayerPreviewItem(
                    $"第 {index + 1:D2} 层 · {Path.GetFileName(outputFile)}",
                    outputFile,
                    previewFile,
                    textureRegistration);
                _pipelineDxfFiles.Add(previewItem);
                _pipelineDxfSelector.SelectedItem = previewItem;
            }

                AppendPipelineLog(mode == PipelineRunMode.All
                    ? $"\n步骤 2/3 完成：共生成 {layerFiles.Length} 个 DXF。"
                    : $"\n第 2 步完成：共生成 {layerFiles.Length} 个 DXF。");
                AppendPipelineLog($"DXF 目录：{dxfOutput}");
            }

            if (mode == PipelineRunMode.DxfOnly)
                return;

            if (needsMachine)
            {
                progressWindow.UpdateMessage("正在执行第 3 步：生成加工文件…");
                if (currentRunDxfFiles.Count == 0)
                {
                    currentRunDxfFiles = Directory
                        .EnumerateFiles(dxfOutput!, "*.dxf")
                        .Where(IsRegularNonEmptyFile)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .Select(Path.GetFullPath)
                        .ToList();
                    if (currentRunDxfFiles.Count == 0)
                        throw new InvalidOperationException(
                            "第 3 步需要先在 DXF 输出目录中生成至少一个有效的 .dxf 文件。");
                }

                var pathComparer = StringComparer.OrdinalIgnoreCase;
            var expectedDxfFiles = new HashSet<string>(currentRunDxfFiles, pathComparer);
            var missingDxfFiles = expectedDxfFiles
                .Where(path => !IsRegularNonEmptyFile(path))
                .OrderBy(path => path, pathComparer)
                .ToArray();
            if (missingDxfFiles.Length > 0)
            {
                var manifestError = new StringBuilder();
                manifestError.AppendLine(
                    $"本次 DXF 清单中有 {missingDxfFiles.Length} 个文件缺失或无效：");
                foreach (var path in missingDxfFiles)
                    manifestError.AppendLine($"- {path}");
                manifestError.Append("请重新运行流程生成完整的本次 DXF 清单。");
                throw new InvalidOperationException(manifestError.ToString());
            }
            AppendPipelineLog($"已验证本次 DXF 清单：{expectedDxfFiles.Count} 个文件。");

            AppendPipelineLog(mode == PipelineRunMode.All
                ? "\n步骤 3/3：开始生成机器加工文件…"
                : "\n第 3 步：开始生成机器加工文件…");
            var useBlockCenterMotion =
                (_pipelineBlocksBox.Value ?? 0) > 0 &&
                _pipelineBlockCenterMotionBox.IsChecked == true;
            AppendPipelineLog(
                $"加工块中心 XY 定位：{(useBlockCenterMotion ? "已启用" : "未启用")}。");
            var ownerToken = Guid.NewGuid().ToString("N");
            var machineInfo = CreatePythonProcess(python);
            foreach (var argument in new[]
            {
                machineScript, dxfOutputAbsolute, machineName!,
                "--owner-token", ownerToken,
                "--layer-step-um", Invariant(layerStep!.Value),
                "--power", power.ToString(CultureInfo.InvariantCulture),
                "--frequency", frequency.ToString(CultureInfo.InvariantCulture),
                "--pulse-width-idx", pulseWidthIdx.ToString(CultureInfo.InvariantCulture),
                "--scan-speed", scanSpeed.ToString(CultureInfo.InvariantCulture),
                "--jump-vel", jumpVelocity.ToString(CultureInfo.InvariantCulture),
                "--jump-delay", jumpDelay.ToString(CultureInfo.InvariantCulture),
                "--acc-scale", accScale.ToString(CultureInfo.InvariantCulture),
                "--corner-scale", cornerScale.ToString(CultureInfo.InvariantCulture),
                "--end-scale", endScale.ToString(CultureInfo.InvariantCulture),
                "--time-lag", timeLag.ToString(CultureInfo.InvariantCulture),
                "--laser-on-shift", laserOnShift.ToString(CultureInfo.InvariantCulture),
                "--delaseroff", delayLaserOff.ToString(CultureInfo.InvariantCulture),
                "--delaseron", delayLaserOn.ToString(CultureInfo.InvariantCulture)
            })
                machineInfo.ArgumentList.Add(argument);
            machineInfo.ArgumentList.Add(
                _pipelineScanAheadBox.IsChecked == true ? "--scan-ahead" : "--no-scan-ahead");
            machineInfo.ArgumentList.Add(
                _pipelineSkyWritingBox.IsChecked == true ? "--sky-writing" : "--no-sky-writing");
            machineInfo.ArgumentList.Add(
                useBlockCenterMotion
                    ? "--block-center-positioning"
                    : "--no-block-center-positioning");
            foreach (var layerDxfPath in currentRunDxfFiles)
            {
                machineInfo.ArgumentList.Add("--layer-dxf");
                machineInfo.ArgumentList.Add(layerDxfPath);
            }

            var machineExitCode = await RunProcessAsync(
                machineInfo,
                AppendPipelineLog,
                _cancellation.Token);
            if (machineExitCode != 0)
                throw new InvalidOperationException($"加工文件生成失败，退出代码：{machineExitCode}");
            if (!Directory.Exists(machineOutputPath))
                throw new InvalidOperationException($"加工文件生成结束，但未找到输出目录：{machineOutputPath}");

            _lastMachineOutputPath = machineOutputPath;
            AppendPipelineLog(mode == PipelineRunMode.All
                ? "\n步骤 3/3 完成：加工文件生成成功。"
                : "\n第 3 步完成：加工文件生成成功。");
            AppendPipelineLog($"加工文件目录：{machineOutputPath}");
            if (mode == PipelineRunMode.All)
                AppendPipelineLog(
                    $"三步流程完成：已生成 {layerFiles.Length} 个 TIFF、{layerFiles.Length} 个 DXF 和 1 个加工文件。");
            _pipelineOpenButton.IsEnabled = true;
            }
        }
        catch (OperationCanceledException)
        {
            AppendPipelineLog("\n操作已取消。");
            AppendPipelineLog(
                "为避免路径替换竞态误删其他任务的数据，程序不会自动删除第三步残留；" +
                "请确认没有生成进程仍在运行后再手动检查以下路径：");
            AppendPipelineLog($"临时目录：{machineTempPath}");
            AppendPipelineLog($"锁文件：{machineLockPath}");
        }
        catch (Exception ex)
        {
            AppendPipelineLog($"\n流程失败：{ex.Message}");
            await ShowMessageAsync(ex.Message);
        }
        finally
        {
            progressWindow.CloseFromOwner();
            _cancellation.Dispose();
            _cancellation = null;
            foreach (var button in pipelineStartButtons)
                button.IsEnabled = true;
            _pipelineBlocksBox.IsEnabled = pipelineBlocksBoxWasEnabled;
            UpdateBlockCenterMotionAvailability();
            _pipelineProgress.IsIndeterminate = false;
        }
    }

    private static void ValidateGeneratedLayerArtifacts(
        string dxfPath,
        string previewPath,
        bool expectBlockMetadata)
    {
        static void ValidateRegularNonEmptyFile(string path, string label)
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (!file.Exists ||
                (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                file.Length <= 0)
            {
                throw new InvalidOperationException(
                    $"Hatch 生成结束，但未找到非空普通{label}文件：{path}");
            }
        }

        ValidateRegularNonEmptyFile(dxfPath, " DXF ");
        ValidateRegularNonEmptyFile(previewPath, "预览 PNG ");
        if (expectBlockMetadata)
        {
            ValidateRegularNonEmptyFile(
                Path.ChangeExtension(dxfPath, ".blocks.json"),
                "块元数据");
        }
    }

    private static bool IsRegularNonEmptyFile(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        return file.Exists &&
               (file.Attributes &
                   (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 &&
               file.Length > 0;
    }

    private void UpdateBlockCenterMotionAvailability()
    {
        _pipelineBlockCenterMotionBox.IsEnabled = (_pipelineBlocksBox.Value ?? 0) > 0;
    }

    private static bool TryGetNonNegativeInt(
        NumericUpDown control,
        string label,
        out int value,
        out string error)
    {
        var candidate = control.Value;
        if (!candidate.HasValue ||
            candidate.Value < 0 ||
            candidate.Value > int.MaxValue ||
            decimal.Truncate(candidate.Value) != candidate.Value)
        {
            value = 0;
            error = $"{label} 必须是 0 到 {int.MaxValue} 之间的整数。";
            return false;
        }

        value = decimal.ToInt32(candidate.Value);
        error = "";
        return true;
    }

    private static ProcessStartInfo CreatePythonProcess(string python) => new()
    {
        FileName = python,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    private static async Task<TextureImageInspection> InspectTextureImageAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var python = await FindPythonAsync(cancellationToken);
        if (python is null)
            throw new InvalidOperationException("找不到带有 numpy 和 Pillow 的 Python 3。");

        var info = CreatePythonProcess(python);
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "texture_to_hatch_dxf.py"));
        info.ArgumentList.Add(path);
        info.ArgumentList.Add("--inspect-image");
        info.ArgumentList.Add("--include-preview");

        using var process = new Process { StartInfo = info };
        process.Start();
        var stdoutTask = BoundedTextReader.ReadToEndAsync(
            process.StandardOutput,
            MaximumInspectionStandardOutputCharacters);
        var stderrTask = BoundedTextReader.ReadToEndAsync(
            process.StandardError,
            MaximumInspectionStandardErrorCharacters);
        await WaitForExitOrKillAsync(process, cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask);

        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? $"读取图片信息失败，退出代码：{process.ExitCode}"
                    : stderr.Trim());
        }

        return TextureImageInspection.ParseJson(await stdoutTask);
    }

    private static async Task WaitForExitOrKillAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        await ProcessCancellation.WaitForExitOrTerminateAsync(
            process,
            cancellationToken);
    }

    private static async Task LoadTexturePreviewAsync(
        string path,
        GrayscaleLayerPreviewControl view,
        TextBox dpiBox,
        NumericUpDown widthBox,
        NumericUpDown heightBox,
        TexturePreviewController controller,
        SharedPreviewView sharedPreview)
    {
        var operation = controller.BeginImport();
        sharedPreview.Selection.BeginTextureImport();
        SelectSharedPreview(sharedPreview, SharedPreviewKind.Texture);
        RenderTexturePreview(view, controller.State);

        try
        {
            var inspection = await InspectTextureImageAsync(path, operation.CancellationToken);
            operation.CancellationToken.ThrowIfCancellationRequested();

            // 先解码一次确认预览 PNG 可用且尺寸自洽，再把它交给纹理界面作为第 0 层。
            using (var stream = new MemoryStream(inspection.PreviewPng, writable: false))
            using (var decoded = new Bitmap(stream))
            {
                if (decoded.PixelSize.Width != inspection.Info.PixelWidth ||
                    decoded.PixelSize.Height != inspection.Info.PixelHeight)
                    throw new InvalidOperationException("图片预览像素尺寸与源图片不一致。");
            }

            // 所有权移交给控制器；纹理界面内部会再解码一份自己持有的副本。
            var payload = new TexturePreviewPayload(
                inspection.PreviewPng,
                inspection.Info.PixelWidth,
                inspection.Info.PixelHeight);
            if (!controller.TryCompleteImport(
                    operation,
                    payload,
                    inspection.Info,
                    dpiBox.Text,
                    widthBox.Minimum,
                    widthBox.Maximum,
                    out _))
            {
                return;
            }

            sharedPreview.Selection.CompleteTextureImport();
            SelectSharedPreview(sharedPreview, SharedPreviewKind.Texture);
            RenderTexturePreview(view, controller.State);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            // A newer import or window close owns the visible state.
        }
        catch (Exception ex)
        {
            if (controller.TryFail(operation, ex))
            {
                sharedPreview.Selection.FailTextureImport();
                SelectSharedPreview(sharedPreview, SharedPreviewKind.Texture);
                RenderTexturePreview(view, controller.State);
            }
        }
    }

    private static void ApplyTextureSizeUpdate(
        TexturePreviewSizeUpdate update,
        NumericUpDown widthBox,
        NumericUpDown heightBox)
    {
        if (!update.ShouldWriteTargets)
            return;

        widthBox.Value = update.Width;
        heightBox.Value = update.Height;
    }

    private void DisposeTexturePreviews()
    {
        // 控制器 Close 时会通知纹理界面卸下第 0 层，这里再释放画布与图层资源。
        _hatchPreviewController.Dispose();
        _pipelinePreviewController.Dispose();
        _hatchTextureSurface.Dispose();
        _pipelineTextureSurface.Dispose();
        _hatchDxfPreview.Dispose();
        _pipelineDxfPreview.Dispose();
    }

    private static async Task<int> RunProcessAsync(
        ProcessStartInfo info,
        Action<string> appendLog,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) appendLog(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) appendLog($"错误：{e.Data}"); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await ProcessCancellation.WaitForExitOrTerminateAsync(
            process,
            cancellationToken);
        return process.ExitCode;
    }

    private async Task PickHatchInputAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择黑白纹理图",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图像文件")
                {
                    Patterns = ["*.tif", "*.tiff", "*.png", "*.jpg", "*.jpeg", "*.bmp"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        _hatchInputBox.Text = path;
        if (string.IsNullOrWhiteSpace(_hatchOutputBox.Text))
            _hatchOutputBox.Text = Path.Combine(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}_hatch.dxf");

        await LoadTexturePreviewAsync(
            path,
            _hatchTextureSurface,
            _dpiBox,
            _widthBox,
            _heightBox,
            _hatchPreviewController,
            _hatchSharedPreview);
    }

    private async Task PickHatchOutputAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存 Hatch DXF",
            DefaultExtension = "dxf",
            SuggestedFileName = string.IsNullOrWhiteSpace(_hatchInputBox.Text)
                ? "texture_hatch.dxf"
                : $"{Path.GetFileNameWithoutExtension(_hatchInputBox.Text)}_hatch.dxf",
            FileTypeChoices =
            [
                new FilePickerFileType("DXF 文件") { Patterns = ["*.dxf"] }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            _hatchOutputBox.Text = path;
    }

    private async Task RunHatchAsync()
    {
        var input = _hatchInputBox.Text?.Trim();
        var output = _hatchOutputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            await ShowMessageAsync("请先选择有效的输入纹理图。");
            return;
        }
        if (string.IsNullOrWhiteSpace(output))
        {
            await ShowMessageAsync("请先选择 DXF 输出位置。");
            return;
        }

        // 如果只输入了文件名，自动补到输入图片所在目录；如果是完整路径则直接取绝对路径。
        if (!Path.IsPathRooted(output)
            && !output.Contains(Path.DirectorySeparatorChar)
            && !output.Contains(Path.AltDirectorySeparatorChar))
        {
            output = Path.Combine(Path.GetDirectoryName(input)!, output);
        }
        output = Path.GetFullPath(output);
        _hatchOutputBox.Text = output;

        var script = ApplicationLayout.GetScriptPath(
            AppContext.BaseDirectory, "texture_to_hatch_dxf.py");
        if (!File.Exists(script))
        {
            await ShowMessageAsync($"找不到 Python 脚本：\n{script}");
            return;
        }

        var python = await FindPythonAsync();
        if (python is null)
        {
            await ShowMessageAsync("找不到带有 numpy 和 Pillow 的 Python 3。");
            return;
        }

        if (!TextureFallbackDpi.TryParseOptional(_dpiBox.Text, out var dpi))
        {
            await ShowMessageAsync("DPI 必须留空或填写有限且大于 0 的数字。");
            return;
        }

        _cancellation = new CancellationTokenSource();
        _hatchRunButton.IsEnabled = false;
        _hatchOpenButton.IsEnabled = false;
        _hatchProgress.IsIndeterminate = true;
        _hatchLogBox.Text = "";
        _hatchDxfPreview.Clear();
        _hatchDxfPreviewStatus.Text = _hatchDxfPreview.Summary;
        _hatchSharedPreview.UpdateDxfOverlayControls();
        _hatchSharedPreview.Selection.ClearDxf();

        var width = _widthBox.Value ?? 100;
        var height = _heightBox.Value ?? 100;
        var spacing = _spacingBox.Value ?? 0.02m;
        if (!TryValidateVoronoiSettings(
                _blocksBox,
                _minBlockPercentBox,
                _maxBlockPercentBox,
                _boundaryCorrelationBox,
                out var voronoiError))
        {
            await ShowMessageAsync(voronoiError);
            return;
        }
        AppendHatchLog($"Python：{python}");
        AppendHatchLog($"输入：{input}");
        AppendHatchLog($"输出：{output}\n");

        try
        {
            var outputDirectory = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var info = new ProcessStartInfo
            {
                FileName = python,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
            {
                script, input, output,
                "--width", Invariant(width),
                "--height", Invariant(height),
                "--spacing", Invariant(spacing),
                "--anchor", _anchorBox.SelectedIndex == 1 ? "top-left" : "center"
            })
                info.ArgumentList.Add(argument);
            if (dpi.HasValue)
            {
                info.ArgumentList.Add("--dpi");
                info.ArgumentList.Add(dpi.Value.ToString(CultureInfo.InvariantCulture));
            }
            if (_includeBorder.IsChecked == true)
                info.ArgumentList.Add("--border");
            if (_bidirectionalHatch.IsChecked == true)
                info.ArgumentList.Add("--bidirectional");
            AddVoronoiArguments(
                info,
                width,
                height,
                _blocksBox,
                _minBlockPercentBox,
                _maxBlockPercentBox,
                _boundaryBlurBox,
                _boundaryCorrelationBox,
                _voronoiSeedBox);

            using var process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendHatchLog(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendHatchLog($"错误：{e.Data}"); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await ProcessCancellation.WaitForExitOrTerminateAsync(
                process,
                _cancellation.Token);

            if (process.ExitCode == 0)
            {
                AppendHatchLog("\nDXF 生成完成。");
                _hatchOpenButton.IsEnabled = true;
                if (LoadDxfPreview(_hatchDxfPreview, _hatchDxfPreviewStatus, output))
                {
                    _hatchSharedPreview.UpdateDxfOverlayControls();
                    _hatchSharedPreview.Selection.CompleteDxfLoad();
                    SelectSharedPreview(_hatchSharedPreview, SharedPreviewKind.Dxf);
                }
                else
                {
                    _hatchSharedPreview.UpdateDxfOverlayControls();
                }
            }
            else
            {
                AppendHatchLog($"\n生成失败，退出代码：{process.ExitCode}");
                await ShowMessageAsync("DXF 生成失败，请查看运行日志。");
            }
        }
        catch (OperationCanceledException)
        {
            AppendHatchLog("\n操作已取消。");
        }
        catch (Exception ex)
        {
            AppendHatchLog($"\n发生异常：{ex.Message}");
            await ShowMessageAsync(ex.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _hatchRunButton.IsEnabled = true;
            _hatchProgress.IsIndeterminate = false;
        }
    }

    // 上下限联动：始终保留至少一级灰阶差，避免出现下限 ≥ 上限的无效区间。
    private static void LinkGrayLevelBounds(NumericUpDown lowerBox, NumericUpDown upperBox)
    {
        lowerBox.ValueChanged += (_, _) =>
        {
            var lower = (int)(lowerBox.Value ?? GrayLevelRange.Minimum);
            var upper = (int)(upperBox.Value ?? GrayLevelRange.Maximum);
            var corrected = GrayLevelRange.EnsureUpperAbove(lower, upper);
            if (corrected != upper)
                upperBox.Value = corrected;
        };
        upperBox.ValueChanged += (_, _) =>
        {
            var lower = (int)(lowerBox.Value ?? GrayLevelRange.Minimum);
            var upper = (int)(upperBox.Value ?? GrayLevelRange.Maximum);
            var corrected = GrayLevelRange.EnsureLowerBelow(lower, upper);
            if (corrected != lower)
                lowerBox.Value = corrected;
        };
    }

    private static bool TryReadGrayLevelRange(
        NumericUpDown lowerBox,
        NumericUpDown upperBox,
        int layers,
        out int lower,
        out int upper,
        out string error)
    {
        lower = (int)(lowerBox.Value ?? GrayLevelRange.Minimum);
        upper = (int)(upperBox.Value ?? GrayLevelRange.Maximum);
        return GrayLevelRange.TryValidate(lower, upper, layers, out error);
    }

    private static bool TryValidateVoronoiSettings(
        NumericUpDown blocksBox,
        NumericUpDown minimumPercentBox,
        NumericUpDown maximumPercentBox,
        NumericUpDown correlationBox,
        out string error)
    {
        var blocks = (int)(blocksBox.Value ?? 0);
        var minimum = minimumPercentBox.Value ?? 0;
        var maximum = maximumPercentBox.Value ?? 100;
        var correlation = correlationBox.Value ?? 0;
        if (blocks == 1)
        {
            error = "加工块数量应为 0（关闭）或至少为 2。";
            return false;
        }
        if (blocks > 0 &&
            (minimum >= maximum || minimum * blocks > 100 || maximum * blocks < 100))
        {
            error = $"{blocks} 块的面积约束不可行；所有块的面积总和必须恰好为 100%。";
            return false;
        }
        if (correlation <= 0)
        {
            error = "边界连续变化长度必须大于 0。";
            return false;
        }
        error = "";
        return true;
    }

    private static void AddVoronoiArguments(
        ProcessStartInfo info,
        decimal width,
        decimal height,
        NumericUpDown blocksBox,
        NumericUpDown minimumPercentBox,
        NumericUpDown maximumPercentBox,
        NumericUpDown blurBox,
        NumericUpDown correlationBox,
        NumericUpDown seedBox,
        int? seedOverride = null)
    {
        var blocks = (int)(blocksBox.Value ?? 0);
        var totalArea = width * height;
        var minimumArea = totalArea * (minimumPercentBox.Value ?? 0) / 100;
        var maximumArea = totalArea * (maximumPercentBox.Value ?? 100) / 100;
        foreach (var argument in new[]
        {
            "--blocks", blocks.ToString(CultureInfo.InvariantCulture),
            "--min-block-area", Invariant(minimumArea),
            "--max-block-area", Invariant(maximumArea),
            "--boundary-blur", Invariant(blurBox.Value ?? 0),
            "--boundary-correlation", Invariant(correlationBox.Value ?? 1),
            "--seed", (seedOverride ?? (int)(seedBox.Value ?? 12345))
                .ToString(CultureInfo.InvariantCulture)
        })
            info.ArgumentList.Add(argument);
    }

    private static string Invariant(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private async Task RunAsync()
    {
        var input = _inputBox.Text?.Trim();
        var output = _outputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            await ShowMessageAsync("请先选择有效的输入图片。");
            return;
        }
        if (string.IsNullOrWhiteSpace(output))
        {
            await ShowMessageAsync("请先选择输出目录。");
            return;
        }

        var layers = (int)(_layersBox.Value ?? 10);
        if (!TryReadGrayLevelRange(
                _minLevelBox,
                _maxLevelBox,
                layers,
                out var minLevel,
                out var maxLevel,
                out var rangeError))
        {
            await ShowMessageAsync(rangeError);
            return;
        }

        var script = ApplicationLayout.GetScriptPath(
            AppContext.BaseDirectory, "grayscale_layers.py");
        if (!File.Exists(script))
        {
            await ShowMessageAsync($"找不到 Python 脚本：\n{script}");
            return;
        }

        var python = await FindPythonAsync();
        if (python is null)
        {
            await ShowMessageAsync("找不到 Python 3。请先安装 Python 3，并确保 python3 可从终端运行。");
            return;
        }

        _cancellation = new CancellationTokenSource();
        _runButton.IsEnabled = false;
        _openOutputButton.IsEnabled = false;
        _progress.IsIndeterminate = true;
        _logBox.Text = "";
        AppendLog($"Python：{python}");
        AppendLog($"输入：{input}");
        AppendLog($"输出：{output}");
        AppendLog($"灰阶区间：[{minLevel}, {maxLevel}]，分层数量：{layers}\n");

        try
        {
            Directory.CreateDirectory(output);
            var info = new ProcessStartInfo
            {
                FileName = python,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            info.ArgumentList.Add(script);
            info.ArgumentList.Add(input);
            info.ArgumentList.Add(output);
            info.ArgumentList.Add("--layers");
            info.ArgumentList.Add(layers.ToString(CultureInfo.InvariantCulture));
            GrayLevelRange.AppendArguments(info.ArgumentList, minLevel, maxLevel);
            if (_belowIsWhite.IsChecked == true)
                info.ArgumentList.Add("--below-is-white");

            using var process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog($"错误：{e.Data}"); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await ProcessCancellation.WaitForExitOrTerminateAsync(
                process,
                _cancellation.Token);

            if (process.ExitCode == 0)
            {
                AppendLog("\n处理完成。");
                _openOutputButton.IsEnabled = true;
            }
            else
            {
                AppendLog($"\n处理失败，退出代码：{process.ExitCode}");
                await ShowMessageAsync("处理失败，请查看运行日志。");
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("\n操作已取消。");
        }
        catch (Exception ex)
        {
            AppendLog($"\n发生异常：{ex.Message}");
            await ShowMessageAsync(ex.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _runButton.IsEnabled = true;
            _progress.IsIndeterminate = false;
        }
    }

    private static async Task<string?> FindPythonAsync(
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in new[] { "/opt/homebrew/bin/python3", "/usr/local/bin/python3", "/usr/bin/python3", "python3" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add("import numpy, PIL");
                using var process = Process.Start(info);
                if (process is null) continue;
                await WaitForExitOrKillAsync(process, cancellationToken);
                if (process.ExitCode == 0) return candidate;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Try the next common Python location.
            }
        }
        return null;
    }

    private void AppendLog(string text) =>
        Dispatcher.UIThread.Post(() =>
        {
            _logBox.Text += text + Environment.NewLine;
            _logBox.CaretIndex = _logBox.Text?.Length ?? 0;
        });

    private static bool LoadDxfPreview(
        DxfPreviewControl preview,
        TextBlock status,
        string path)
    {
        try
        {
            preview.LoadFile(path);
            status.Text = preview.Summary;
            status.ClearValue(TextBlock.ForegroundProperty);
            return true;
        }
        catch (Exception ex)
        {
            status.Text = $"无法预览 {Path.GetFileName(path)}：{ex.Message}";
            status.Foreground = Brushes.OrangeRed;
            return false;
        }
    }

    private async Task ImportDxfPreviewAsync(
        DxfPreviewControl preview,
        TextBlock status,
        bool addToPipelineSelector,
        SharedPreviewView sharedPreview)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入 DXF 文件进行预览",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("DXF 文件") { Patterns = ["*.dxf"] }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (addToPipelineSelector)
        {
            var item = DxfLayerPreviewItem.Imported(path);
            _pipelineDxfFiles.Add(item);
            _pipelineDxfSelector.SelectedItem = item;
        }
        else
        {
            preview.ClearTexture();
            sharedPreview.UpdateDxfOverlayControls();
            if (LoadDxfPreview(preview, status, path))
            {
                sharedPreview.UpdateDxfOverlayControls();
                sharedPreview.Selection.CompleteDxfLoad();
                SelectSharedPreview(sharedPreview, SharedPreviewKind.Dxf);
            }
            else
            {
                sharedPreview.UpdateDxfOverlayControls();
            }
        }
    }

    private void AppendHatchLog(string text) =>
        Dispatcher.UIThread.Post(() =>
        {
            _hatchLogBox.Text += text + Environment.NewLine;
            _hatchLogBox.CaretIndex = _hatchLogBox.Text?.Length ?? 0;
        });

    private void AppendPipelineLog(string text) =>
        Dispatcher.UIThread.Post(() =>
        {
            _pipelineLogBox.Text += text + Environment.NewLine;
            _pipelineLogBox.CaretIndex = _pipelineLogBox.Text?.Length ?? 0;
        });

    private void OpenOutputDirectory()
    {
        var path = _outputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        var info = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
        info.ArgumentList.Add(path);
        Process.Start(info);
    }

    private void OpenHatchOutput()
    {
        var path = _hatchOutputBox.Text?.Trim();
        var directory = string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;
        var info = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
        info.ArgumentList.Add(directory);
        Process.Start(info);
    }

    private static void OpenDirectory(string? path)
    {
        path = path?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        var info = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
        info.ArgumentList.Add(path);
        Process.Start(info);
    }

    private async Task ShowMessageAsync(string message)
    {
        var ok = new Button { Content = "确定", HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 90 };
        var dialog = new Window
        {
            Title = "提示",
            Width = 420,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 22,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok
                }
            }
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
}
