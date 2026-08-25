using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace GrayscaleLayersMac;

public sealed class MainWindow : Window
{
    private sealed record DxfPreviewItem(string Name, string Path)
    {
        public override string ToString() => Name;
    }

    private sealed record TexturePreviewView(
        Image Preview,
        TextBlock Metadata,
        TextBlock PhysicalSize);

    private readonly TextBox _inputBox = new() { Watermark = "请选择一张灰度纹理图", IsReadOnly = true };
    private readonly TextBox _outputBox = new() { Watermark = "请选择结果保存目录", IsReadOnly = true };
    private readonly NumericUpDown _layersBox = new()
    {
        Minimum = 1, Maximum = 255, Value = 10, Increment = 1,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly CheckBox _belowIsWhite = new()
    {
        Content = "低于阈值的区域设为白色（默认设为黑色）"
    };
    private readonly TextBox _logBox = UiTheme.CreateLogBox(190);
    private readonly Button _runButton = new() { Content = "开始处理", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _openOutputButton = new() { Content = "打开输出目录", IsEnabled = false };
    private readonly ProgressBar _progress = UiTheme.CreateProgress();
    private readonly TextBox _hatchInputBox = new() { Watermark = "请选择一张黑白纹理图", IsReadOnly = true };
    private readonly TextBox _hatchOutputBox = new() { Watermark = "请选择 DXF 保存位置", IsReadOnly = true };
    private readonly NumericUpDown _widthBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _heightBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _spacingBox = MakeNumberBox(0.02m, 0.001m, 1000);
    private readonly NumericUpDown _thresholdBox = MakeNumberBox(128, 1, 255, 0);
    private readonly TextBox _dpiBox = new() { Watermark = "可选；图片无 DPI 时填写" };
    private readonly Image _hatchTexturePreview = new()
    {
        Height = 190,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _hatchTextureMetadata = new()
    {
        Text = "尚未选择图片",
        Foreground = UiTheme.TextSecondaryBrush
    };
    private readonly TextBlock _hatchTexturePhysicalSize = new()
    {
        Text = "物理尺寸：等待读取图片信息",
        Foreground = UiTheme.TextSecondaryBrush
    };
    private readonly ComboBox _anchorBox = new()
    {
        ItemsSource = new[] { "居中裁剪", "左上角裁剪" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly CheckBox _includeBorder = new() { Content = "在 DXF 中写入加工区域边框" };
    private readonly CheckBox _bidirectionalHatch = new() { Content = "往返填充 Hatch（相邻行方向交替）" };
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
    private readonly NumericUpDown _pipelineLayersBox = MakeNumberBox(10, 1, 255, 0);
    private readonly CheckBox _pipelineBelowIsWhite = new() { Content = "低于阈值的区域设为白色（默认设为黑色）" };
    private readonly NumericUpDown _pipelineWidthBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _pipelineHeightBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _pipelineSpacingBox = MakeNumberBox(0.02m, 0.001m, 1000);
    private readonly NumericUpDown _pipelineHatchAngleStepBox = MakeNumberBox(0, 0.1m, 180, 2, showButtons: false);
    private readonly NumericUpDown _pipelineThresholdBox = MakeNumberBox(128, 1, 255, 0);
    private readonly TextBox _pipelineDpiBox = new() { Watermark = "可选；图片无 DPI 时填写" };
    private readonly Image _pipelineTexturePreview = new()
    {
        Height = 190,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _pipelineTextureMetadata = new()
    {
        Text = "尚未选择图片",
        Foreground = UiTheme.TextSecondaryBrush
    };
    private readonly TextBlock _pipelineTexturePhysicalSize = new()
    {
        Text = "物理尺寸：等待读取图片信息",
        Foreground = UiTheme.TextSecondaryBrush
    };
    private readonly ComboBox _pipelineAnchorBox = new()
    {
        ItemsSource = new[] { "居中裁剪", "左上角裁剪" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly CheckBox _pipelineIncludeBorder = new() { Content = "在 DXF 中写入加工区域边框" };
    private readonly CheckBox _pipelineBidirectionalHatch = new() { Content = "往返填充 Hatch（相邻行方向交替）" };
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
    private readonly DxfPreviewControl _pipelineDxfPreview = new();
    private readonly TextBlock _pipelineDxfPreviewStatus = new() { Foreground = UiTheme.TextSecondaryBrush };
    private readonly ObservableCollection<DxfPreviewItem> _pipelineDxfFiles = [];
    private readonly ComboBox _pipelineDxfSelector = new()
    {
        MinWidth = 240,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        PlaceholderText = "生成后选择要预览的层"
    };
    private readonly TextBox _pipelineLogBox = MakeLogBox();
    private readonly Button _pipelineRunButton = new() { Content = "开始三步处理", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _pipelineOpenButton = new() { Content = "打开加工文件目录", IsEnabled = false };
    private readonly ProgressBar _pipelineProgress = UiTheme.CreateProgress();
    private readonly TexturePreviewView _hatchTextureView;
    private readonly TexturePreviewView _pipelineTextureView;
    private readonly TexturePreviewController _hatchPreviewController;
    private readonly TexturePreviewController _pipelinePreviewController;
    private string? _lastMachineOutputPath;
    private CancellationTokenSource? _cancellation;

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
        _hatchTextureView = new TexturePreviewView(
            _hatchTexturePreview,
            _hatchTextureMetadata,
            _hatchTexturePhysicalSize);
        _pipelineTextureView = new TexturePreviewView(
            _pipelineTexturePreview,
            _pipelineTextureMetadata,
            _pipelineTexturePhysicalSize);
        _hatchPreviewController = new TexturePreviewController(
            source => _hatchTextureView.Preview.Source = source as Bitmap,
            update => ApplyTextureSizeUpdate(update, _widthBox, _heightBox));
        _pipelinePreviewController = new TexturePreviewController(
            source => _pipelineTextureView.Preview.Source = source as Bitmap,
            update => ApplyTextureSizeUpdate(
                update,
                _pipelineWidthBox,
                _pipelineHeightBox));
        foreach (var primaryButton in new[] { _pipelineRunButton, _hatchRunButton, _runButton })
            UiTheme.ApplyPrimaryStyle(primaryButton);
        _pipelineDxfSelector.ItemsSource = _pipelineDxfFiles;
        _pipelineDxfSelector.SelectionChanged += (_, _) =>
        {
            if (_pipelineDxfSelector.SelectedItem is DxfPreviewItem item)
                LoadDxfPreview(
                    _pipelineDxfPreview,
                    _pipelineDxfPreviewStatus,
                    item.Path);
        };
        _dpiBox.TextChanged += (_, _) =>
        {
            _hatchPreviewController.ApplyFallbackDpiEdit(
                _dpiBox.Text,
                _widthBox.Minimum,
                _widthBox.Maximum);
            RenderTexturePreview(_hatchTextureView, _hatchPreviewController.State);
        };
        _pipelineDpiBox.TextChanged += (_, _) =>
        {
            _pipelinePreviewController.ApplyFallbackDpiEdit(
                _pipelineDpiBox.Text,
                _pipelineWidthBox.Minimum,
                _pipelineWidthBox.Maximum);
            RenderTexturePreview(_pipelineTextureView, _pipelinePreviewController.State);
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

        var layerContent = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                UiTheme.PageTitle("灰度图分层"),
                UiTheme.PageSubtitle("将灰度纹理图按累计阈值生成多张黑白 TIFF 图像。"),
                MakeField("输入图片", _inputBox, inputButton),
                MakeField("输出目录", _outputBox, outputButton),
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("180,*"),
                    ColumnSpacing = 16,
                    Children =
                    {
                        MakeLabeledControl("分层数量（1–255）", _layersBox, 0),
                        MakeLabeledControl("像素方向", _belowIsWhite, 1)
                    }
                },
                _progress,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        Place(_runButton, 0),
                        Place(cancelButton, 1),
                        Place(_openOutputButton, 2)
                    }
                },
                UiTheme.PanelLabel("运行日志"),
                _logBox
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
                addToPipelineSelector: false);

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
                MakeTexturePreviewCard(_hatchTextureView),
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
                            MakeLabeledControl("黑色阈值（0–255）", _thresholdBox, 0),
                            MakeLabeledControl("设置 DPI", _dpiBox, 1),
                            MakeLabeledControl("单元阵列对齐", _anchorBox, 2)
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
        var hatchContent = MakeWorkspace(
            hatchInspector,
            MakeDxfPreviewPanel(
                _hatchDxfPreview,
                _hatchDxfPreviewStatus,
                hatchImportDxfButton),
            _hatchLogBox,
            "运行日志");

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
                await RunPipelineAsync();
                pipelineCancelButton.IsEnabled = false;
            }
        };
        pipelineCancelButton.Click += (_, _) => _cancellation?.Cancel();
        _pipelineOpenButton.Click += (_, _) => OpenDirectory(_lastMachineOutputPath);
        pipelineImportDxfButton.Click += async (_, _) =>
            await ImportDxfPreviewAsync(
                _pipelineDxfPreview,
                _pipelineDxfPreviewStatus,
                addToPipelineSelector: true);
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
                        ColumnDefinitions = new ColumnDefinitions("180,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("分层数量（1–255）", _pipelineLayersBox, 0),
                            MakeLabeledControl("像素方向", _pipelineBelowIsWhite, 1)
                        }
                    }),
                MakeTexturePreviewCard(_pipelineTextureView),
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
                        ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("黑色阈值（0–255）", _pipelineThresholdBox, 0),
                            MakeLabeledControl("设置 DPI", _pipelineDpiBox, 1),
                            MakeLabeledControl("单元阵列对齐", _pipelineAnchorBox, 2),
                            MakeLabeledControl("层间角度递进（°）", _pipelineHatchAngleStepBox, 3)
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
                            FontSize = 13,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = UiTheme.TextPrimaryBrush
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
                        Place(_pipelineRunButton, 0),
                        Place(pipelineCancelButton, 1),
                        Place(_pipelineOpenButton, 2)
                    }
                }
            }
        };
        var pipelineContent = MakeWorkspace(
            pipelineInspector,
            MakeDxfPreviewPanel(
                _pipelineDxfPreview,
                _pipelineDxfPreviewStatus,
                pipelineImportDxfButton,
                _pipelineDxfSelector),
            _pipelineLogBox,
            "流程日志");

        foreach (var secondaryButton in new[]
        {
            inputButton, outputButton, cancelButton,
            hatchInputButton, hatchOutputButton, hatchCancelButton, hatchImportDxfButton,
            pipelineInputButton, pipelineLayerOutputButton, pipelineDxfOutputButton,
            pipelineCancelButton, pipelineImportDxfButton,
            _openOutputButton, _hatchOpenButton, _pipelineOpenButton
        })
            UiTheme.ApplyGhostStyle(secondaryButton);

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
                            MaxWidth = 920,
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
            Padding = new Thickness(22, 10),
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = UiTheme.HeaderBrush,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 12,
                Children =
                {
                    Place(new Image
                    {
                        Source = new Bitmap(
                            AssetLoader.Open(
                                new Uri("avares://GrayscaleLayersMac/Assets/AppIcon.png"))),
                        Width = 36,
                        Height = 36
                    }, 0),
                    Place(new StackPanel
                    {
                        Spacing = 1,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "纹理预处理工作台",
                                FontSize = 17,
                                FontWeight = FontWeight.SemiBold
                            },
                            new TextBlock
                            {
                                Text = "GRAYSCALE · HATCH · DXF",
                                FontSize = 10,
                                Foreground = UiTheme.TextFaintBrush,
                                LetterSpacing = 1.5
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
        var content = new StackPanel { Spacing = 14 };
        foreach (var control in controls)
            content.Children.Add(control);
        return UiTheme.CardExpander(title, content);
    }

    private static Control MakeTexturePreviewCard(TexturePreviewView view) => new Border
    {
        Padding = new Thickness(14),
        Background = UiTheme.CardBrush,
        BorderBrush = UiTheme.BorderSubtleBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = UiTheme.CardRadius,
        Child = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        UiTheme.AccentBar(),
                        new TextBlock
                        {
                            Text = "纹理预览",
                            FontSize = 13,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = UiTheme.TextPrimaryBrush,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                new Border
                {
                    Padding = new Thickness(8),
                    Background = UiTheme.SunkenBrush,
                    BorderBrush = UiTheme.BorderSubtleBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = UiTheme.ControlRadius,
                    ClipToBounds = true,
                    Child = view.Preview
                },
                view.Metadata,
                view.PhysicalSize
            }
        }
    };

    private static Control MakeDxfPreviewPanel(
        DxfPreviewControl preview,
        TextBlock status,
        Button importButton,
        ComboBox? fileSelector = null)
    {
        var fitButton = new Button { Content = "适应窗口" };
        fitButton.Click += (_, _) => preview.FitToView();
        var topButton = new Button { Content = "顶视图" };
        topButton.Click += (_, _) => preview.SetTopView();
        var isometricButton = new Button { Content = "等轴测" };
        isometricButton.Click += (_, _) => preview.SetIsometricView();
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
            Margin = new Thickness(0, 12, 12, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 10,
            Children =
            {
                AtRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
                    ColumnSpacing = 10,
                    Children =
                    {
                        Place(new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                UiTheme.AccentBar(),
                                new TextBlock
                                {
                                    Text = "DXF 预览",
                                    FontSize = 16,
                                    FontWeight = FontWeight.SemiBold,
                                    Foreground = UiTheme.TextPrimaryBrush,
                                    VerticalAlignment = VerticalAlignment.Center
                                }
                            }
                        }, 0),
                        Place(importButton, 1),
                        Place(topButton, 2),
                        Place(isometricButton, 3),
                        Place(fitButton, 4)
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

    private static Control MakeWorkspace(
        StackPanel inspector,
        Control previewPanel,
        TextBox log,
        string logTitle)
    {
        var actionRow = inspector.Children[^1];
        inspector.Children.RemoveAt(inspector.Children.Count - 1);
        var progress = inspector.Children[^1];
        inspector.Children.RemoveAt(inspector.Children.Count - 1);
        inspector.Margin = new Thickness(18, 16, 18, 16);
        inspector.Spacing = 14;
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
        Grid.SetColumn(inspectorSurface, 1);
        Grid.SetRowSpan(inspectorSurface, 2);

        var logSurface = new Border
        {
            Margin = new Thickness(0, 0, 12, 0),
            Padding = new Thickness(14, 12),
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = UiTheme.CardRadius,
            Background = UiTheme.PanelBrush,
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 8,
                Children =
                {
                    AtRow(UiTheme.PanelLabel(logTitle), 0),
                    AtRow(log, 1)
                }
            }
        };
        Grid.SetRow(logSurface, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,510"),
            RowDefinitions = new RowDefinitions("*,210"),
            ColumnSpacing = 0,
            RowSpacing = 12,
            Children =
            {
                previewPanel,
                logSurface,
                inspectorSurface
            }
        };
    }

    private static Control MakeField(string label, Control field, Button button)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        grid.Children.Add(Place(field, 0));
        grid.Children.Add(Place(button, 1));
        return new StackPanel
        {
            Spacing = 7,
            Children = { UiTheme.FieldLabel(label), grid }
        };
    }

    private static Control MakeLabeledControl(string label, Control control, int column)
    {
        var panel = new StackPanel
        {
            Spacing = 7,
            Children = { UiTheme.FieldLabel(label), control }
        };
        Grid.SetColumn(panel, column);
        return panel;
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
            _pipelineTextureView,
            _pipelineDpiBox,
            _pipelineWidthBox,
            _pipelineHeightBox,
            _pipelinePreviewController);
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

    private async Task RunPipelineAsync()
    {
        var input = _pipelineInputBox.Text?.Trim();
        var layerOutput = _pipelineLayerOutputBox.Text?.Trim();
        var dxfOutput = _pipelineDxfOutputBox.Text?.Trim();
        var machineName = _pipelineMachineNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(machineName))
        {
            machineName = $"machine_file_{DateTime.Now:yyyyMMdd_HHmmss}";
            _pipelineMachineNameBox.Text = machineName;
        }

        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            await ShowMessageAsync("请先选择有效的原始灰度图。");
            return;
        }
        if (string.IsNullOrWhiteSpace(layerOutput) || string.IsNullOrWhiteSpace(dxfOutput))
        {
            await ShowMessageAsync("请同时选择分层 TIFF 和 DXF 的输出目录。");
            return;
        }
        if (machineName is "." or ".." || machineName.Contains('/') || machineName.Contains('\\'))
        {
            await ShowMessageAsync("加工文件名不能是“.”或“..”，且不能包含 / 或 \\。");
            return;
        }

        var layerStep = _pipelineLayerStepBox.Value;
        if (
            !layerStep.HasValue ||
            layerStep.Value < 1m ||
            layerStep.Value > 100000m ||
            layerStep.Value != decimal.Truncate(layerStep.Value))
        {
            await ShowMessageAsync(
                "每层下降深度必须是 1–100000 μm 的整数，才能与 0.001 mm 的机器坐标精度一致。");
            return;
        }

        var layerScript = Path.Combine(AppContext.BaseDirectory, "grayscale_layers.py");
        var hatchScript = Path.Combine(AppContext.BaseDirectory, "texture_to_hatch_dxf.py");
        var machineScript = Path.Combine(AppContext.BaseDirectory, "dxf_to_machine_file.py");
        if (!File.Exists(layerScript) || !File.Exists(hatchScript) || !File.Exists(machineScript))
        {
            await ShowMessageAsync(
                "找不到流程所需的 Python 脚本（grayscale_layers.py、texture_to_hatch_dxf.py、" +
                "dxf_to_machine_file.py），请重新编译或发布应用。");
            return;
        }

        var python = await FindPythonAsync();
        if (python is null)
        {
            await ShowMessageAsync("找不到带有 numpy 和 Pillow 的 Python 3。");
            return;
        }
        if (!TextureFallbackDpi.TryParseOptional(_pipelineDpiBox.Text, out var dpi))
        {
            await ShowMessageAsync("DPI 必须留空或填写有限且大于 0 的数字。");
            return;
        }

        var layers = (int)(_pipelineLayersBox.Value ?? 10);
        var width = _pipelineWidthBox.Value ?? 100;
        var height = _pipelineHeightBox.Value ?? 100;
        var spacing = _pipelineSpacingBox.Value ?? 0.02m;
        var hatchAngleStep = _pipelineHatchAngleStepBox.Value ?? 0;
        var threshold = (int)(_pipelineThresholdBox.Value ?? 128);
        if (!TryValidateVoronoiSettings(
                _pipelineBlocksBox,
                _pipelineMinBlockPercentBox,
                _pipelineMaxBlockPercentBox,
                _pipelineBoundaryCorrelationBox,
                out var voronoiError))
        {
            await ShowMessageAsync(voronoiError);
            return;
        }

        if (!TryGetNonNegativeInt(_pipelinePowerBox, "功率（power）", out var power, out var laserError) ||
            !TryGetNonNegativeInt(_pipelineFrequencyBox, "频率（frequency）", out var frequency, out laserError) ||
            !TryGetNonNegativeInt(_pipelinePulseWidthIdxBox, "脉宽索引（pulseWidthIdx）", out var pulseWidthIdx, out laserError) ||
            !TryGetNonNegativeInt(_pipelineScanSpeedBox, "扫描速度（scanSpeed）", out var scanSpeed, out laserError) ||
            !TryGetNonNegativeInt(_pipelineJumpVelocityBox, "跳转速度（jump_vel）", out var jumpVelocity, out laserError) ||
            !TryGetNonNegativeInt(_pipelineJumpDelayBox, "跳转延迟（jump_delay）", out var jumpDelay, out laserError) ||
            !TryGetNonNegativeInt(_pipelineAccScaleBox, "加速度比例（accScale）", out var accScale, out laserError) ||
            !TryGetNonNegativeInt(_pipelineCornerScaleBox, "转角比例（cornerScale）", out var cornerScale, out laserError) ||
            !TryGetNonNegativeInt(_pipelineEndScaleBox, "结束比例（endScale）", out var endScale, out laserError) ||
            !TryGetNonNegativeInt(_pipelineTimeLagBox, "时间滞后（timeLag）", out var timeLag, out laserError) ||
            !TryGetNonNegativeInt(_pipelineLaserOnShiftBox, "开光偏移（laserOnShift）", out var laserOnShift, out laserError) ||
            !TryGetNonNegativeInt(_pipelineDelayLaserOffBox, "关光延迟（delaseroff）", out var delayLaserOff, out laserError) ||
            !TryGetNonNegativeInt(_pipelineDelayLaserOnBox, "开光延迟（delaseron）", out var delayLaserOn, out laserError))
        {
            await ShowMessageAsync(laserError);
            return;
        }

        string dxfOutputAbsolute;
        string machineOutputPath;
        string machineTempPath;
        string machineLockPath;
        try
        {
            dxfOutputAbsolute = Path.GetFullPath(dxfOutput);
            var dxfParent = new DirectoryInfo(dxfOutputAbsolute).Parent?.FullName;
            if (string.IsNullOrWhiteSpace(dxfParent))
            {
                await ShowMessageAsync("DXF 输出目录必须有可用的父目录。");
                return;
            }
            machineOutputPath = Path.Combine(dxfParent, machineName);
            machineTempPath = Path.Combine(dxfParent, $".{machineName}.building");
            machineLockPath = Path.Combine(dxfParent, $".{machineName}.lock");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await ShowMessageAsync($"无法解析加工文件输出路径：{ex.Message}");
            return;
        }

        _lastMachineOutputPath = null;
        _pipelineOpenButton.IsEnabled = false;
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(machineOutputPath),
                Path.TrimEndingDirectorySeparator(dxfOutputAbsolute),
                StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync("加工文件名不能与 DXF 输出目录同名。");
            return;
        }

        foreach (var collisionPath in new[] { machineOutputPath, machineTempPath, machineLockPath })
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
        _pipelineDxfFiles.Clear();

        try
        {
            Directory.CreateDirectory(layerOutput);
            Directory.CreateDirectory(dxfOutput);
            AppendPipelineLog("步骤 1/3：开始生成灰度分层 TIFF…");
            AppendPipelineLog($"输入：{input}");
            AppendPipelineLog($"分层目录：{layerOutput}\n");

            var layerStartedAt = DateTime.UtcNow.AddSeconds(-2);
            var layerInfo = CreatePythonProcess(python);
            foreach (var argument in new[]
            {
                layerScript, input, layerOutput,
                "--layers", layers.ToString(CultureInfo.InvariantCulture)
            })
                layerInfo.ArgumentList.Add(argument);
            if (_pipelineBelowIsWhite.IsChecked == true)
                layerInfo.ArgumentList.Add("--below-is-white");

            var layerExitCode = await RunProcessAsync(
                layerInfo,
                AppendPipelineLog,
                _cancellation.Token);
            if (layerExitCode != 0)
                throw new InvalidOperationException($"灰度分层失败，退出代码：{layerExitCode}");

            var layerFiles = Directory
                .EnumerateFiles(layerOutput, "layer_*.tiff")
                .Where(path => File.GetLastWriteTimeUtc(path) >= layerStartedAt)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (layerFiles.Length != layers)
                throw new InvalidOperationException(
                    $"预期生成 {layers} 个分层 TIFF，实际找到 {layerFiles.Length} 个。");

            AppendPipelineLog($"\n步骤 1/3 完成：共生成 {layerFiles.Length} 个 TIFF。");
            AppendPipelineLog("步骤 2/3：开始逐层生成 Hatch DXF…\n");
            var baseVoronoiSeed = (int)(_pipelineVoronoiSeedBox.Value ?? 12345);
            var currentRunDxfFiles = new List<string>(layerFiles.Length);

            for (var index = 0; index < layerFiles.Length; index++)
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                var layerFile = layerFiles[index];
                var outputFile = Path.Combine(
                    dxfOutputAbsolute,
                    $"{Path.GetFileNameWithoutExtension(layerFile)}.dxf");
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
                    "--threshold", threshold.ToString(CultureInfo.InvariantCulture),
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

                var hatchExitCode = await RunProcessAsync(
                    hatchInfo,
                    line => AppendPipelineLog($"    {line}"),
                    _cancellation.Token);
                if (hatchExitCode != 0)
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(layerFile)} 转换失败，退出代码：{hatchExitCode}");
                ValidateGeneratedLayerPair(
                    outputFile,
                    (_pipelineBlocksBox.Value ?? 0) > 0);
                currentRunDxfFiles.Add(Path.GetFullPath(outputFile));
                var previewItem = new DxfPreviewItem(
                    $"第 {index + 1:D2} 层 · {Path.GetFileName(outputFile)}",
                    outputFile);
                _pipelineDxfFiles.Add(previewItem);
                _pipelineDxfSelector.SelectedItem = previewItem;
            }

            AppendPipelineLog($"\n步骤 2/3 完成：共生成 {layerFiles.Length} 个 DXF。");
            AppendPipelineLog($"DXF 目录：{dxfOutput}");

            var pathComparer = StringComparer.OrdinalIgnoreCase;
            var expectedDxfFiles = new HashSet<string>(currentRunDxfFiles, pathComparer);
            var actualDxfFiles = Directory
                .EnumerateFiles(
                    dxfOutputAbsolute,
                    "layer_*.dxf",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = false,
                        MatchCasing = MatchCasing.CaseInsensitive,
                        ReturnSpecialDirectories = false
                    })
                .Select(Path.GetFullPath)
                .ToHashSet(pathComparer);
            var unexpectedDxfFiles = actualDxfFiles
                .Except(expectedDxfFiles, pathComparer)
                .OrderBy(path => path, pathComparer)
                .ToArray();
            var missingDxfFiles = expectedDxfFiles
                .Where(path => !IsRegularNonEmptyFile(path))
                .OrderBy(path => path, pathComparer)
                .ToArray();
            if (unexpectedDxfFiles.Length > 0 || missingDxfFiles.Length > 0)
            {
                var manifestError = new StringBuilder();
                manifestError.AppendLine(
                    $"DXF 目录与本次运行清单不一致：意外文件 {unexpectedDxfFiles.Length} 个，" +
                    $"缺失文件 {missingDxfFiles.Length} 个。");
                if (unexpectedDxfFiles.Length > 0)
                {
                    manifestError.AppendLine("意外文件：");
                    foreach (var path in unexpectedDxfFiles)
                        manifestError.AppendLine($"- {path}");
                }
                if (missingDxfFiles.Length > 0)
                {
                    manifestError.AppendLine("缺失文件：");
                    foreach (var path in missingDxfFiles)
                        manifestError.AppendLine($"- {path}");
                }
                manifestError.Append("请使用干净的 DXF 目录后重试；程序不会自动删除任何文件。");
                throw new InvalidOperationException(manifestError.ToString());
            }
            AppendPipelineLog($"已验证本次 DXF 清单：{expectedDxfFiles.Count} 个文件。");

            AppendPipelineLog("\n步骤 3/3：开始生成机器加工文件…");
            var useBlockCenterMotion =
                (_pipelineBlocksBox.Value ?? 0) > 0 &&
                _pipelineBlockCenterMotionBox.IsChecked == true;
            AppendPipelineLog(
                $"加工块中心 XY 定位：{(useBlockCenterMotion ? "已启用" : "未启用")}。");
            var ownerToken = Guid.NewGuid().ToString("N");
            var machineInfo = CreatePythonProcess(python);
            foreach (var argument in new[]
            {
                machineScript, dxfOutputAbsolute, machineName,
                "--owner-token", ownerToken,
                "--layer-step-um", Invariant(layerStep.Value),
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

            var machineExitCode = await RunProcessAsync(
                machineInfo,
                AppendPipelineLog,
                _cancellation.Token);
            if (machineExitCode != 0)
                throw new InvalidOperationException($"加工文件生成失败，退出代码：{machineExitCode}");
            if (!Directory.Exists(machineOutputPath))
                throw new InvalidOperationException($"加工文件生成结束，但未找到输出目录：{machineOutputPath}");

            _lastMachineOutputPath = machineOutputPath;
            AppendPipelineLog("\n步骤 3/3 完成：加工文件生成成功。");
            AppendPipelineLog($"加工文件目录：{machineOutputPath}");
            AppendPipelineLog(
                $"三步流程完成：已生成 {layerFiles.Length} 个 TIFF、{layerFiles.Length} 个 DXF 和 1 个加工文件。");
            _pipelineOpenButton.IsEnabled = true;
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
            _cancellation.Dispose();
            _cancellation = null;
            _pipelineRunButton.IsEnabled = true;
            _pipelineBlocksBox.IsEnabled = pipelineBlocksBoxWasEnabled;
            UpdateBlockCenterMotionAvailability();
            _pipelineProgress.IsIndeterminate = false;
        }
    }

    private static void ValidateGeneratedLayerPair(
        string dxfPath,
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

    private static async Task<TextureImageInfo> InspectTextureImageAsync(
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

        using var process = new Process { StartInfo = info };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
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

        return TextureImageInfo.ParseJson(await stdoutTask);
    }

    private static async Task WaitForExitOrKillAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        using var cancellationRegistration = cancellationToken.Register(
            static state => TryTerminateProcess((Process)state!),
            process);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryTerminateProcess(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the cancellation after making a best effort to reap the process.
            }
            throw;
        }
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may have exited between the state check and termination.
        }
    }

    private static async Task LoadTexturePreviewAsync(
        string path,
        TexturePreviewView view,
        TextBox dpiBox,
        NumericUpDown widthBox,
        NumericUpDown heightBox,
        TexturePreviewController controller)
    {
        var operation = controller.BeginImport();
        RenderTexturePreview(view, controller.State);

        Bitmap? candidateBitmap = null;
        try
        {
            var info = await InspectTextureImageAsync(path, operation.CancellationToken);
            operation.CancellationToken.ThrowIfCancellationRequested();
            var constraint = TexturePreviewDecodePolicy.Select(info, 380);
            using (var stream = File.OpenRead(path))
            {
                candidateBitmap = constraint.Axis == TexturePreviewDecodeAxis.Width
                    ? Bitmap.DecodeToWidth(stream, constraint.PixelLimit)
                    : Bitmap.DecodeToHeight(stream, constraint.PixelLimit);
            }

            var completedPreview = candidateBitmap;
            candidateBitmap = null;
            if (!controller.TryCompleteImport(
                    operation,
                    completedPreview,
                    info,
                    dpiBox.Text,
                    widthBox.Minimum,
                    widthBox.Maximum,
                    out _))
            {
                return;
            }

            RenderTexturePreview(view, controller.State);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            // A newer import or window close owns the visible state.
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Texture preview import failed for '{path}': {ex}");
            if (controller.TryFail(operation, ex))
                RenderTexturePreview(view, controller.State);
        }
        finally
        {
            candidateBitmap?.Dispose();
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

    private static void RenderTexturePreview(
        TexturePreviewView view,
        TexturePreviewState state)
    {
        view.Metadata.Text = state.MetadataText;
        view.PhysicalSize.Text = state.PhysicalSizeText;
        if (state.Phase == TexturePreviewPhase.Failed)
            view.Metadata.Foreground = Brushes.OrangeRed;
        else
            view.Metadata.ClearValue(TextBlock.ForegroundProperty);
    }

    private void DisposeTexturePreviews()
    {
        _hatchPreviewController.Close();
        _pipelinePreviewController.Close();
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
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may already have exited.
            }
        });
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may already have exited.
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve cancellation even if waiting for final termination fails.
            }
            throw;
        }
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
            _hatchTextureView,
            _dpiBox,
            _widthBox,
            _heightBox,
            _hatchPreviewController);
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

        var script = Path.Combine(AppContext.BaseDirectory, "texture_to_hatch_dxf.py");
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

        var width = _widthBox.Value ?? 100;
        var height = _heightBox.Value ?? 100;
        var spacing = _spacingBox.Value ?? 0.02m;
        var threshold = (int)(_thresholdBox.Value ?? 128);
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
                "--threshold", threshold.ToString(CultureInfo.InvariantCulture),
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
            using var cancellationRegistration = _cancellation.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // The process may already have exited.
                }
            });
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(_cancellation.Token);

            if (process.ExitCode == 0)
            {
                AppendHatchLog("\nDXF 生成完成。");
                _hatchOpenButton.IsEnabled = true;
                LoadDxfPreview(_hatchDxfPreview, _hatchDxfPreviewStatus, output);
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

        var script = Path.Combine(AppContext.BaseDirectory, "grayscale_layers.py");
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
        AppendLog($"输出：{output}\n");

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
            info.ArgumentList.Add(((int)(_layersBox.Value ?? 10)).ToString());
            if (_belowIsWhite.IsChecked == true)
                info.ArgumentList.Add("--below-is-white");

            using var process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog($"错误：{e.Data}"); };

            process.Start();
            using var cancellationRegistration = _cancellation.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // The process may already have exited.
                }
            });
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(_cancellation.Token);

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

    private static void LoadDxfPreview(
        DxfPreviewControl preview,
        TextBlock status,
        string path)
    {
        try
        {
            preview.LoadFile(path);
            status.Text = preview.Summary;
            status.ClearValue(TextBlock.ForegroundProperty);
        }
        catch (Exception ex)
        {
            status.Text = $"无法预览 {Path.GetFileName(path)}：{ex.Message}";
            status.Foreground = Brushes.OrangeRed;
        }
    }

    private async Task ImportDxfPreviewAsync(
        DxfPreviewControl preview,
        TextBlock status,
        bool addToPipelineSelector)
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
            var item = new DxfPreviewItem($"导入 · {Path.GetFileName(path)}", path);
            _pipelineDxfFiles.Add(item);
            _pipelineDxfSelector.SelectedItem = item;
        }
        else
        {
            LoadDxfPreview(preview, status, path);
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
