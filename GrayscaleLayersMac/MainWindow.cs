using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
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

internal sealed record PipelineImportSelection(
    Func<(string[] Tiffs, string[] Dxfs)> Discover,
    string SuccessHeading,
    string EmptySelectionMessage,
    string? TiffDirectory,
    string? DxfDirectory);

internal sealed record PreparedImportFlowActions(
    Func<
        string[],
        string[],
        IProgress<ImportProgressState>,
        CancellationToken,
        Task<PreparedPipelineImport>> PrepareAsync,
    Func<
        PreparedPipelineImport,
        CancellationToken,
        Task<PreparedGrayscaleLayerSet>> PrepareLayersAsync,
    Action<PreparedGrayscaleLayerSet, string?> CommitTiffs,
    Func<IReadOnlyList<DxfPreviewControl.PreparedDxfPreview>, string?, Action> CreateDxfCommit,
    Action<ImportProgressState> Show,
    Action<ImportProgressState> Update,
    Func<ImportProgressState, CancellationToken, Task> ShowSucceededAndCollapseAsync,
    Action<ImportProgressState> ShowFailure,
    Action<string> AppendLog,
    Func<string, Task> ShowMessageAsync);

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

public sealed class MainWindow : Window
{
    internal const double AppHeaderHeight = 64;
    internal static readonly bool AppExtendsIntoWindowDecorations = true;
    internal static readonly SystemDecorations AppSystemDecorations = SystemDecorations.Full;
    internal static readonly ExtendClientAreaChromeHints AppChromeHints =
        ExtendClientAreaChromeHints.PreferSystemChrome |
        ExtendClientAreaChromeHints.OSXThickTitleBar;
    internal static readonly Thickness AppHeaderPadding = new(80, 8, 20, 8);

    private enum PipelineRunMode
    {
        All,
        GrayscaleOnly,
        DxfOnly,
        MachineOnly,
        LaserPmtOnly
    }

    private const int InspectionJsonOverheadCharacters = 4 * 1024;
    private const int MaximumInspectionStandardErrorCharacters = 1024 * 1024;
    private static readonly int MaximumInspectionStandardOutputCharacters = checked(
        TextureImageInspection.GetMaximumBase64CharacterCount(
            TextureImageInspection.DefaultMaximumPreviewBytes) + InspectionJsonOverheadCharacters);

    private sealed record SharedPreviewView(
        ToggleButton TextureTab,
        ToggleButton DxfTab,
        ToggleButton PmtTab,
        Control TextureContent,
        Control DxfContent,
        Control PmtContent,
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

    private readonly TextBox _pipelineInputBox = new() { Watermark = "请选择一张灰度纹理图" };
    private readonly TextBox _pipelineLayerOutputBox = new() { Watermark = "请选择分层 TIFF 保存目录" };
    private readonly TextBox _pipelineDxfOutputBox = new() { Watermark = "请选择 DXF 保存目录" };
    private readonly NumericUpDown _pipelineLayersBox = MakeNumberBox(10, 1, 255, 0, showButtons: false);
    private readonly NumericUpDown _pipelineMinLevelBox = MakeNumberBox(0, 1, 254, 0, showButtons: false);
    private readonly NumericUpDown _pipelineMaxLevelBox = MakeNumberBox(255, 1, 255, 0, showButtons: false);
    private readonly CheckBox _pipelineBelowIsWhite = new() { Content = "低于阈值的区域设为白色（默认设为黑色）" };
    private readonly NumericUpDown _pipelineWidthBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _pipelineHeightBox = MakeNumberBox(100, 0.01m, 100000, showButtons: false);
    private readonly NumericUpDown _pipelineSpacingBox = MakeNumberBox(0.02m, 0.001m, 1000);
    private readonly NumericUpDown _pipelineHatchAngleStepBox = MakeNumberBox(0, 0.1m, 180, 2, showButtons: false);
    private readonly TextBox _pipelineDpiBox = new() { Watermark = "可选；图片无 DPI 时填写" };
    // 四步流程页的纹理界面：第 0 层是导入的源纹理，之后是各灰度分层。
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
    private readonly LaserPmtPanel _pipelinePmtPanel = new();
    private readonly PmtPreviewControl _pipelinePmtPreview = new();
    private readonly PmtDetailsEditor _pipelinePmtDetails = new() { MaxHeight = 160 };
    private string? _pipelinePmtLayoutPath;
    private readonly DxfPreviewControl _pipelineDxfPreview = new(startInTopView: true);
    private readonly TextBlock _pipelineDxfPreviewStatus = new() { Foreground = UiTheme.TextSecondaryBrush };
    private readonly ObservableCollection<DxfLayerPreviewItem> _pipelineDxfFiles = [];
    // DXF 预览宿主在构造预览面板时才建好，因此这里只能是可空字段。
    private DxfPreviewHost? _pipelineDxfHost;
    private readonly TextBox _pipelineLogBox = MakeLogBox();
    private readonly DropDownButton _pipelineImportButton = new() { Content = "导入", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ImportProgressOverlay _pipelineImportProgress;
    private readonly ImportProgressOverlay _pipelineRunProgress;
    private readonly Button _pipelineClearButton = new() { Content = "清空缓存", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly DropDownButton _appearanceButton = new() { Content = "外观", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _pipelineReadinessText = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiTheme.TextSecondaryBrush
    };
    private readonly TextBlock _pipelineActionStateText = new()
    {
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiTheme.TextSecondaryBrush
    };
    private Flyout? _pipelineImportFlyout;
    private readonly SplitButton _pipelineRunSplitButton = new()
    {
        Content = "全部执行",
        HorizontalAlignment = HorizontalAlignment.Left
    };
    private readonly Button _pipelineOpenButton = new() { Content = "打开加工文件目录", IsEnabled = false };
    private readonly ProgressBar _pipelineProgress = UiTheme.CreateProgress();
    private readonly TexturePreviewController _pipelinePreviewController;
    private readonly SharedPreviewView _pipelineSharedPreview;
    private string? _lastMachineOutputPath;
    private string? _lastLaserPmtOutputPath;
    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// 把 DXF 预览宿主接到主界面：选层改由宿主的图层侧栏驱动（与纹理界面同一套交互），
    /// 侧栏的展开 / 收起沿用工位设置的持久化。
    /// 宿主在 <see cref="MakeSharedPreviewPanel"/> 里才建好，因此必须在预览面板之后调用。
    /// </summary>
    private void ConfigurePipelineDxfHost()
    {
        var host = _pipelineDxfHost;
        if (host is null)
            return;

        host.LoadLayer = item => LoadPipelineLayerPreview(item, host.KeepView);
        host.SetItems(_pipelineDxfFiles);
        // 先恢复、再订阅：恢复动作本身不会触发一次多余的写入。
        host.SetRailCollapsed(_workspaceSplitSettings.LoadDxfLayerCollapsed());
        host.RailCollapsedChanged += (_, _) =>
            _workspaceSplitSettings.TrySaveDxfLayerCollapsed(host.IsRailCollapsed);
    }

    /// <param name="keepView">
    /// 为真时保留当前缩放 / 平移 / 视角——逐层对照时不必每次都跳回适应窗口。
    /// 由宿主的「切层保持视图」勾选框下发。
    /// </param>
    private bool LoadPipelineLayerPreview(DxfLayerPreviewItem item, bool keepView)
    {
        _pipelineDxfPreview.ClearTexture();
        _pipelineSharedPreview.UpdateDxfOverlayControls();
        if (item.PreparedPreview is not null)
        {
            try
            {
                _pipelineDxfPreview.InstallPreparedFile(item.PreparedPreview, keepView);
                _pipelineDxfPreviewStatus.Text = _pipelineDxfPreview.Summary;
                _pipelineDxfPreviewStatus.ClearValue(TextBlock.ForegroundProperty);
            }
            catch (Exception error)
            {
                _pipelineDxfPreviewStatus.Text =
                    $"无法预览 {Path.GetFileName(item.DxfPath)}：{error.Message}";
                _pipelineDxfPreviewStatus.Foreground = UiTheme.DangerTextBrush;
                _pipelineSharedPreview.UpdateDxfOverlayControls();
                return false;
            }
        }
        else if (!LoadDxfPreview(
                     _pipelineDxfPreview,
                     _pipelineDxfPreviewStatus,
                     item.DxfPath,
                     keepView))
        {
            _pipelineSharedPreview.UpdateDxfOverlayControls();
            return false;
        }

        if (item.HasTexture)
        {
            try
            {
                _pipelineDxfPreview.LoadTexture(
                    item.TexturePath!, item.TextureRegistration!, keepView);
            }
            catch (Exception error)
            {
                _pipelineDxfPreview.ClearTexture();
                _pipelineDxfPreviewStatus.Text = $"无法加载配准纹理：{error.Message}";
                _pipelineDxfPreviewStatus.Foreground = UiTheme.DangerTextBrush;
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
        ConfigureIntegratedTitleBar(this);
        Icon = new WindowIcon(
            AssetLoader.Open(
                new Uri("avares://GrayscaleLayersMac/Assets/AppIcon.png")));
        Width = 1440;
        Height = 940;
        MinWidth = 1080;
        MinHeight = 720;
        FontFamily = UiTheme.UiFont;
        Background = UiTheme.RootBrush;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _pipelinePreviewController = new TexturePreviewController(
            source => _pipelineTextureSurface.SetSourceTexture(source as TexturePreviewPayload),
            update => ApplyTextureSizeUpdate(
                update,
                _pipelineWidthBox,
                _pipelineHeightBox));
        Styles.Add(UiTheme.CreateGlobalStyles());
        UiTheme.ApplyFluentResourceOverrides(this);
        _workspacePreviewRatio = _workspaceSplitSettings.LoadPreviewRatio();
        // 主界面图层缩略图侧栏恢复上次收起状态。
        // 先恢复、再订阅，恢复动作本身不会触发一次多余的写入。
        _pipelineTextureSurface.SetThumbnailsCollapsed(
            _workspaceSplitSettings.LoadThumbnailCollapsed());
        _pipelineTextureSurface.ThumbnailsCollapsedChanged += (_, _) =>
            _workspaceSplitSettings.TrySaveThumbnailCollapsed(
                _pipelineTextureSurface.IsThumbnailsCollapsed);
        UiTheme.ApplyPrimaryStyle(_pipelineRunSplitButton);
        AutomationProperties.SetName(_pipelineRunSplitButton, "全部执行与单步执行");
        ApplyPipelineInputStyles();
        _pipelineDpiBox.TextChanged += (_, _) =>
        {
            _pipelinePreviewController.ApplyFallbackDpiEdit(
                _pipelineDpiBox.Text,
                _pipelineWidthBox.Minimum,
                _pipelineWidthBox.Maximum);
            RenderTexturePreview(_pipelineTextureSurface, _pipelinePreviewController.State);
        };
        Closed += (_, _) => DisposeTexturePreviews();

        var pipelineInputButton = new Button { Content = "选择图片…" };
        var pipelineLayerOutputButton = new Button { Content = "选择目录…" };
        var pipelineDxfOutputButton = new Button { Content = "选择目录…" };
        pipelineInputButton.Click += async (_, _) => await PickPipelineInputAsync();
        pipelineLayerOutputButton.Click += async (_, _) =>
            await PickPipelineFolderAsync(_pipelineLayerOutputBox, "选择分层 TIFF 保存目录");
        pipelineDxfOutputButton.Click += async (_, _) =>
            await PickPipelineFolderAsync(_pipelineDxfOutputBox, "选择 DXF 保存目录");
        _pipelinePmtPanel.PickBaseDirectoryRequested += async (_, _) =>
            await ImportMachineDirectoryAsync();
        _pipelinePmtPanel.ConfigurationChanged += (_, _) => UpdatePipelineReadiness();
        _pipelinePmtPreview.SelectionChanged += (_, _) => UpdatePmtSelectionDetails();
        _pipelinePmtDetails.SaveRequested += OnPmtDetailsSaveRequested;
        _pipelineImportFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = UiTheme.FlyoutSurface(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    CreatePipelineImportMenuButton(
                        "选择文件夹…",
                        ImportPipelineDirectoryAsync),
                    CreatePipelineImportMenuButton(
                        "选择文件…",
                        ImportPipelineFilesAsync),
                    CreatePipelineImportMenuButton(
                        "导入加工文件目录…",
                        ImportMachineDirectoryAsync)
                }
            })
        };
        UiTheme.RemoveFlyoutOuterChrome(_pipelineImportFlyout);
        _pipelineImportButton.Flyout = _pipelineImportFlyout;
        _pipelineImportButton.Content = UiIcons.Labeled(UiIcon.Import, "导入");
        ToolTip.SetTip(_pipelineImportButton, "导入已有的分层 TIFF、DXF 或机器加工文件。");
        ConfigureAppearanceMenu();
        ToolTip.SetTip(
            _pipelineClearButton,
            "清空所有已导入或生成的 TIFF 与 DXF 预览缓存；不会删除磁盘上的文件。");
        _pipelineClearButton.Click += (_, _) => ClearImportedArtifacts();
        _pipelineRunSplitButton.Click += async (_, _) =>
        {
            if (_cancellation is null)
                await RunPipelineAsync(PipelineRunMode.All);
        };
        var singleStepFlyout = new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            Content = UiTheme.FlyoutSurface(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    CreatePipelineStepMenuButton(
                        "第 1 步：灰度分层",
                        PipelineRunMode.GrayscaleOnly),
                    CreatePipelineStepMenuButton(
                        "第 2 步：生成 DXF",
                        PipelineRunMode.DxfOnly),
                    CreatePipelineStepMenuButton(
                        "第 3 步：生成加工文件",
                        PipelineRunMode.MachineOnly),
                    CreatePipelineStepMenuButton(
                        "第 4 步：生成 LaserPMT",
                        PipelineRunMode.LaserPmtOnly)
                }
            })
        };
        UiTheme.RemoveFlyoutOuterChrome(singleStepFlyout);
        _pipelineRunSplitButton.Flyout = singleStepFlyout;
        _pipelineOpenButton.Click += (_, _) =>
            OpenDirectory(_lastLaserPmtOutputPath ?? _lastMachineOutputPath);
        _pipelineBlocksBox.ValueChanged += (_, _) => UpdateBlockCenterMotionAvailability();
        UpdateBlockCenterMotionAvailability();

