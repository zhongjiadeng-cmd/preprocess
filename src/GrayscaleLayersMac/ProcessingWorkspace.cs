using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace GrayscaleLayersMac;

/// <summary>Native processing graph shell. The inspector edits snapshots, never process arguments directly.</summary>
public sealed class ProcessingWorkspace : UserControl
{
    public ProcessingSession Session { get; } = new();
    public ProcessingCanvas Canvas { get; }
    private readonly StackPanel _inspector = new() { Spacing = 7, Margin = new Thickness(14, 10, 14, 18) };
    private readonly TextBlock _inspectorTitle = new() { Text = "工作流", FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _inspectorDetail = new() { Text = "项目概览", FontSize = 11, Foreground = UiTheme.TextSecondaryBrush, Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBlock _message = new() { Text = "就绪 · 拖入纹理开始", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _output = new() { MaxWidth = 260, FontSize = 11, Foreground = UiTheme.TextSecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _projectName = new() { Text = "未命名项目", FontSize = 12, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _selectionSummary = new() { FontSize = 11, Foreground = UiTheme.TextSecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, HashSet<string>> _layerSelection = new();
    private ProcessingExecutor? _executor;
    private CancellationTokenSource? _run;
    private string? _projectPath;
    private Button _runButton = null!;
    private Button _cancelButton = null!;
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Button _matrixButton = null!;
    private string? _inspected;
    private long _inspectedRevision = -1;
    private string _savedJson = "";
    private bool _layoutExpanded;
    private readonly Dictionary<string, bool> _expandedSections = new();
    public ProcessingWorkspace()
    {
        Canvas = new ProcessingCanvas(Session);
        Canvas.SelectedLayers = id => _layerSelection.GetValueOrDefault(id)?.ToArray() ?? [];
        Canvas.Error += Show;
        Canvas.FileDropped += (path, point) => Safe(() => ImportFile(path, point));
        Canvas.Shortcut += action => _ = KeyboardAsync(action);
        Session.Changed += Refresh;
        var files = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 5) };
        var fileActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        fileActions.Children.Add(Command("打开", UiIcon.OpenFolder, async () => await OpenAsync(), "打开节点工作流项目"));
        fileActions.Children.Add(Command("保存", UiIcon.Save, async () => await SaveAsync(), "保存节点、连接与画布布局"));
        fileActions.Children.Add(Command("导入 Machine", UiIcon.Source, async () => await ImportMachineAsync(), "导入包含 machine.json 的加工目录"));
        fileActions.Children.Add(Divider()); files.Children.Add(fileActions);
        _projectName.Margin = new Thickness(10, 0); Grid.SetColumn(_projectName, 1); files.Children.Add(_projectName);
        var outputActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        outputActions.Children.Add(_output);
        outputActions.Children.Add(Command("输出目录", UiIcon.OpenFolder, async () => await PickOutputAsync(), "设置纹理处理与 PMT 输出目录"));
        Grid.SetColumn(outputActions, 2); files.Children.Add(outputActions);

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 6) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        tools.Children.Add(new TextBlock { Text = "添加节点", FontSize = 11, Foreground = UiTheme.TextFaintBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        tools.Children.Add(Command("纹理", UiIcon.Import, async () => await PickTextureAsync(), "导入 TIFF、PNG、JPEG 或 BMP 纹理"));
        tools.Children.Add(Command("灰度分层", () => Add(ProcessingNodeKind.Grayscale)));
        tools.Children.Add(Command("DXF Hatch", () => Add(ProcessingNodeKind.Hatch)));
        tools.Children.Add(Command("Machine", () => Add(ProcessingNodeKind.Machine)));
        tools.Children.Add(Command("PMT", () => Add(ProcessingNodeKind.Pmt)));
        tools.Children.Add(Divider());
        _matrixButton = Command("矩阵", UiIcon.PmtMatrix, ShowMatrix, "选中单个 PMT，以它为模板创建矩阵工件"); tools.Children.Add(_matrixButton);
        tools.Children.Add(Divider());
        _undoButton = IconButton(UiIcon.Undo, "撤销 · ⌘/Ctrl Z", Session.Undo); tools.Children.Add(_undoButton);
        _redoButton = IconButton(UiIcon.Undo, "重做 · ⌘/Ctrl Shift Z", Session.Redo);
        ((Control)_redoButton.Content!).RenderTransform = new ScaleTransform(-1, 1); tools.Children.Add(_redoButton);
        tools.Children.Add(IconButton(UiIcon.Fit, "适应全部节点 · F", Canvas.FitAll));
        toolbar.Children.Add(new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var runActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 0, 0, 0) };
        _runButton = Button("运行选中节点", async () => await RunAsync()); UiTheme.ApplyPrimaryStyle(_runButton); _runButton.Height = UiTheme.ControlHeight; _runButton.FontSize = 12;
        ToolTip.SetTip(_runButton, "按连接顺序运行上游，直到当前选中的节点"); runActions.Children.Add(_runButton);
        _cancelButton = Command("停止", () => _run?.Cancel()); _cancelButton.IsEnabled = false; runActions.Children.Add(_cancelButton);
        Grid.SetColumn(runActions, 1); toolbar.Children.Add(runActions);
        var main = new Grid { ColumnDefinitions = WorkspacePanelLayout.Columns(320, 300, .75) };
        main.Children.Add(Canvas);
        var splitter = UiTheme.WorkspaceSplitter();
        Grid.SetColumn(splitter, 1); main.Children.Add(splitter);
        var inspectorContent = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        inspectorContent.Children.Add(new Border { Padding = new Thickness(14, 11), BorderBrush = UiTheme.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 0, 1), Background = UiTheme.BarBrush,
            Child = new StackPanel { Children = { _inspectorTitle, _inspectorDetail } } });
        var inspectorScroll = new ScrollViewer { Content = _inspector, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(inspectorScroll, 1); inspectorContent.Children.Add(inspectorScroll);
        var inspector = new Border { BorderBrush = UiTheme.BorderSubtleBrush, BorderThickness = new Thickness(1, 0, 0, 0), Background = UiTheme.PanelBrush, Child = inspectorContent };
        Grid.SetColumn(inspector, 2); main.Children.Add(inspector);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        root.Children.Add(CommandBar(files, UiTheme.HeaderBrush)); var toolBarBorder = CommandBar(toolbar, UiTheme.BarBrush); Grid.SetRow(toolBarBorder, 1); root.Children.Add(toolBarBorder); Grid.SetRow(main, 2); root.Children.Add(main);
        var statusContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; statusContent.Children.Add(_message);
        _selectionSummary.Margin = new Thickness(20, 0, 0, 0); Grid.SetColumn(_selectionSummary, 1); statusContent.Children.Add(_selectionSummary);
        var status = new Border { Padding = new Thickness(12, 6), BorderBrush = UiTheme.BorderSubtleBrush, BorderThickness = new Thickness(0, 1, 0, 0), Background = UiTheme.BarBrush, Child = statusContent };
        Grid.SetRow(status, 3); root.Children.Add(status); Content = root;
        _savedJson = Session.Document.ToJson(); Refresh();
        DetachedFromVisualTree += (_, _) => { _run?.Cancel(); Canvas.ClearImages(); };
    }
    private Button Button(string label, Action action)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center };
        UiTheme.ApplySecondaryStyle(button, small: true);
        button.Click += (_, _) => Safe(action);
        return button;
    }
    private Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center };
        UiTheme.ApplySecondaryStyle(button, small: true);
        button.Click += async (_, _) => { try { await action(); } catch (Exception error) { Show(error.Message); } };
        return button;
    }
    private static Border CommandBar(Control child, IBrush background) => new() { Child = child, Background = background, BorderBrush = UiTheme.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 0, 1) };
    private static Border Divider() => new() { Width = 1, Height = 18, Margin = new Thickness(7, 0), Background = UiTheme.BorderSubtleBrush, VerticalAlignment = VerticalAlignment.Center };
    private Button Command(string text, Action action)
    {
        var button = Button(text, action); button.Classes.Remove("btn-secondary"); UiTheme.ApplyQuietStyle(button, small: true); return button;
    }
    private Button Command(string text, UiIcon icon, Action action, string tip)
    {
        var button = Command(text, action); button.Content = UiIcons.Labeled(icon, text); ToolTip.SetTip(button, tip); return button;
    }
    private Button Command(string text, UiIcon icon, Func<Task> action, string tip)
    {
        var button = Button(text, action); button.Classes.Remove("btn-secondary"); UiTheme.ApplyQuietStyle(button, small: true); button.Content = UiIcons.Labeled(icon, text); ToolTip.SetTip(button, tip); return button;
    }
    private Button IconButton(UiIcon icon, string tip, Action action)
    {
        var button = Button(tip, action); button.Classes.Remove("btn-secondary"); UiTheme.ApplyIconStyle(button, tip); button.Content = UiIcons.CreateSmall(icon); ToolTip.SetTip(button, tip); return button;
    }
    private void Safe(Action action) { try { action(); } catch (Exception error) { Show(error.Message); } }
    private void Show(string text)
    {
        _message.Text = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "就绪";
        ToolTip.SetTip(_message, text);
    }
    private TopLevel Host => TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("窗口未就绪。");
    private ProcessingNode? SelectedNode => Session.Selection.Count == 1 ? Session.Document.Nodes.FirstOrDefault(n => n.Id == Session.Selection.Single()) : null;
    private void Add(ProcessingNodeKind kind)
    {
        var previous = SelectedNode;
        var position = previous is null ? Canvas.VisibleCenter : new Point(previous.X + previous.CanvasWidth + 60, previous.Y);
        var node = Session.Add(kind, position.X, position.Y);
        if (previous is not null && ProcessingDocument.CanConnect(previous.Kind, kind))
            Session.Connect(previous.Id, node.Id, null);
        Canvas.FitAll(); Canvas.Focus();
    }
    private void ImportFile(string path, Point point)
    {
        if (!new[] { ".tif", ".tiff", ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            throw new InvalidOperationException("请选择 TIFF、PNG、JPEG 或 BMP 纹理。");
        Session.Add(ProcessingNodeKind.Texture, point.X, point.Y, Path.GetFullPath(path));
        _ = InspectTextureAsync(Session.Document.Nodes.Last().Id);
    }
    private async Task PickTextureAsync()
    {
        var files = await Host.StorageProvider.OpenFilePickerAsync(new() { Title = "导入纹理", AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("纹理图片") { Patterns = ["*.tif", "*.tiff", "*.png", "*.jpg", "*.jpeg", "*.bmp"] }] });
        var point = Canvas.VisibleCenter;
        foreach (var file in files) if (file.TryGetLocalPath() is { } path) { ImportFile(path, point); point += new Vector(40, 40); }
        Canvas.FitAll();
    }
    private async Task InspectTextureAsync(string id)
    {
        try
        {
            var node = Session.Document.Node(id);
            var python = await PythonProcessingRunner.FindPythonAsync(CancellationToken.None);
            var runner = new PythonProcessingRunner(python, ApplicationLayout.GetScriptsDirectory(AppContext.BaseDirectory));
            var directory = Path.Combine(Path.GetTempPath(), "pmt-preview-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var result = await runner.RunAsync(node, null, directory, CancellationToken.None);
            await EnsureExecutorAsync();
            if (Session.Document.Nodes.Any(n => n.Id == id && n.Setting("path") == node.Setting("path")))
            {
                _executor!.States[id] = new(ProcessingExecutor.Configuration(Session.Document, id), "预览就绪", result);
                Canvas.InvalidateVisual(); _inspectedRevision = -1; Refresh();
            }
        }
        catch (Exception error) { Show(error.Message); }
    }
    private async Task ImportMachineAsync()
    {
        var folders = await Host.StorageProvider.OpenFolderPickerAsync(new() { Title = "导入包含 machine.json 与 patches 的目录" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        if (!File.Exists(Path.Combine(path, "machine.json"))) throw new InvalidOperationException("目录中没有 machine.json。");
        var p = Canvas.VisibleCenter; Session.Add(ProcessingNodeKind.Machine, p.X, p.Y, path); Canvas.FitAll();
    }
    private async Task<bool> PickOutputAsync()
    {
        var folders = await Host.StorageProvider.OpenFolderPickerAsync(new() { Title = "选择工作流输出目录" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return false;
        Session.Edit(d => d with { OutputDirectory = path }); return true;
    }
    private async Task EnsureExecutorAsync()
    {
        if (_executor is not null) return;
        var python = await PythonProcessingRunner.FindPythonAsync(_run?.Token ?? CancellationToken.None);
        if (_executor is not null) return;
        _executor = new(new PythonProcessingRunner(python, ApplicationLayout.GetScriptsDirectory(AppContext.BaseDirectory), text => Dispatcher.UIThread.Post(() => Show(text))));
        _executor.Changed += () => Dispatcher.UIThread.Post(() => { _inspectedRevision = -1; Refresh(); });
        Canvas.Executor = _executor;
    }
    private async Task RunAsync()
    {
        var node = SelectedNode ?? throw new InvalidOperationException("请选中需要运行到的节点。");
        if (_run is not null) return;
        if (string.IsNullOrWhiteSpace(Session.Document.OutputDirectory) && !await PickOutputAsync()) return;
        _run = new(); _runButton.IsEnabled = false; _cancelButton.IsEnabled = true;
        try
        {
            await EnsureExecutorAsync();
            var snapshot = ProcessingDocument.FromJson(Session.Document.ToJson());
            Show($"正在运行到 {node.Name}…");
            var result = await _executor!.RunAsync(snapshot, node.Id, _run.Token);
            var unchanged = Session.Document.Nodes.Any(n => n.Id == node.Id) && ProcessingExecutor.Configuration(snapshot, node.Id) == ProcessingExecutor.Configuration(Session.Document, node.Id);
            Show(unchanged ? $"已完成：{result.PackageDirectory ?? result.MachineDirectory ?? Path.GetDirectoryName(result.Layers.FirstOrDefault()?.Path)}" : "此轮已完成，但配置已修改；请重新运行以更新结果。");
        }
        catch (OperationCanceledException) { Show("运行已取消。已完成的结果保留，未完成节点可重新运行。"); }
        finally { _run.Dispose(); _run = null; _runButton.IsEnabled = true; _cancelButton.IsEnabled = false; _inspectedRevision = -1; Refresh(); }
    }
    private async Task SaveAsync()
    {
        var file = await Host.StorageProvider.SaveFilePickerAsync(new() { Title = "保存节点工作流", SuggestedFileName = Path.GetFileName(_projectPath ?? "texture.pmt-workflow.json"),
            DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("节点工作流") { Patterns = ["*.json"] }], ShowOverwritePrompt = true });
        if (file?.TryGetLocalPath() is not { } path) return;
        var snapshot = Session.Document.ToJson();
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, snapshot); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        _projectPath = path; _savedJson = snapshot; Refresh(); Show("项目已保存：" + path);
    }
    private async Task OpenAsync()
    {
        if (_run is not null) throw new InvalidOperationException("请先取消或等待当前运行结束。");
        if (Session.Document.Nodes.Length > 0 && Session.Document.ToJson() != _savedJson)
        {
            var dialog = new WorkspaceConfirmDialog("打开其他项目？", "当前项目有未保存的修改。继续打开将放弃这些修改。", "放弃并打开");
            if (!await dialog.ShowDialog<bool>((Window)Host)) return;
        }
        var files = await Host.StorageProvider.OpenFilePickerAsync(new() { Title = "打开节点工作流", FileTypeFilter = [new FilePickerFileType("节点工作流") { Patterns = ["*.json"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        var document = ProcessingDocument.FromJson(await File.ReadAllTextAsync(path));
        _executor?.Clear(); Canvas.ClearImages(); _layerSelection.Clear(); Session.Open(document); _projectPath = path; _savedJson = document.ToJson();
        Refresh();
        Show("项目已打开；运行节点时会重新核对来源并生成结果。");
    }
    private async Task KeyboardAsync(string action)
    {
        try
        {
            switch (action)
            {
                case "undo": Session.Undo(); break;
                case "redo": Session.Redo(); break;
                case "delete": Session.Delete(); break;
                case "copy":
                    if (Session.Copy() is { } json && Host.Clipboard is { } clipboard) await clipboard.SetTextAsync(json);
                    break;
                case "paste":
#pragma warning disable CS0618
                    var text = Host.Clipboard is { } clip ? await clip.GetTextAsync() : null;
#pragma warning restore CS0618
                    Session.Paste(text); break;
                case "all": Session.Selection.UnionWith(Session.Document.Nodes.Select(n => n.Id)); Canvas.InvalidateVisual(); break;
            }
        }
        catch (Exception error) { Show(error.Message); }
    }
    private void Refresh()
    {
        Canvas.InvalidateVisual(); _output.Text = Session.Document.OutputDirectory.Length == 0 ? "尚未设置输出目录" : Session.Document.OutputDirectory;
        ToolTip.SetTip(_output, Session.Document.OutputDirectory);
        _projectName.Text = (_projectPath is null ? "未命名项目" : Path.GetFileNameWithoutExtension(_projectPath)) + (Session.Document.ToJson() == _savedJson ? "" : " · 未保存");
        _selectionSummary.Text = Session.Selection.Count == 0 ? "拖动节点 · 右下角缩放 · F 适应全部" : $"已选 {Session.Selection.Count} 项 · Shift 多选";
        _runButton.IsEnabled = _run is null && SelectedNode is not null;
        _undoButton.IsEnabled = Session.CanUndo; _redoButton.IsEnabled = Session.CanRedo;
        _matrixButton.IsEnabled = SelectedNode is { Kind: ProcessingNodeKind.Pmt } pmtNode &&
            (pmtNode.Instances.Length == 1 || pmtNode.Instances.Any(i => i.Id == Session.SelectedInstanceId));
        var selected = Session.Selection.SingleOrDefaultIfOne();
        var key = selected + ":" + Session.SelectedInstanceId;
        if (_inspected == key && _inspectedRevision == Session.Revision) return;
        _inspected = key; _inspectedRevision = Session.Revision;
        _inspector.Children.Clear();
        var node = SelectedNode;
        _inspectorTitle.Text = node?.Name ?? (Session.Selection.Count > 1 ? $"已选 {Session.Selection.Count} 项" : "工作流");
        _inspectorDetail.Text = node?.Kind switch
        {
            ProcessingNodeKind.Texture => "纹理源 / 图像输入", ProcessingNodeKind.Grayscale => "灰度分层 / 阈值处理",
            ProcessingNodeKind.Hatch => "DXF Hatch / 路径与分块", ProcessingNodeKind.Machine => "Machine / 加工参数",
            ProcessingNodeKind.Pmt => "PMT / 工件布局", _ => "项目与操作说明"
        };
        if (node is null)
        {
            var edge = Session.Document.Connections.FirstOrDefault(c => c.Id == selected);
            if (edge is not null)
            {
                _inspectorTitle.Text = "连接属性"; _inspectorDetail.Text = "传递范围 / 层选择";
                Note(Session.Document.Node(edge.SourceId).Name + " → " + Session.Document.Node(edge.TargetId).Name);
                Layers(Session.Document.Node(edge.SourceId), edge);
                _inspector.Children.Add(Button("删除连接", Session.Delete));
            }
            else
            {
                Note("选择节点以编辑参数，或导入纹理开始。");
                Fold("workflow-help", "操作与快捷键", () => Note("拖动节点移动，右下角调整大小。Shift 点击多选。\n\n拖动空白画布或按鼠标中键平移，滚轮缩放，F 适应全部。\n\n从输出圆点拖到下游输入圆点。选中单个 PMT 后可创建矩阵。\n\n⌘/Ctrl C、V 复制粘贴\n⌘/Ctrl Z 撤销\n⌘/Ctrl Shift Z 重做\nEsc 取消拖动\nDelete 删除选中对象"));
            }
            return;
        }
        Heading("节点属性");
        Field("名称", node.Name, value => Session.UpdateNode(Session.Document.Node(node.Id) with { Name = value }));
        CanvasLayout(node);
        var state = _executor?.States.GetValueOrDefault(node.Id);
        if (state is not null)
        {
            Note(_executor!.Status(Session.Document, node.Id) + (state.Error.Length > 0 ? "\n" + state.Error : ""));
            if (state.Result is { } result)
            {
                if (result.Info.Length > 0) Fold(node.Id + ":result", "运行详情", () => Note(result.Info + (result.Width > 0 && result.Height > 0 ? $"\n{result.Width:0.##} × {result.Height:0.##} mm" : "")));
                if ((result.PackageDirectory ?? result.MachineDirectory) is { } path)
                    _inspector.Children.Add(Button("打开结果目录", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })));
            }
        }
        if (node.Kind is ProcessingNodeKind.Grayscale or ProcessingNodeKind.Hatch) Layers(node, null);
        if (node.Kind == ProcessingNodeKind.Texture)
        {
            Heading("图像输入");
            var pathLabel = new TextBlock { Text = Path.GetFileName(node.Setting("path")), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, Foreground = UiTheme.TextSecondaryBrush };
            ToolTip.SetTip(pathLabel, node.Setting("path")); _inspector.Children.Add(pathLabel); Setting(node, "dpi", "备用 DPI");
        }
        if (node.Kind == ProcessingNodeKind.Grayscale)
        {
            Heading("分层参数");
            Setting(node, "layers", "分层数量"); Setting(node, "min-level", "灰阶下限"); Setting(node, "max-level", "灰阶上限"); Toggle(node, "below-is-white", "低于阈值设为白色");
        }
        if (node.Kind == ProcessingNodeKind.Hatch)
        {
            Heading("PMT 输出");
            var allPmt = Button("全部层 → PMT", () => CreatePmt(node, null));
            var selectedPmt = Button("选中层 → PMT", () =>
            {
                var layers = _layerSelection.GetValueOrDefault(node.Id)?.ToArray() ?? [];
                if (layers.Length == 0) throw new InvalidOperationException("请先勾选需要的层。"); CreatePmt(node, layers);
            });
            ToolTip.SetTip(selectedPmt, "勾选输出层后，将所选层合并为单个 PMT");
            AddActionPair(allPmt, selectedPmt);
            Heading("路径与分块");
            foreach (var (keyName, label) in new[] { ("width", "宽度 mm"), ("height", "高度 mm"), ("spacing", "Hatch 间距 mm"), ("angle-step", "层间角度递进 °"), ("blocks", "分块数量") }) Setting(node, keyName, label);
            Fold(node.Id + ":hatch-advanced", "高级分块与纹理", () =>
            {
                foreach (var (keyName, label) in new[] { ("min-area-percent", "最小块面积 %"), ("max-area-percent", "最大块面积 %"), ("boundary-blur", "边界扩散 mm"), ("boundary-correlation", "连续变化长度 mm"), ("seed", "随机种子"), ("threshold", "二值阈值") }) Setting(node, keyName, label);
                Choice(node, "anchor", "裁剪基准", ["center", "top-left"]); Choice(node, "tile-mode", "纹理平铺", ["unit", "repeat", "mirror"]);
                Toggle(node, "bidirectional", "往返填充"); Toggle(node, "border", "包含检查边框");
                Note("分块数量为 0 时关闭分块。");
            });

        }
        if (node.Kind == ProcessingNodeKind.Machine)
        {
            Heading("加工参数");
            if (node.Setting("path").Length > 0) Fold(node.Id + ":machine-source", "来源与参数继承", () => Note("导入来源：" + node.Setting("path") + "\n参数来自已有加工文件；可在下游 PMT 中覆盖。"));
            if (node.Setting("path").Length == 0)
            {
                Setting(node, "layer-step-um", "层间进给 μm"); Toggle(node, "block-center-positioning", "按加工块中心移动 XY");
                void Parameter(LaserPmtParameterDefinition definition)
                {
                    var option = PythonProcessingRunner.LaserOptions[definition.Name];
                    var caption = definition.DisplayName.Split('（')[0];
                    if (definition.IsBoolean) Toggle(node, option, caption, true);
                    else Setting(node, option, caption);
                }
                foreach (var definition in LaserPmtConfiguration.Parameters.Where(p => p.Name is "power" or "frequency" or "scanSpeed" or "pulseWidthIdx")) Parameter(definition);
                Fold(node.Id + ":laser-advanced", "高级激光参数", () =>
                {
                    foreach (var definition in LaserPmtConfiguration.Parameters.Where(p => p.Name is not ("layerFeedUm" or "power" or "frequency" or "scanSpeed" or "pulseWidthIdx"))) Parameter(definition);
                });
            }
            _inspector.Children.Add(Button("创建 PMT", () =>
            {
                var result = state?.Result;
                var pmt = Session.Add(ProcessingNodeKind.Pmt, node.X + node.CanvasWidth + 60, node.Y);
                if (result is not null && result.Width > 0 && result.Height > 0)
                    Session.UpdateNode(pmt with { WorkpieceWidth = result.Width + 10, WorkpieceHeight = result.Height + 10,
                        Instances = pmt.Instances.Select(i => i with { Width = result.Width, Height = result.Height }).ToArray() });
                Session.Connect(node.Id, pmt.Id, null);
            }));
        }
        if (node.Kind == ProcessingNodeKind.Pmt) PmtInspector(node);
        Heading("节点操作");
        AddActionPair(Button("运行到此节点", async () => await RunAsync()), Button("删除选中项", Session.Delete));
    }
    private void CreatePmt(ProcessingNode node, string[]? layers)
    {
        Session.CreatePmt(node.Id, layers, PythonProcessingRunner.Number(node, "width"), PythonProcessingRunner.Number(node, "height")); Canvas.FitAll();
    }
    private void Heading(string text) => _inspector.Children.Add(new Border
    {
        BorderBrush = UiTheme.BorderSubtleBrush, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 8, 0, 7), Margin = new Thickness(0, 2, 0, 2),
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = UiTheme.TextSecondaryBrush }
    });
    private void Note(string text) => _inspector.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.TextSecondaryBrush, FontSize = 11 });
    private void AddActionPair(Button first, Button second)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        first.HorizontalAlignment = second.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.Children.Add(first); Grid.SetColumn(second, 1); row.Children.Add(second); _inspector.Children.Add(row);
    }
    private void Fold(string key, string title, Action build)
    {
        var start = _inspector.Children.Count;
        build();
        var content = new StackPanel { Spacing = 7, Margin = new Thickness(10) };
        foreach (var child in _inspector.Children.Skip(start).ToArray()) { _inspector.Children.Remove(child); content.Children.Add(child); }
        var expander = UiTheme.StyleExpander(new Expander
        {
            Header = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.Medium }, Content = content,
            IsExpanded = _expandedSections.GetValueOrDefault(key), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        });
        expander.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _expandedSections[key] = expander.IsExpanded; };
        _inspector.Children.Add(expander);
    }
    private void CanvasLayout(ProcessingNode node)
    {
        var fields = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        Field("画布 X", F(node.X), v => Session.UpdateNode(Session.Document.Node(node.Id) with { X = Parse(v) }), fields);
        Field("画布 Y", F(node.Y), v => Session.UpdateNode(Session.Document.Node(node.Id) with { Y = Parse(v) }), fields);
        Field("节点宽度 ≥240", F(node.CanvasWidth), v => Session.UpdateNode(Session.Document.Node(node.Id) with { CanvasWidth = Parse(v) }), fields);
        Field("节点高度 ≥200", F(ProcessingCanvas.NodeHeight(node)), v => Session.UpdateNode(Session.Document.Node(node.Id) with { CanvasHeight = Parse(v) }), fields);
        fields.Children.Add(Button("恢复默认大小", () => Session.UpdateNode(Session.Document.Node(node.Id) with { CanvasWidth = 280, CanvasHeight = null })));
        var expander = UiTheme.StyleExpander(new Expander { Header = new TextBlock { Text = "画布位置与大小", FontSize = 12, FontWeight = FontWeight.Medium }, Content = fields, IsExpanded = _layoutExpanded, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        ToolTip.SetTip(expander, "仅调整节点在画布上的位置和大小，不改变实际加工尺寸。");
        expander.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _layoutExpanded = expander.IsExpanded; };
        _inspector.Children.Add(expander);
    }
    private TextBox Field(string label, string value, Action<string> apply, StackPanel? target = null)
    {
        var box = new TextBox { Text = value, Watermark = label, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
        UiTheme.ApplyInputStyle(box);
        ParameterRow(label, box, target);
        box.TextChanged += (_, _) => UiTheme.SetInputError(box, false);
        box.LostFocus += (_, _) =>
        {
            if (box.Text == value) return;
            try { apply(box.Text?.Trim() ?? ""); }
            catch (Exception error) { UiTheme.SetInputError(box, true); ToolTip.SetTip(box, error.Message); Show(error.Message); }
        };
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { Canvas.Focus(); e.Handled = true; } };
        return box;
    }
    private void ParameterRow(string label, Control control, StackPanel? target = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("1.05*,1.2*"), ColumnSpacing = 10 };
        var caption = new TextBlock { Text = label, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.TextSecondaryBrush };
        ToolTip.SetTip(caption, label); ToolTip.SetTip(control, label);
        row.Children.Add(caption); Grid.SetColumn(control, 1); row.Children.Add(control); (target ?? _inspector).Children.Add(row);
    }
    private TextBox Setting(ProcessingNode node, string key, string label) => Field(label, node.Setting(key), value =>
    {
        var current = Session.Document.Node(node.Id); var settings = new Dictionary<string, string>(current.Settings) { [key] = value };
        Session.UpdateNode(current with { Settings = settings });
    });
    private void Toggle(ProcessingNode node, string key, string label, bool fallback = false)
    {
        var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 }, IsChecked = node.Setting(key, fallback ? "true" : "false") == "true", MinHeight = 28 };
        box.IsCheckedChanged += (_, _) => Safe(() =>
        {
            var current = Session.Document.Node(node.Id); var settings = new Dictionary<string, string>(current.Settings) { [key] = box.IsChecked == true ? "true" : "false" };
            Session.UpdateNode(current with { Settings = settings });
        });
        _inspector.Children.Add(box);
    }
    private void Choice(ProcessingNode node, string key, string label, string[] values)
    {
        var box = new ComboBox { ItemsSource = values, SelectedItem = node.Setting(key), HorizontalAlignment = HorizontalAlignment.Stretch };
        UiTheme.ApplyInputStyle(box);
        box.SelectionChanged += (_, _) => Safe(() => { var current = Session.Document.Node(node.Id); Session.UpdateNode(current with { Settings = new(current.Settings) { [key] = box.SelectedItem?.ToString() ?? values[0] } }); });
        ParameterRow(label, box);
    }
    private void Layers(ProcessingNode node, ProcessingConnection? edge)
    {
        Heading("输出层");
        var layers = _executor?.States.GetValueOrDefault(node.Id)?.Result?.Layers;
        if (layers is null || layers.Length == 0) { var note = new TextBlock { Text = "运行后可选择层", FontSize = 11, Foreground = UiTheme.TextFaintBrush }; ToolTip.SetTip(note, "运行此节点后可勾选输出层；全部层端口可以提前连接。"); _inspector.Children.Add(note); return; }
        if (!_layerSelection.TryGetValue(node.Id, out var selected)) _layerSelection[node.Id] = selected = [];
        var chosen = edge is null ? selected : (edge.LayerIds ?? layers.Select(l => l.Id).ToArray()).ToHashSet();
        if (edge is not null)
        {
            _inspector.Children.Add(Button("改为全部层（随上游更新）", () => Session.Edit(d => d with { Connections = d.Connections.Select(c => c.Id == edge.Id ? c with { LayerIds = null } : c).ToArray() })));
            if (edge.LayerIds?.Any(id => !layers.Any(l => l.Id == id)) == true) Note("选中的部分层已消失，请重新选择后应用。");
        }
        var layerList = new StackPanel { Spacing = 2 };
        foreach (var layer in layers)
        {
            var check = new CheckBox { Content = new TextBlock { Text = $"{layer.Order} · {layer.Name}", TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11.5 }, IsChecked = chosen.Contains(layer.Id), MinHeight = 28, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ToolTip.SetTip(check, layer.Name);
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) chosen.Add(layer.Id); else chosen.Remove(layer.Id); };
            layerList.Children.Add(check);
        }
        _inspector.Children.Add(new Border { Background = UiTheme.SunkenBrush, BorderBrush = UiTheme.BorderSubtleBrush, BorderThickness = new Thickness(1), CornerRadius = UiTheme.ControlRadius, Padding = new Thickness(7, 3),
            Child = new ScrollViewer { Content = layerList, MaxHeight = 144, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } });
        if (edge is not null)
            _inspector.Children.Add(Button("应用选中层", () => Session.Edit(d => d with { Connections = d.Connections.Select(c => c.Id == edge.Id ? c with { LayerIds = chosen.ToArray() } : c).ToArray() })));
        else Fold(node.Id + ":layer-help", "层选择说明", () => Note("勾选后从「选中层」圆点拖线；「全部层」圆点始终传递所有层。"));
    }
    private void PmtInspector(ProcessingNode node)
    {
        Heading("工件尺寸");
        Field("工件宽度 mm", F(node.WorkpieceWidth), v => Session.UpdateNode(Session.Document.Node(node.Id) with { WorkpieceWidth = Parse(v) }));
        Field("工件高度 mm", F(node.WorkpieceHeight), v => Session.UpdateNode(Session.Document.Node(node.Id) with { WorkpieceHeight = Parse(v) }));
        Setting(node, "spacing", "Hatch 间距 mm");
        var selected = node.Instances.FirstOrDefault(i => i.Id == Session.SelectedInstanceId) ?? (node.Instances.Length == 1 ? node.Instances[0] : null);
        if (selected is null) { Note("点击工件中的单个 PMT，编辑其位置、尺寸和参数，或以它为模板生成矩阵。"); return; }
        Heading("PMT " + selected.Number);
        void Edit(Func<ProcessingPmtInstance, ProcessingPmtInstance> edit)
        {
            var current = Session.Document.Node(node.Id);
            Session.UpdateNode(current with { Instances = current.Instances.Select(i => i.Id == selected.Id ? edit(i) : i).ToArray() });
        }
        Field("X mm", F(selected.X), v => Edit(i => i with { X = Parse(v) })); Field("Y mm", F(selected.Y), v => Edit(i => i with { Y = Parse(v) }));
        Field("宽度 mm", F(selected.Width), v => Edit(i => i with { Width = Parse(v) })); Field("高度 mm", F(selected.Height), v => Edit(i => i with { Height = Parse(v) }));
        _inspector.Children.Add(Button("以此 PMT 创建矩阵工件", ShowMatrix));
        Fold(node.Id + ":overrides", $"激光参数覆盖 · {selected.Overrides.Count} 项", () =>
        {
        Note("留空继承 Machine，填写仅覆盖此 PMT。布尔值为 true / false。");
        foreach (var definition in LaserPmtConfiguration.Parameters)
            Field(definition.DisplayName, selected.Overrides.GetValueOrDefault(definition.Name, ""), value => Edit(i =>
            {
                var values = new Dictionary<string, string>(i.Overrides);
                if (value.Length == 0) values.Remove(definition.Name);
                else
                {
                    if (!LaserPmtConfiguration.TryParseRows([new(definition.Name, value)], out _, out var count, out var error) || count != 1)
                        throw new InvalidOperationException(error.Length > 0 ? error : "每个 PMT 参数需要单个值。");
                    values[definition.Name] = value;
                }
                return i with { Overrides = values };
            }));
        });
    }
    private void ShowMatrix()
    {
        var node = SelectedNode ?? throw new InvalidOperationException("请先选中单个 PMT。");
        var pmt = node.Instances.FirstOrDefault(i => i.Id == Session.SelectedInstanceId) ?? (node.Instances.Length == 1 ? node.Instances[0] : null)
            ?? throw new InvalidOperationException("请点击工件内的一个 PMT 作为矩阵模板。");
        _inspector.Children.Clear(); Heading("矩阵工件"); Note($"模板：PMT {pmt.Number}。创建新工件，原 PMT 保留。");
        _inspectorTitle.Text = "创建矩阵工件"; _inspectorDetail.Text = $"模板 PMT {pmt.Number} / 阵列布局";
        var rows = 2; var columns = 2; var gap = 2d;
        var width = Math.Max(node.WorkpieceWidth, pmt.Width * 2 + 6); var height = Math.Max(node.WorkpieceHeight, pmt.Height * 2 + 6);
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void Preview() => preview.Text = $"{rows} × {columns} = {rows * columns} 个 PMT\n需要 {(columns * pmt.Width + (columns - 1) * gap):0.##} × {(rows * pmt.Height + (rows - 1) * gap):0.##} mm";
        var rowBox = Field("行数", "2", v => { rows = int.Parse(v, CultureInfo.InvariantCulture); Preview(); });
        var columnBox = Field("列数", "2", v => { columns = int.Parse(v, CultureInfo.InvariantCulture); Preview(); });
        var picker = new PmtMatrixPicker();
        picker.PreviewChanged += (_, e) => preview.Text = $"预览：{e.Rows} × {e.Columns} = {e.Rows * e.Columns} 个 PMT";
        picker.PreviewCancelled += (_, _) => Preview();
        picker.SelectionCommitted += (_, e) => { rows = e.Rows; columns = e.Columns; rowBox.Text = rows.ToString(); columnBox.Text = columns.ToString(); Preview(); };
        _inspector.Children.Add(picker);
        Field("间距 mm", F(gap), v => { gap = Parse(v); Preview(); });
        Field("工件宽度 mm", F(width), v => width = Parse(v)); Field("工件高度 mm", F(height), v => height = Parse(v));
        _inspector.Children.Add(preview); Preview();
        AddActionPair(Button("返回", () => { _inspectedRevision = -1; Refresh(); }), Button("创建矩阵", () => { Session.Matrix(node.Id, pmt.Id, rows, columns, gap, width, height); Canvas.FitAll(); }));
    }
    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

internal static class ProcessingSelectionExtensions
{
    internal static T? SingleOrDefaultIfOne<T>(this ICollection<T> values) => values.Count == 1 ? values.Single() : default;
}