        _pipelineInputBox.TextChanged += (_, _) => UpdatePipelineReadiness();
        _pipelineLayerOutputBox.TextChanged += (_, _) => UpdatePipelineReadiness();
        _pipelineDxfOutputBox.TextChanged += (_, _) => UpdatePipelineReadiness();
        _pipelineInputBox.LostFocus += async (_, _) => await OnPipelineInputBoxLostFocusAsync();
        _pipelineLayerOutputBox.LostFocus += (_, _) => NormalizeDirectoryBox(_pipelineLayerOutputBox);
        _pipelineDxfOutputBox.LostFocus += (_, _) => NormalizeDirectoryBox(_pipelineDxfOutputBox);
        UpdatePipelineReadiness();

        var pipelineInspector = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                new Border
                {
                    Padding = new Thickness(12, 10),
                    CornerRadius = UiTheme.ControlRadius,
                    Background = UiTheme.SunkenBrush,
                    BorderBrush = UiTheme.BorderSubtleBrush,
                    BorderThickness = new Thickness(1),
                    Child = _pipelineReadinessText
                },
                MakeInspectorSection(
                    "灰度分层",
                    MakeField("原始灰度图", _pipelineInputBox, pipelineInputButton),
                    MakeField("分层 TIFF 目录", _pipelineLayerOutputBox, pipelineLayerOutputButton),
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
                    MakeField("DXF 目录", _pipelineDxfOutputBox, pipelineDxfOutputButton),
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
                        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                        ColumnSpacing = 16,
                        Children =
                        {
                            MakeLabeledControl("层间进给（μm）", _pipelineLayerStepBox, 0),
                            MakeLabeledControl("加工文件名", _pipelineMachineNameBox, 1)
                        }
                    },
                    _pipelineBlockCenterMotionBox,
                    UiTheme.StyleExpander(new Expander
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
                    })),
                MakeInspectorSection(
                    "LaserPMT 参数矩阵",
                    _pipelinePmtPanel),
                _pipelineProgress,
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        _pipelineActionStateText,
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                            ColumnSpacing = 10,
                            Children =
                            {
                                Place(_pipelineRunSplitButton, 0),
                                Place(_pipelineOpenButton, 1)
                            }
                        }
                    }
                }
            }
        };
        var pipelinePreviewPanel = MakeSharedPreviewPanel(
            _pipelineTextureSurface,
            _pipelineDxfPreview,
            _pipelineDxfPreviewStatus,
            _pipelinePmtPreview,
            _pipelinePmtDetails,
            out _pipelineSharedPreview);
        ConfigurePipelineDxfHost();
        var pipelineContent = MakeWorkspace(
            pipelineInspector,
            pipelinePreviewPanel,
            _pipelineLogBox,
            "log",
            PipelineLogKey);

        foreach (var secondaryButton in new[]
        {
            pipelineInputButton, pipelineLayerOutputButton, pipelineDxfOutputButton,
            _pipelineOpenButton
        })
            UiTheme.ApplySecondaryStyle(secondaryButton);

        UiTheme.ApplyQuietStyle(_pipelineImportButton);
        AutomationProperties.SetName(_pipelineImportButton, "导入中间结果");
        _pipelineImportProgress = new ImportProgressOverlay(_pipelineImportButton);
        _pipelineRunProgress = new ImportProgressOverlay(
            _pipelineRunSplitButton,
            cancelRequested: () => _cancellation?.Cancel(),
            placement: PlacementMode.TopEdgeAlignedLeft);
        UiTheme.ApplyIconStyle(_pipelineClearButton, "清空缓存");
        _pipelineClearButton.Content = UiIcons.Create(UiIcon.ClearCache);
        UiTheme.ApplyQuietStyle(_appearanceButton);
        AutomationProperties.SetName(_appearanceButton, "切换外观");

        var headerTools = new Border
        {
            Padding = new Thickness(4),
            CornerRadius = UiTheme.SegmentRadius,
            Background = UiTheme.CardBrush,
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { _pipelineImportButton, _pipelineClearButton, _appearanceButton }
            }
        };

        var headerDragRegion = new Grid
        {
            Background = Brushes.Transparent,
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
            Children =
            {
                Place(new Border
                {
                    Width = 38,
                    Height = 38,
                    Padding = new Thickness(6),
                    CornerRadius = UiTheme.SegmentRadius,
                    Background = UiTheme.CardBrush,
                    BorderBrush = UiTheme.BorderSubtleBrush,
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new Image
                    {
                        Source = new Bitmap(
                            AssetLoader.Open(
                                new Uri("avares://GrayscaleLayersMac/Assets/AppIcon.png"))),
                        Width = 26,
                        Height = 26
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
                            FontSize = 15.5,
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
        };
        headerDragRegion.PointerPressed += (_, args) => BeginHeaderDrag(args);

        var appHeader = new Border
        {
            Padding = AppHeaderPadding,
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = UiTheme.HeaderBrush,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 12,
                Children =
                {
                    Place(headerDragRegion, 0),
                    Place(headerTools, 1)
                }
            }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("64,*"),
            Children =
            {
                AtRow(appHeader, 0),
                AtRow(new Border
                {
                    Child = pipelineContent,
                    Margin = new Thickness(16, 0, 16, 16)
                }, 1),
                _pipelineImportProgress.Root,
                _pipelineRunProgress.Root
            }
        };
        Content = root;
    }

    internal static void ConfigureIntegratedTitleBar(Window window)
    {
        window.SystemDecorations = AppSystemDecorations;
        window.ExtendClientAreaToDecorationsHint = AppExtendsIntoWindowDecorations;
        window.ExtendClientAreaChromeHints = AppChromeHints;
        window.ExtendClientAreaTitleBarHeightHint = AppHeaderHeight;
    }

    private void BeginHeaderDrag(PointerPressedEventArgs args)
    {
        var updateKind = args.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (IsHeaderDragGesture(updateKind))
            BeginMoveDrag(args);
    }

    internal static bool IsHeaderDragGesture(PointerUpdateKind updateKind) =>
        updateKind == PointerUpdateKind.LeftButtonPressed;

    private void ConfigureAppearanceMenu()
    {
        if (Application.Current is not App app)
        {
            _appearanceButton.IsEnabled = false;
            return;
        }

        var system = new RadioButton { Content = "跟随系统", GroupName = "appearance" };
        var light = new RadioButton { Content = "浅色", GroupName = "appearance" };
        var dark = new RadioButton { Content = "深色", GroupName = "appearance" };
        UiTheme.ApplyAppearanceOptionStyle(system);
        UiTheme.ApplyAppearanceOptionStyle(light);
        UiTheme.ApplyAppearanceOptionStyle(dark);

        void RefreshSelection()
        {
            system.IsChecked = app.Appearance == AppAppearance.System;
            light.IsChecked = app.Appearance == AppAppearance.Light;
            dark.IsChecked = app.Appearance == AppAppearance.Dark;
            var label = app.Appearance switch
            {
                AppAppearance.Light => "外观 · 浅色",
                AppAppearance.Dark => "外观 · 深色",
                _ => "外观 · 系统"
            };
            _appearanceButton.Content = UiIcons.Labeled(UiIcon.Appearance, label);
        }

        system.Click += (_, _) => app.SetAppearance(AppAppearance.System);
        light.Click += (_, _) => app.SetAppearance(AppAppearance.Light);
        dark.Click += (_, _) => app.SetAppearance(AppAppearance.Dark);
        app.AppearanceChanged += (_, _) => RefreshSelection();

        var appearanceFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = UiTheme.FlyoutSurface(new Border
            {
                Padding = new Thickness(2),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children = { system, light, dark }
                }
            })
        };
        UiTheme.RemoveFlyoutOuterChrome(appearanceFlyout);
        _appearanceButton.Flyout = appearanceFlyout;
        ToolTip.SetTip(_appearanceButton, "默认跟随 macOS 外观，也可以在此手动覆盖。");
        RefreshSelection();
    }

    private void UpdatePipelineReadiness()
    {
        var ready = _cancellation is null &&
            !string.IsNullOrWhiteSpace(_pipelineInputBox.Text) &&
            !string.IsNullOrWhiteSpace(_pipelineLayerOutputBox.Text) &&
            !string.IsNullOrWhiteSpace(_pipelineDxfOutputBox.Text);
        var message = PipelineReadiness.Describe(
            _cancellation is not null,
            _pipelineInputBox.Text,
            _pipelineLayerOutputBox.Text,
            _pipelineDxfOutputBox.Text);

        _pipelineReadinessText.Text = message;
        _pipelineActionStateText.Text = message;
        var foreground = ready ? UiTheme.TextPrimaryBrush : UiTheme.TextSecondaryBrush;
        _pipelineReadinessText.Foreground = foreground;
        _pipelineActionStateText.Foreground = foreground;
    }

    private static NumericUpDown MakeNumberBox(
        decimal value,
        decimal increment,
        decimal maximum,
        int decimalPlaces = 3,
        decimal minimum = 0,
        bool showButtons = true)
    {
        var box = new NumericUpDown
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
        UiTheme.ApplyInputStyle(box);
        return box;
    }

    private void ApplyPipelineInputStyles()
    {
        foreach (var input in new Control[]
        {
            _pipelineInputBox,
            _pipelineLayerOutputBox,
            _pipelineDxfOutputBox,
            _pipelineDpiBox,
            _pipelineMachineNameBox,
            _pipelineAnchorBox
        })
            UiTheme.ApplyInputStyle(input);

        AttachPathTooltip(_pipelineInputBox);
        AttachPathTooltip(_pipelineLayerOutputBox);
        AttachPathTooltip(_pipelineDxfOutputBox);
    }

    private static void AttachPathTooltip(TextBox box)
    {
        void Refresh() => ToolTip.SetTip(
            box,
            string.IsNullOrWhiteSpace(box.Text) ? box.Watermark : box.Text);

        box.TextChanged += (_, _) => Refresh();
        Refresh();
    }

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
    /// 预览区包含「纹理」「DXF」「PMT」三个标签页：纹理界面内部用第 0 层承载源纹理，
    /// 1..N 承载灰度分层，所以不再需要单独的分层标签页。
    /// 两侧共用同一套「图层侧栏 + 工具栏 + 画布 + 状态行」骨架，只是内容不同。
    /// </summary>
    private Control MakeSharedPreviewPanel(
        GrayscaleLayerPreviewControl texture,
        DxfPreviewControl dxfPreview,
        TextBlock dxfStatus,
        PmtPreviewControl pmtPreview,
        PmtDetailsEditor pmtDetails,
        out SharedPreviewView view)
    {
        var textureContent = MakeTexturePreviewContent(texture);
        var dxfContent = MakePipelineDxfPreviewContent(
            dxfPreview,
            dxfStatus,
            out var dxfHost,
            out var updateDxfOverlayControls);
        _pipelineDxfHost = dxfHost;
        var pmtContent = MakePmtPreviewContent(pmtPreview, pmtDetails);
        var textureTab = new ToggleButton { Content = "纹理" };
        var dxfTab = new ToggleButton { Content = "DXF" };
        var pmtTab = new ToggleButton { Content = "PMT" };
        UiTheme.ApplyPreviewTabStyle(textureTab);
        UiTheme.ApplyPreviewTabStyle(dxfTab);
        UiTheme.ApplyPreviewTabStyle(pmtTab);
        AutomationProperties.SetName(textureTab, "显示纹理预览");
        AutomationProperties.SetName(dxfTab, "显示 DXF 预览");
        AutomationProperties.SetName(pmtTab, "显示 PMT 工件布局");
        var sharedView = new SharedPreviewView(
            textureTab,
            dxfTab,
            pmtTab,
            textureContent,
            dxfContent,
            pmtContent,
            new SharedPreviewSelection(),
            updateDxfOverlayControls);
        textureTab.Click += (_, _) => SelectSharedPreview(sharedView, SharedPreviewKind.Texture);
        dxfTab.Click += (_, _) => SelectSharedPreview(sharedView, SharedPreviewKind.Dxf);
        pmtTab.Click += (_, _) => SelectSharedPreview(sharedView, SharedPreviewKind.Pmt);
        SelectSharedPreview(sharedView, SharedPreviewKind.Texture);
        view = sharedView;

        var previewSegments = new Border
        {
            Padding = new Thickness(3),
            CornerRadius = UiTheme.SegmentRadius,
            Background = UiTheme.CardBrush,
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Children = { textureTab, dxfTab, pmtTab }
            }
        };

        return new Grid
        {
            Margin = new Thickness(0, 12, 12, 12),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            Children =
            {
                AtRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        Place(previewSegments, 1)
                    }
                }, 0),
                AtRow(new Grid
                {
                    Children = { textureContent, dxfContent, pmtContent }
                }, 1)
            }
        };
    }

    private static Control MakeTexturePreviewContent(GrayscaleLayerPreviewControl view) => view;

    private static Control MakePmtPreviewContent(PmtPreviewControl preview, PmtDetailsEditor details)
    {
        var fit = new Button { Content = "适应窗口" };
        var zoomOut = new Button { Content = "−" };
        var zoomIn = new Button { Content = "+" };
        UiTheme.ApplySecondaryStyle(fit);
        UiTheme.ApplySecondaryStyle(zoomOut);
        UiTheme.ApplySecondaryStyle(zoomIn);
        fit.Click += (_, _) => preview.FitToView();
        zoomOut.Click += (_, _) => preview.ZoomOut();
        zoomIn.Click += (_, _) => preview.ZoomIn();
        return new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            Children =
            {
                AtRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { fit, zoomOut, zoomIn }
                }, 0),
                AtRow(preview, 1),
                AtRow(new Border
                {
                    Padding = new Thickness(10, 8),
                    CornerRadius = UiTheme.ControlRadius,
                    Background = UiTheme.CardBrush,
                    BorderBrush = UiTheme.BorderSubtleBrush,
                    BorderThickness = new Thickness(1),
                    Child = details
                }, 2)
            }
        };
    }

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

    /// <summary>
    /// 主界面的 DXF 预览：图层侧栏 + 工具栏 + 叠加控制行 + 画布 + 状态行，
    /// 整块交给 <see cref="DxfPreviewHost"/> 拼装，与纹理界面是同一个骨架。
    /// 「顶视图 / 等轴测」是 DXF 专属工具，以 extraTools 插进标准工具栏。
    /// </summary>
    private static Control MakePipelineDxfPreviewContent(
        DxfPreviewControl preview,
        TextBlock status,
        out DxfPreviewHost host,
        out Action updateOverlayControlAvailability)
    {
        var topButton = new Button { Content = "顶视图" };
        var isometricButton = new Button { Content = "等轴测" };
        UiTheme.ApplyGhostStyle(topButton, small: true);
        UiTheme.ApplyGhostStyle(isometricButton, small: true);
        ToolTip.SetTip(topButton, "回到正上方俯视，并开始 / 继续顶视图巡览。");
        ToolTip.SetTip(isometricButton, "切到 35° 等轴测视角，便于看清分层高度。");
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

        var overlayRow = new StackPanel
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
        };

        // 操作提示压在叠加控制行下面，省下单独一行：工具栏已经够宽了。
        var extraRow = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                overlayRow,
                new TextBlock
                {
                    Text = "左键拖拽环视 · 滚轮缩放 · 中键平移 · Shift + 中键环视 · 双击中键适应窗口",
                    Foreground = UiTheme.TextFaintBrush,
                    FontSize = 11
                }
            }
        };

        host = new DxfPreviewHost(
            preview,
            status,
            extraTools: [topButton, isometricButton],
            extraRow: extraRow);
        return host;
    }

    private static void SelectSharedPreview(SharedPreviewView view, SharedPreviewKind kind)
    {
        view.Selection.Select(kind);
        view.TextureContent.IsVisible = kind == SharedPreviewKind.Texture;
        view.DxfContent.IsVisible = kind == SharedPreviewKind.Dxf;
        view.PmtContent.IsVisible = kind == SharedPreviewKind.Pmt;
        view.TextureTab.IsChecked = kind == SharedPreviewKind.Texture;
        view.DxfTab.IsChecked = kind == SharedPreviewKind.Dxf;
        view.PmtTab.IsChecked = kind == SharedPreviewKind.Pmt;
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
        if (string.IsNullOrWhiteSpace(_pipelineLayerOutputBox.Text))
            _pipelineLayerOutputBox.Text = parent;
        if (string.IsNullOrWhiteSpace(_pipelineDxfOutputBox.Text))
            _pipelineDxfOutputBox.Text = parent;

        await LoadTexturePreviewAsync(
            path,
            _pipelineTextureSurface,
            _pipelineDpiBox,
            _pipelineWidthBox,
            _pipelineHeightBox,
            _pipelinePreviewController,
            _pipelineSharedPreview);
    }

    private Button CreatePipelineStepMenuButton(
        string label,
        PipelineRunMode mode)
    {
        var button = new Button
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        UiTheme.ApplyQuietStyle(button, small: true);
        button.Click += async (_, _) =>
        {
            _pipelineRunSplitButton.Flyout?.Hide();
            if (_cancellation is null)
                await RunPipelineAsync(mode);
        };
        return button;
    }

    private Button CreatePipelineImportMenuButton(
        string label,
        Func<Task> importAsync)
    {
        var button = new Button
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        UiTheme.ApplyQuietStyle(button, small: true);
        button.Click += async (_, _) =>
        {
            _pipelineImportFlyout?.Hide();
            if (_cancellation is not null || !_pipelineImportButton.IsEnabled)
                return;

            _pipelineImportButton.IsEnabled = false;
            _pipelineClearButton.IsEnabled = false;
            _pipelineRunSplitButton.IsEnabled = false;
            _pipelineProgress.IsIndeterminate = true;
            try
            {
                await importAsync();
            }
            finally
            {
                _pipelineProgress.IsIndeterminate = false;
                if (_cancellation is null)
                {
                    _pipelineImportButton.IsEnabled = true;
                    _pipelineClearButton.IsEnabled = true;
                    _pipelineRunSplitButton.IsEnabled = true;
                }
            }
        };
        return button;
    }

    /// <summary>
    /// 统一的"导入文件夹"入口：扫描目录后按文件类型自动路由——
    /// layer_*.tiff 接到纹理界面的第 1..N 层，*.dxf 接到 DXF 层选择器。
    /// 两类可以同时存在，缺任一类不中断另一类的导入。
    /// </summary>
    private async Task ImportPipelineDirectoryAsync()
    {
        await RunPreparedImportAsync(
            async _ =>
            {
                var directory = await PickPipelineFolderPathAsync(
                    "导入文件夹（分层 TIFF 或 DXF）");
                if (directory is null)
                    return null;

                return new PipelineImportSelection(
                    () => (
                        PipelineArtifactDiscovery.FindLayerTiffsOrEmpty(directory),
                        PipelineArtifactDiscovery.FindDxfFilesOrEmpty(directory)),
                    $"已导入文件夹：{directory}",
                    "文件夹中没有找到可导入的产物：\n" +
                    $"{directory}\n\n" +
                    "期望 layer_*.tiff（分层 TIFF）或 *.dxf（Hatch DXF）。",
                    directory,
                    directory);
            },
            "无法导入文件夹",
            CreatePreparedImportFlowActions());
    }

    /// <summary>
    /// 统一的"导入文件"入口：按扩展名分组后分别路由到分层层与 DXF 层。
    /// 一次选择里同类型的产物整体替换，与文件夹导入保持相同语义。
    /// </summary>
    private async Task ImportPipelineFilesAsync()
    {
        await RunPreparedImportAsync(
            async _ =>
            {
                var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "导入分层 TIFF 或 DXF 文件",
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("分层 TIFF 与 DXF")
                        {
                            Patterns = ["*.tiff", "*.tif", "*.dxf"]
                        },
                        new FilePickerFileType("TIFF 图像") { Patterns = ["*.tiff", "*.tif"] },
                        new FilePickerFileType("DXF 文件") { Patterns = ["*.dxf"] }
                    ]
                });

                var paths = picked
                    .Select(file => file.TryGetLocalPath())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => Path.GetFullPath(path!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (paths.Length == 0)
                    return null;

                var dxfs = paths.Where(PipelineArtifactDiscovery.IsDxf).ToArray();
                var tiffs = paths
                    .Where(path => !PipelineArtifactDiscovery.IsDxf(path))
                    .ToArray();
                return new PipelineImportSelection(
                    () => (tiffs, dxfs),
                    $"已导入 {paths.Length} 个文件",
                    "没有可导入的 TIFF 或 DXF 文件。",
                    DirectoryOf(tiffs),
                    DirectoryOf(dxfs));
            },
            "无法导入文件",
            CreatePreparedImportFlowActions());
    }

    private async Task ImportMachineDirectoryAsync()
    {
        var directory = await PickPipelineFolderPathAsync("导入机器加工文件目录");
        if (directory is null)
            return;
        try
        {
            var machineJson = Path.Combine(directory, "machine.json");
            var patches = Path.Combine(directory, "patches");
            if (!IsRegularNonEmptyFile(machineJson) || !Directory.Exists(patches) ||
                !Directory.EnumerateFiles(patches, "*_0.npy").Any())
            {
                await ShowMessageAsync(
                    "所选目录不是有效的基础加工目录：需要非空 machine.json 和 patches/*_0.npy。\n" +
                    directory);
                return;
            }
            _pipelinePmtPanel.BaseDirectory = directory;
            AppendPipelineLog($"已导入基础加工目录：{directory}");
            UpdatePipelineReadiness();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync($"无法读取基础加工目录：{exception.Message}");
        }
    }

    internal static async Task<bool> RunPreparedImportAsync(
        Func<CancellationToken, Task<PipelineImportSelection?>> selectAsync,
        string pickerFailureHeading,
        PreparedImportFlowActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectAsync);
        ArgumentException.ThrowIfNullOrWhiteSpace(pickerFailureHeading);
        ArgumentNullException.ThrowIfNull(actions);

        PipelineImportSelection? selection;
        try
        {
            selection = await selectAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await actions.ShowMessageAsync($"{pickerFailureHeading}：\n{error.Message}");
            return false;
        }

        if (selection is null)
            return false;

        var latestProgress = ImportProgressState.Scanning("正在扫描文件…");
        IProgress<ImportProgressState> progress = new InlineProgress<ImportProgressState>(state =>
        {
            latestProgress = state;
            actions.Update(state);
        });
        actions.Show(latestProgress);

        try
        {
            var (tiffs, dxfs) = selection.Discover();
            if (tiffs.Length == 0 && dxfs.Length == 0)
                throw new InvalidDataException(selection.EmptySelectionMessage);

            var prepared = await actions.PrepareAsync(
                tiffs, dxfs, progress, cancellationToken);
            using var preparedLayers = await actions.PrepareLayersAsync(
                prepared, cancellationToken);
            progress.Report(ImportProgressState.LoadingPreview(
                prepared.TotalCount,
                prepared.TotalCount,
                "正在加载预览…"));

            Action? commitDxfs = null;
            if (prepared.DxfPreviews.Count > 0)
                commitDxfs = actions.CreateDxfCommit(
                    prepared.DxfPreviews, selection.DxfDirectory);
            if (prepared.TiffInspections.Count > 0)
                actions.CommitTiffs(preparedLayers, selection.TiffDirectory);
            commitDxfs?.Invoke();

            var report = new StringBuilder();
            if (prepared.TiffInspections.Count > 0)
                report.AppendLine($"分层 TIFF：已导入 {prepared.TiffInspections.Count} 层。");
            if (prepared.DxfPreviews.Count > 0)
                report.AppendLine($"DXF：已导入 {prepared.DxfPreviews.Count} 层。");
            actions.AppendLog(selection.SuccessHeading);
            actions.AppendLog(report.ToString().TrimEnd());
            actions.AppendLog("");
            await actions.ShowSucceededAndCollapseAsync(
                ImportProgressState.Succeeded(prepared.TotalCount), cancellationToken);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            actions.AppendLog($"导入失败：{error.Message}");
            actions.ShowFailure(
                ImportProgressState.Failed(latestProgress.CurrentFileName, error.Message));
            return false;
        }
    }

    private PreparedImportFlowActions CreatePreparedImportFlowActions() => new(
        (tiffs, dxfs, progress, cancellationToken) =>
            PipelineImportPreparation.PrepareAsync(
                tiffs,
                dxfs,
                InspectTextureImageAsync,
                ValidateImportedDxf,
                progress,
                cancellationToken),
        (prepared, cancellationToken) => ImportLayerTiffsAsync(
            prepared.TiffInspections, cancellationToken),
        (preparedLayers, directory) =>
        {
            _pipelineTextureSurface.CommitPreparedLayers(preparedLayers);
            SelectSharedPreview(_pipelineSharedPreview, SharedPreviewKind.Texture);
            if (directory is not null)
                _pipelineLayerOutputBox.Text = directory;
        },
        CreatePipelineDxfCommit,
        _pipelineImportProgress.Show,
        _pipelineImportProgress.Update,
        _pipelineImportProgress.ShowSucceededAndCollapseAsync,
        _pipelineImportProgress.ShowFailure,
        AppendPipelineLog,
        ShowMessageAsync);

    /// <summary>预先解码已检查的 TIFF，但不改变当前可见预览。</summary>
    private Task<PreparedGrayscaleLayerSet> ImportLayerTiffsAsync(
        IReadOnlyList<KeyValuePair<string, TextureImageInspection>> inspections,
        CancellationToken cancellationToken) =>
        _pipelineTextureSurface.PrepareLayerFilesAsync(inspections, cancellationToken);

    /// <summary>验证单个 DXF；批次准备阶段会为错误补充文件名上下文。</summary>
    private static DxfPreviewControl.PreparedDxfPreview ValidateImportedDxf(string path) =>
        DxfPreviewControl.PrepareFile(path);

    /// <summary>纯准备：只检查 staged 数据并构造批次快照，不读取或改变任何真实 UI 状态。</summary>
    private Action CreatePipelineDxfCommit(
        IReadOnlyList<DxfPreviewControl.PreparedDxfPreview> previews,
        string? directory)
    {
        foreach (var preview in previews)
            DxfPreviewControl.EnsurePreparedFileInstallable(preview);
        var items = previews
            .Select((preview, index) => new DxfLayerPreviewItem(
                $"导入第 {index + 1:D2} 层 · {Path.GetFileName(preview.Path)}",
                preview))
            .ToArray();
        return () => CommitPipelineDxfImports(items, directory);
    }

    /// <summary>确定性发布已准备批次：不解析文件、不构造行组，也不触发层加载器。</summary>
    private void CommitPipelineDxfImports(
        IReadOnlyList<DxfLayerPreviewItem> items,
        string? directory)
    {
        var firstPreview = items[0].PreparedPreview!;
        _pipelineDxfPreview.ClearTexture();
        _pipelineDxfPreview.InstallPreparedFile(
            firstPreview, keepView: false, raiseViewChanged: false);
        _pipelineDxfPreviewStatus.Text = _pipelineDxfPreview.Summary;
        _pipelineDxfPreviewStatus.ClearValue(TextBlock.ForegroundProperty);
        _pipelineSharedPreview.UpdateDxfOverlayControls();
        _pipelineSharedPreview.Selection.ClearDxf();
        _pipelineDxfFiles.Clear();
        foreach (var item in items)
            _pipelineDxfFiles.Add(item);
        _pipelineDxfHost!.ReplaceItemsWithLoadedSelection(items, 0);
        _pipelineSharedPreview.Selection.CompleteDxfLoad();
        SelectSharedPreview(_pipelineSharedPreview, SharedPreviewKind.Dxf);
        if (directory is not null)
            _pipelineDxfOutputBox.Text = directory;
    }

    private static string? DirectoryOf(IReadOnlyList<string> files) =>
        files.Count == 0 ? null : Path.GetDirectoryName(files[0]);

    /// <summary>
    /// 清空所有已导入或生成的 TIFF 与 DXF 缓存：
    /// 纹理/分层预览、DXF 预览与层选择器全部释放。
    /// 只清内存状态——磁盘上的文件、用户手填的输入输出路径与各项参数都不动。
    /// </summary>
    private void ClearImportedArtifacts()
    {
        if (_cancellation is not null)
        {
            AppendPipelineLog("正在处理中，暂时无法清空缓存。\n");
            return;
        }

        _pipelineDxfFiles.Clear();
        _pipelineDxfHost?.SetItems(_pipelineDxfFiles);
        _pipelineDxfPreview.Clear();
        _pipelineDxfPreviewStatus.Text = _pipelineDxfPreview.Summary;
        _pipelineDxfPreviewStatus.Foreground = UiTheme.TextSecondaryBrush;
        _pipelineSharedPreview.Selection.ClearDxf();
        _pipelineSharedPreview.UpdateDxfOverlayControls();
        _pipelineTextureSurface.ClearAll();
        _pipelinePreviewController.Reset();
        RenderTexturePreview(_pipelineTextureSurface, _pipelinePreviewController.State);
        _pipelinePmtPreview.Clear();
        _pipelineSharedPreview.Selection.ClearPmt();
        _pipelinePmtDetails.LoadJob(null);
        _pipelinePmtLayoutPath = null;
        SelectSharedPreview(_pipelineSharedPreview, SharedPreviewKind.Texture);

        _lastMachineOutputPath = null;
        _lastLaserPmtOutputPath = null;
        _pipelineOpenButton.IsEnabled = false;

        AppendPipelineLog(
            "已清空缓存：导入/生成的 TIFF、DXF 与 PMT 预览全部释放（磁盘文件未受影响）。\n");
    }

    private void UpdatePmtSelectionDetails()
    {
        var job = _pipelinePmtPreview.SelectedJob;
        _pipelinePmtDetails.LoadJob(job);
    }

    private void OnPmtDetailsSaveRequested(object? sender, PmtDetailsSaveEventArgs args)
    {
        if (_pipelinePmtLayoutPath is null)
        {
            AppendPipelineLog("[警告] 未生成 PMT 布局，无法保存覆盖参数。\n");
            return;
        }
        try
        {
            LaserPmtLayoutWriter.UpdateJob(
                _pipelinePmtLayoutPath,
                args.JobIdentifier,
                args.Parameters);
            var layout = LaserPmtLayout.Load(_pipelinePmtLayoutPath);
            _pipelinePmtPreview.Load(layout);
            var refreshed = layout.Jobs
                .FirstOrDefault(job => string.Equals(
                    job.Identifier, args.JobIdentifier, StringComparison.Ordinal));
            if (refreshed is not null)
                _pipelinePmtDetails.LoadJob(refreshed);
            AppendPipelineLog(
                $"已保存 {args.JobIdentifier} 的覆盖参数到 PMT 布局；" +
                "再次执行第 4 步即可同步到对应单元机器文件。\n");
        }
        catch (Exception error) when (
            error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppendPipelineLog($"[错误] 保存 {args.JobIdentifier} 覆盖参数失败：{error.Message}\n");
            try
            {
                var layout = LaserPmtLayout.Load(_pipelinePmtLayoutPath);
                var reverted = layout.Jobs
                    .FirstOrDefault(job => string.Equals(
                        job.Identifier, args.JobIdentifier, StringComparison.Ordinal));
                _pipelinePmtDetails.LoadJob(reverted);
            }
            catch (Exception rollbackError)
            {
                AppendPipelineLog($"[错误] 回滚覆盖参数预览失败：{rollbackError.Message}\n");
            }
        }
    }

    private async Task PickPipelineFolderAsync(TextBox target, string title)
    {
        var path = await PickPipelineFolderPathAsync(title);
        if (path is not null)
            target.Text = path;
    }

    private async Task<string?> PickPipelineFolderPathAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }

    private async Task OnPipelineInputBoxLostFocusAsync()
    {
        var text = _pipelineInputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (TryGetFullPath(text) is not { } normalized)
            return;
        _pipelineInputBox.Text = normalized;
        if (File.Exists(normalized))
        {
            await LoadTexturePreviewAsync(
                normalized,
                _pipelineTextureSurface,
                _pipelineDpiBox,
                _pipelineWidthBox,
                _pipelineHeightBox,
                _pipelinePreviewController,
                _pipelineSharedPreview);
        }
    }

    private static void NormalizeDirectoryBox(TextBox box)
    {
        var text = box.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (TryGetFullPath(text) is { } normalized)
            box.Text = normalized;
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// 把用户选定的输出目录解析为实际输出目录：
    /// 若基础目录里已经存在目标产物（来自导入），则直接用它；
    /// 否则在基础目录下创建一层以源文件名为前缀的子目录，避免不同源文件混乱。
    /// </summary>
    private static string ResolvePipelineOutputDirectory(
        string input,
        string baseDirectory,
        string suffix,
        string existingFilePattern)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return baseDirectory;
        if (Directory.Exists(baseDirectory))
        {
            try
            {
                if (Directory.EnumerateFiles(baseDirectory, existingFilePattern)
                        .Any())
                    return baseDirectory;
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        var safeName = GetSafeFileName(Path.GetFileNameWithoutExtension(input));
        return Path.Combine(baseDirectory, $"{safeName}{suffix}");
    }

    private static string ResolveLayerOutputDirectory(string input, string baseDirectory) =>
        ResolvePipelineOutputDirectory(input, baseDirectory, "_layers", "layer_*.tiff");

    private static string ResolveDxfOutputDirectory(string input, string baseDirectory) =>
        ResolvePipelineOutputDirectory(input, baseDirectory, "_dxf", "layer_*.dxf");

    private static string ResolveMachineOutputParentDirectory(string input, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return baseDirectory;
        var safeName = GetSafeFileName(Path.GetFileNameWithoutExtension(input));
        return Path.Combine(baseDirectory, $"{safeName}_machine");
    }

    private static string GetSafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "output";
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        var result = builder.ToString().Trim('.', ' ');
        return string.IsNullOrWhiteSpace(result) ? "output" : result;
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
        var needsPmt = mode is PipelineRunMode.All or PipelineRunMode.LaserPmtOnly;
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
                "层间进给必须是 1–100000 μm 的整数，才能与 0.001 mm 的机器坐标精度一致。");
            return;
        }

        var scriptsDirectory = ApplicationLayout.GetScriptsDirectory(AppContext.BaseDirectory);
        var layerScript = Path.Combine(scriptsDirectory, "grayscale_layers.py");
        var hatchScript = Path.Combine(scriptsDirectory, "texture_to_hatch_dxf.py");
        var machineScript = Path.Combine(scriptsDirectory, "dxf_to_machine_file.py");
        var pmtScript = Path.Combine(scriptsDirectory, "laser_pmt.py");
        if ((needsLayers && !File.Exists(layerScript)) ||
            (needsDxf && !File.Exists(hatchScript)) ||
            (needsMachine && !File.Exists(machineScript)) ||
            (needsPmt && !File.Exists(pmtScript)))
        {
            await ShowMessageAsync(
                "找不到流程所需的 Python 脚本（grayscale_layers.py、texture_to_hatch_dxf.py、" +
                $"dxf_to_machine_file.py、laser_pmt.py）。请重新编译或发布应用。\n脚本目录：{scriptsDirectory}");
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

        var layerOutputActual = (needsLayers || needsDxf) && !string.IsNullOrWhiteSpace(layerOutput)
            ? ResolveLayerOutputDirectory(input ?? string.Empty, layerOutput)
            : layerOutput!;
        var dxfOutputActual = (needsDxf || needsMachine) && !string.IsNullOrWhiteSpace(dxfOutput)
            ? ResolveDxfOutputDirectory(input ?? string.Empty, dxfOutput)
            : dxfOutput!;
        var machineOutputParent = needsMachine && !string.IsNullOrWhiteSpace(dxfOutput)
            ? ResolveMachineOutputParentDirectory(input ?? string.Empty, dxfOutput)
            : null;

        var dxfOutputAbsolute = "";
        string machineOutputPath = "";
        string machineTempPath = "";
        string machineLockPath = "";
        string pmtTempPath = "";
        string pmtLockPath = "";
        string? machineOutputParentAbsolute = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(dxfOutputActual))
            {
                dxfOutputAbsolute = Path.GetFullPath(dxfOutputActual);
                if (needsMachine && machineOutputParent is not null)
                {
                    machineOutputParentAbsolute = Path.GetFullPath(machineOutputParent);
                    Directory.CreateDirectory(machineOutputParentAbsolute);
                    machineOutputPath = Path.Combine(machineOutputParentAbsolute, machineName!);
                    machineTempPath = Path.Combine(machineOutputParentAbsolute, $".{machineName}.building");
                    machineLockPath = Path.Combine(machineOutputParentAbsolute, $".{machineName}.lock");
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await ShowMessageAsync($"无法解析加工文件输出路径：{ex.Message}");
            return;
        }

        _lastMachineOutputPath = null;
        _lastLaserPmtOutputPath = null;
        _pipelineOpenButton.IsEnabled = false;

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
        UpdatePipelineReadiness();
        var pipelineBlocksBoxWasEnabled = _pipelineBlocksBox.IsEnabled;
        _pipelineRunSplitButton.IsEnabled = false;
        _pipelineImportButton.IsEnabled = false;
        _pipelineClearButton.IsEnabled = false;
        _pipelineBlocksBox.IsEnabled = false;
        _pipelineBlockCenterMotionBox.IsEnabled = false;
        _pipelineProgress.IsIndeterminate = true;
        _pipelineLogBox.Text = "";
        if (needsLayers || needsDxf)
        {
            _pipelineDxfPreview.Clear();
            _pipelineDxfPreviewStatus.Text = _pipelineDxfPreview.Summary;
            _pipelineSharedPreview.UpdateDxfOverlayControls();
            _pipelineSharedPreview.Selection.ClearDxf();
            _pipelineDxfFiles.Clear();
            _pipelineDxfHost?.SetItems(_pipelineDxfFiles);
        }
        if (needsPmt)
        {
            _pipelinePmtPreview.Clear();
            _pipelineSharedPreview.Selection.ClearPmt();
        }
        string[] layerFiles = [];
        var currentRunDxfFiles = new List<string>();
        string? latestPipelineFile = null;
        _pipelineRunProgress.Show(PipelineProgressState.Starting(mode == PipelineRunMode.All));
        try
        {
            if (needsLayers)
            {
                _pipelineRunProgress.Update(PipelineProgressState.Step(
                    PipelineProgressStage.Grayscale,
                    "正在执行第 1 步：灰度分层…",
                    mode == PipelineRunMode.All ? "步骤 1/4" : "第 1 步"));
                Directory.CreateDirectory(layerOutputActual);
                AppendPipelineLog(mode == PipelineRunMode.All
                    ? "步骤 1/4：开始生成灰度分层 TIFF…"
                    : "第 1 步：开始生成灰度分层 TIFF…");
                AppendPipelineLog($"输入：{input}");
                AppendPipelineLog($"分层目录：{layerOutputActual}");
                AppendPipelineLog($"灰阶区间：[{minLevel}, {maxLevel}]，分层数量：{layers}\n");

                var layerStartedAt = DateTime.UtcNow.AddSeconds(-2);
                var layerInfo = CreatePythonProcess(python);
                foreach (var argument in new[]
                {
                    layerScript, input!, layerOutputActual,
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
                    .EnumerateFiles(layerOutputActual, "layer_*.tiff")
                    .Where(path => File.GetLastWriteTimeUtc(path) >= layerStartedAt)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (layerFiles.Length != layers)
                    throw new InvalidOperationException(
                        $"预期生成 {layers} 个分层 TIFF，实际找到 {layerFiles.Length} 个。");

                AppendPipelineLog(mode == PipelineRunMode.All
                    ? $"\n步骤 1/4 完成：共生成 {layerFiles.Length} 个 TIFF。"
                    : $"\n第 1 步完成：共生成 {layerFiles.Length} 个 TIFF。");
                await RefreshPipelineLayersAsync(
                    layerOutputActual,
                    _cancellation.Token);
                if (mode == PipelineRunMode.GrayscaleOnly)
                {
                    await _pipelineRunProgress.ShowAndCollapseAsync(
                        PipelineProgressState.Succeeded(
                            $"已生成 {layerFiles.Length} 个分层 TIFF"),
                        CancellationToken.None);
                    return;
                }
            }

            if (needsDxf)
            {
                _pipelineRunProgress.Update(PipelineProgressState.Step(
                    PipelineProgressStage.Dxf,
                    "正在执行第 2 步：生成 DXF…",
                    mode == PipelineRunMode.All ? "步骤 2/4" : "第 2 步"));
                if (layerFiles.Length == 0)
                {
                    try
                    {
                        layerFiles = PipelineArtifactDiscovery.FindLayerTiffs(layerOutputActual);
                    }
                    catch (Exception error) when (
                        error is DirectoryNotFoundException or InvalidDataException)
                    {
                        throw new InvalidOperationException(
                            "第 2 步需要先在分层 TIFF 输出目录中生成至少一个 " +
                            $"layer_*.tiff 文件。{error.Message}",
                            error);
                    }
                }

                Directory.CreateDirectory(dxfOutputActual);
                AppendPipelineLog(mode == PipelineRunMode.All
                    ? "步骤 2/4：开始逐层生成 Hatch DXF…\n"
                    : "第 2 步：开始逐层生成 Hatch DXF…\n");
                var baseVoronoiSeed = (int)(_pipelineVoronoiSeedBox.Value ?? 12345);
                currentRunDxfFiles = new List<string>(layerFiles.Length);

                for (var index = 0; index < layerFiles.Length; index++)
                {
                    _cancellation.Token.ThrowIfCancellationRequested();
                    var layerFile = layerFiles[index];
                    latestPipelineFile = layerFile;
                    _pipelineRunProgress.Update(PipelineProgressState.DxfLayer(
                        index + 1,
                        layerFiles.Length,
                        layerFile,
                        mode == PipelineRunMode.All ? "步骤 2/4" : "第 2 步"));
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
                    _pipelineDxfHost?.SetItems(_pipelineDxfFiles);
                    _pipelineDxfHost?.SelectIndex(_pipelineDxfFiles.Count - 1);
                }

                AppendPipelineLog(mode == PipelineRunMode.All
                    ? $"\n步骤 2/4 完成：共生成 {layerFiles.Length} 个 DXF。"
                    : $"\n第 2 步完成：共生成 {layerFiles.Length} 个 DXF。");
                AppendPipelineLog($"DXF 目录：{dxfOutputActual}");

                // 生成阶段产物（DXF/预览 PNG/块元数据 JSON）默认落在同一目录；
                // 这里把它们归整到 DXF 目录下的 previews/ 与 metadata/ 子文件夹，
                // 避免文件混杂。DXF 预览与加工打包均会从子文件夹回读这些侧车。
                var reorganized = ReorganizeDxfCompanions(dxfOutputActual);
                if (reorganized.MovedPreviews.Count > 0 || reorganized.MovedMetadata.Count > 0)
                {
                    foreach (var moved in reorganized.MovedPreviews)
                        AppendPipelineLog($"预览 PNG → {moved}");
                    foreach (var moved in reorganized.MovedMetadata)
                        AppendPipelineLog($"块元数据 → {moved}");
                    // 同步更新内存中已加载的预览条目，使其指向子文件夹内的 PNG。
                    for (var i = 0; i < _pipelineDxfFiles.Count; i++)
                    {
                        var item = _pipelineDxfFiles[i];
                        if (item.TexturePath is not null)
                        {
                            var newTexture = Path.Combine(
                                reorganized.PreviewsDir,
                                Path.GetFileName(item.TexturePath));
                            if (File.Exists(newTexture))
                                _pipelineDxfFiles[i] = new DxfLayerPreviewItem(
                                    item.Name,
                                    item.DxfPath,
                                    newTexture,
                                    item.TextureRegistration);
                        }
                    }
                    _pipelineDxfHost?.SetItems(_pipelineDxfFiles);
                }
            }

            if (mode == PipelineRunMode.DxfOnly)
            {
                await _pipelineRunProgress.ShowAndCollapseAsync(
                    PipelineProgressState.Succeeded(
                        $"已生成 {currentRunDxfFiles.Count} 个 DXF"),
                    CancellationToken.None);
                return;
            }

            if (needsMachine)
            {
                latestPipelineFile = null;
                _pipelineRunProgress.Update(PipelineProgressState.Step(
                    PipelineProgressStage.Machine,
                    "正在执行第 3 步：生成加工文件…",
                    mode == PipelineRunMode.All ? "步骤 3/4" : "第 3 步"));
                if (currentRunDxfFiles.Count == 0)
                {
                    try
                    {
                        currentRunDxfFiles = PipelineArtifactDiscovery
                            .FindDxfFiles(dxfOutputActual)
                            .ToList();
                    }
                    catch (Exception error) when (
                        error is DirectoryNotFoundException or InvalidDataException)
                    {
                        throw new InvalidOperationException(
                            "第 3 步需要先在 DXF 输出目录中生成至少一个有效的 " +
                            $".dxf 文件。{error.Message}",
                            error);
                    }
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
                    ? "\n步骤 3/4：开始生成机器加工文件…"
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
                "--output-dir", machineOutputParentAbsolute ?? Path.GetDirectoryName(dxfOutputAbsolute)!,
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
                    ? "\n步骤 3/4 完成：加工文件生成成功。"
                    : "\n第 3 步完成：加工文件生成成功。");
                AppendPipelineLog($"加工文件目录：{machineOutputPath}");
                _pipelineOpenButton.IsEnabled = true;
                _pipelinePmtPanel.BaseDirectory = machineOutputPath;
                if (mode == PipelineRunMode.MachineOnly)
                {
                    await _pipelineRunProgress.ShowAndCollapseAsync(
                        PipelineProgressState.Succeeded("加工文件生成成功"),
                        CancellationToken.None);
                    return;
                }
            }

            if (needsPmt)
            {
                latestPipelineFile = null;
                _pipelineRunProgress.Update(PipelineProgressState.Step(
                    PipelineProgressStage.LaserPmt,
                    "正在执行第 4 步：生成 LaserPMT…",
                    mode == PipelineRunMode.All ? "步骤 4/4" : "第 4 步"));
                var baseMachineDirectory = mode == PipelineRunMode.All
                    ? machineOutputPath
                    : _pipelinePmtPanel.BaseDirectory;
                if (string.IsNullOrWhiteSpace(baseMachineDirectory) ||
                    !Directory.Exists(baseMachineDirectory))
                    throw new InvalidOperationException("第 4 步需要先生成或导入有效的基础加工目录。");
                _pipelinePmtPanel.BaseDirectory = baseMachineDirectory;
                var pmtOutputParent = Path.GetDirectoryName(Path.GetFullPath(baseMachineDirectory))
                    ?? throw new InvalidOperationException("无法确定 LaserPMT 输出目录。");
                var pmtOwnerToken = Guid.NewGuid().ToString("N");
                if (!_pipelinePmtPanel.TryBuildRequest(
                        pmtOutputParent,
                        pmtOwnerToken,
                        out var requestJson,
                        out var pmtOutputName,
                        out var pmtJobCount,
                        out var pmtError))
                    throw new InvalidOperationException(pmtError);
                var pmtOutputPath = Path.Combine(pmtOutputParent, pmtOutputName);
                pmtTempPath = Path.Combine(pmtOutputParent, $".{pmtOutputName}.building");
                pmtLockPath = Path.Combine(pmtOutputParent, $".{pmtOutputName}.lock");
                foreach (var collision in new[] { pmtOutputPath, pmtTempPath, pmtLockPath })
                    if (File.Exists(collision) || Directory.Exists(collision))
                        throw new IOException($"LaserPMT 输出路径已存在：{collision}");

                AppendPipelineLog(mode == PipelineRunMode.All
                    ? "\n步骤 4/4：开始生成 LaserPMT 参数矩阵…"
                    : "\n第 4 步：开始生成 LaserPMT 参数矩阵…");
                AppendPipelineLog($"基础加工目录：{baseMachineDirectory}");
                AppendPipelineLog($"参数组合数量：{pmtJobCount}");
                var requestPath = Path.Combine(
                    Path.GetTempPath(),
                    $"laserpmt-request-{pmtOwnerToken}.json");
                try
                {
                    File.WriteAllText(
                        requestPath,
                        requestJson,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    var pmtInfo = CreatePythonProcess(python);
                    pmtInfo.ArgumentList.Add(pmtScript);
                    pmtInfo.ArgumentList.Add(requestPath);
                    var pmtExitCode = await RunProcessAsync(
                        pmtInfo,
                        AppendPipelineLog,
                        _cancellation.Token);
                    if (pmtExitCode != 0)
                        throw new InvalidOperationException($"LaserPMT 生成失败，退出代码：{pmtExitCode}");
                }
                finally
                {
                    try
                    {
                        if (File.Exists(requestPath))
                            File.Delete(requestPath);
                    }
                    catch (IOException)
                    {
                        AppendPipelineLog($"临时请求文件未能删除，请手动检查：{requestPath}");
                    }
                }
                var layoutPath = Path.Combine(pmtOutputPath, "pmt-layout.json");
                _pipelinePmtLayoutPath = layoutPath;
                var layout = LaserPmtLayout.Load(layoutPath);
                if (layout.Jobs.Count != pmtJobCount)
                    throw new InvalidDataException("PMT 布局任务数量与请求不一致。");
                _pipelinePmtPreview.Load(layout);
                _pipelineSharedPreview.Selection.CompletePmtLoad();
                SelectSharedPreview(_pipelineSharedPreview, SharedPreviewKind.Pmt);
                _lastLaserPmtOutputPath = pmtOutputPath;
                _pipelinePmtPanel.OutputName = pmtOutputName;
                _pipelineOpenButton.IsEnabled = true;
                AppendPipelineLog(mode == PipelineRunMode.All
                    ? $"\n四步流程完成：已生成 {layerFiles.Length} 个 TIFF、" +
                      $"{layerFiles.Length} 个 DXF、1 个基础加工文件和 {pmtJobCount} 个 LaserPMT 编号文件。"
                    : $"\n第 4 步完成：已生成 {pmtJobCount} 个 LaserPMT 编号文件。");
                AppendPipelineLog($"LaserPMT 目录：{pmtOutputPath}");
                await _pipelineRunProgress.ShowAndCollapseAsync(
                    PipelineProgressState.Succeeded(
                        mode == PipelineRunMode.All
                            ? "全部四步文件生成流程已完成"
                            : "LaserPMT 生成成功"),
                    CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            AppendPipelineLog("\n操作已取消。");
            AppendPipelineLog(
                "为避免路径替换竞态误删其他任务的数据，程序不会自动删除生成残留；" +
                "请确认没有生成进程仍在运行后再手动检查以下路径：");
            if (!string.IsNullOrWhiteSpace(machineTempPath))
            {
                AppendPipelineLog($"基础加工临时目录：{machineTempPath}");
                AppendPipelineLog($"基础加工锁文件：{machineLockPath}");
            }
            if (!string.IsNullOrWhiteSpace(pmtTempPath))
            {
                AppendPipelineLog($"LaserPMT 临时目录：{pmtTempPath}");
                AppendPipelineLog($"LaserPMT 锁文件：{pmtLockPath}");
            }
            await _pipelineRunProgress.ShowAndCollapseAsync(
                PipelineProgressState.Cancelled(),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppendPipelineLog($"\n流程失败：{ex.Message}");
            _pipelineRunProgress.ShowFailure(
                PipelineProgressState.Failed(latestPipelineFile, ex.Message));
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _pipelineRunSplitButton.IsEnabled = true;
            _pipelineImportButton.IsEnabled = true;
            _pipelineClearButton.IsEnabled = true;
            _pipelineBlocksBox.IsEnabled = pipelineBlocksBoxWasEnabled;
            UpdateBlockCenterMotionAvailability();
            _pipelineProgress.IsIndeterminate = false;
            UpdatePipelineReadiness();
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
                ResolveBlockMetadataPath(dxfPath),
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

    /// <summary>
    /// 解析与某 DXF 配套的块元数据 JSON 路径：优先查 DXF 同级的
    /// <c>metadata/</c> 子目录（新生成产物），缺失时回退到与 DXF 同目录（导入/旧版）。
    /// </summary>
    private static string ResolveBlockMetadataPath(string dxfPath)
    {
        var subfolder = Path.Combine(
            Path.GetDirectoryName(dxfPath) ?? string.Empty,
            "metadata",
            Path.GetFileName(Path.ChangeExtension(dxfPath, ".blocks.json")));
        return IsRegularNonEmptyFile(subfolder)
            ? subfolder
            : Path.ChangeExtension(dxfPath, ".blocks.json");
    }

    private sealed record DxfCompanionReorganization(
        string PreviewsDir,
        IReadOnlyList<string> MovedPreviews,
        IReadOnlyList<string> MovedMetadata);

    /// <summary>
    /// 把 DXF 目录中散落的预览 PNG 与块元数据 JSON 分别归整到
    /// <c>previews/</c> 与 <c>metadata/</c> 子文件夹，避免与 DXF 混杂。
    /// 仅移动目录顶层文件，子文件夹内已有文件不受影响（可重复运行）。
    /// </summary>
    private static DxfCompanionReorganization ReorganizeDxfCompanions(string dxfDirectory)
    {
        var previewsDir = Path.Combine(dxfDirectory, "previews");
        var metadataDir = Path.Combine(dxfDirectory, "metadata");
        var movedPreviews = new List<string>();
        var movedMetadata = new List<string>();

        foreach (var source in Directory.EnumerateFiles(dxfDirectory, "*.preview.png"))
        {
            Directory.CreateDirectory(previewsDir);
            var destination = Path.Combine(previewsDir, Path.GetFileName(source));
            if (!string.Equals(
                    Path.GetFullPath(source),
                    Path.GetFullPath(destination),
                    StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(destination))
                    File.Delete(destination);
                File.Move(source, destination);
            }
            movedPreviews.Add(Path.GetRelativePath(dxfDirectory, destination));
        }

        foreach (var source in Directory.EnumerateFiles(dxfDirectory, "*.blocks.json"))
        {
            Directory.CreateDirectory(metadataDir);
            var destination = Path.Combine(metadataDir, Path.GetFileName(source));
            if (!string.Equals(
                    Path.GetFullPath(source),
                    Path.GetFullPath(destination),
                    StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(destination))
                    File.Delete(destination);
                File.Move(source, destination);
            }
            movedMetadata.Add(Path.GetRelativePath(dxfDirectory, destination));
        }

        return new DxfCompanionReorganization(previewsDir, movedPreviews, movedMetadata);
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
        info.ArgumentList.Add(ApplicationLayout.GetScriptPath(
            AppContext.BaseDirectory,
            "texture_to_hatch_dxf.py"));
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
        _pipelinePreviewController.Dispose();
        _pipelineTextureSurface.Dispose();
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

    private static bool LoadDxfPreview(
        DxfPreviewControl preview,
        TextBlock status,
        string path,
        bool keepView = false)
    {
        try
        {
            preview.LoadFile(path, keepView);
            status.Text = preview.Summary;
            status.ClearValue(TextBlock.ForegroundProperty);
            return true;
        }
        catch (Exception ex)
        {
            status.Text = $"无法预览 {Path.GetFileName(path)}：{ex.Message}";
            status.Foreground = UiTheme.DangerTextBrush;
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
            _pipelineDxfHost?.SetItems(_pipelineDxfFiles);
            _pipelineDxfHost?.SelectItem(item);
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

    private void AppendPipelineLog(string text) =>
        Dispatcher.UIThread.Post(() =>
        {
            _pipelineLogBox.Text += text + Environment.NewLine;
            _pipelineLogBox.CaretIndex = _pipelineLogBox.Text?.Length ?? 0;
        });

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
