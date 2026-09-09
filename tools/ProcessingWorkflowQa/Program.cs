using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrayscaleLayersMac;
using System.Reflection;

// A separate process keeps the headless dispatcher isolated from domain tests.
var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts");
Directory.CreateDirectory(output);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var window = new MainWindow { Width = 1440, Height = 900 };
window.Show(); Dispatcher.UIThread.RunJobs();
var workspace = window.GetVisualDescendants().OfType<ProcessingWorkspace>().Single();
var s = workspace.Session;
var app = (App)Application.Current!;
var appearanceMethod = typeof(App).GetMethod("SetAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
var originalAppearance = typeof(App).GetProperty("Appearance", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app);
var appearanceType = appearanceMethod.GetParameters()[0].ParameterType;
void Appearance(string name)
{
    // QA must never persist a theme override or change the user's system-following preference.
    appearanceMethod.Invoke(app, [Enum.Parse(appearanceType, name), false]);
    Frame();
}
void Frame() { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
string Capture(string name, Window? target = null)
{
    Frame();
    var path = Path.Combine(output, name + ".png");
    using var bitmap = (target ?? window).CaptureRenderedFrame() ?? throw new Exception("The window did not render a frame.");
    bitmap.Save(path);
    return path;
}
try
{
Appearance("Light"); var emptyLight = Capture("industrial-empty-light");
Appearance("Dark"); var emptyDark = Capture("industrial-empty-dark");
Require(!File.ReadAllBytes(emptyLight).SequenceEqual(File.ReadAllBytes(emptyDark)), "Changing appearance must redraw the existing window.");
var samplePath = Path.GetFullPath("火花纹COL001214.tif");
var texture = s.Add(ProcessingNodeKind.Texture, 0, 0, File.Exists(samplePath) ? samplePath : "sample.tif");
var gray = s.Add(ProcessingNodeKind.Grayscale, 340, 0);
Point Screen(double x, double y) => workspace.Canvas.TranslatePoint(new Point(x * s.Document.Zoom + s.Document.PanX, y * s.Document.Zoom + s.Document.PanY), window)!.Value;
void Drag(Point start, Point end)
{
    window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton);
    window.MouseUp(end, MouseButton.Left); Dispatcher.UIThread.RunJobs();
}
Drag(Screen(texture.X + ProcessingCanvas.NodeWidth, texture.Y + 49), Screen(gray.X, gray.Y + 49));
Require(s.Document.Connections.Length == 1, "Dragging an output to an input must connect the nodes.");
Drag(Screen(gray.X + 20, gray.Y + 18), Screen(gray.X + 60, gray.Y + 48));
Require(Math.Abs(s.Document.Node(gray.Id).X - gray.X - 40) < .01, "Header drag did not move the node.");
window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Meta); window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Meta); Dispatcher.UIThread.RunJobs();
Require(Math.Abs(s.Document.Node(gray.Id).X - gray.X) < .01, "A single undo must revert the complete drag.");
window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Meta | RawInputModifiers.Shift); window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Meta | RawInputModifiers.Shift); Dispatcher.UIThread.RunJobs();
Require(Math.Abs(s.Document.Node(gray.Id).X - gray.X - 40) < .01, "Redo did not restore the drag.");
var hatch = s.Add(ProcessingNodeKind.Hatch, 680, 0); s.Connect(gray.Id, hatch.Id, null);
var pmt = s.CreatePmt(hatch.Id, null, 10, 10);
var matrix = s.Matrix(pmt.Id, pmt.Instances[0].Id, 3, 4, 2, 60, 45);
foreach (var original in s.Document.Nodes.ToArray())
{
    // Keep each gesture visible and exercise all five types plus matrix workpieces.
    s.View(1, 30 - original.X, 30 - original.Y); workspace.Canvas.InvalidateVisual(); Frame();
    var height = ProcessingCanvas.NodeHeight(original);
    Drag(Screen(original.X + original.CanvasWidth - 7, original.Y + height - 7),
        Screen(original.X + original.CanvasWidth + 63, original.Y + height + 43));
    var resized = s.Document.Node(original.Id);
    Require(Math.Abs(resized.CanvasWidth - original.CanvasWidth - 70) < .01 &&
        Math.Abs(ProcessingCanvas.NodeHeight(resized) - height - 50) < .01, "Every node must support independent resizing.");
    s.Undo(); Require(s.Document.Node(original.Id).CanvasWidth == original.CanvasWidth, "One undo must restore the full resize.");
    s.Redo();
}
var cancelNode = s.Document.Node(texture.Id);
s.View(1, 30 - cancelNode.X, 30 - cancelNode.Y); Frame();
var cancelStart = Screen(cancelNode.X + cancelNode.CanvasWidth - 7, cancelNode.Y + ProcessingCanvas.NodeHeight(cancelNode) - 7);
window.MouseDown(cancelStart, MouseButton.Left); window.MouseMove(cancelStart + new Vector(40, 30), RawInputModifiers.LeftMouseButton);
window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None); window.MouseUp(cancelStart, MouseButton.Left);
Require(s.Document.Node(texture.Id).CanvasWidth == cancelNode.CanvasWidth, "Escape must cancel resizing without committing.");
var loaded = ProcessingDocument.FromJson(s.Document.ToJson());
Require(loaded.Node(texture.Id).CanvasWidth == cancelNode.CanvasWidth, "Saved projects must preserve node sizes.");
s.Edit(d => d with { Nodes = d.Nodes.Select((n, index) => n with { X = index < 3 ? index * 430 : (5 - index) * 430, Y = index < 3 ? 0 : 360 }).ToArray() });
workspace.Canvas.FitAll(); Dispatcher.UIThread.RunJobs();
Capture("processing-workflow-overview");
s.Select(pmt.Id, instanceId: pmt.Instances[0].Id); workspace.Canvas.Focus(); Dispatcher.UIThread.RunJobs();
window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Meta); window.KeyReleaseQwerty(PhysicalKey.C, RawInputModifiers.Meta); Dispatcher.UIThread.RunJobs();
var beforePaste = s.Document.Nodes.Length;
window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Meta); window.KeyReleaseQwerty(PhysicalKey.V, RawInputModifiers.Meta); Dispatcher.UIThread.RunJobs();
Require(s.Document.Nodes.Length == beforePaste + 1, "Keyboard paste did not create a PMT copy.");
window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Meta); window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Meta); Dispatcher.UIThread.RunJobs();
Require(s.Document.Nodes.Length == beforePaste, "Keyboard undo did not remove the paste.");
s.Select(hatch.Id); Dispatcher.UIThread.RunJobs();
Capture("processing-workflow-hatch");
var editor = workspace.GetVisualDescendants().OfType<TextBox>().First(); editor.Focus();
var beforeTextUndo = s.Document.ToJson();
window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Meta); window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Meta); Dispatcher.UIThread.RunJobs();
Require(s.Document.ToJson() == beforeTextUndo, "Text editing shortcut leaked into graph history.");
editor.Text = "Hatch 参数检查"; workspace.Canvas.Focus(); Frame();
Require(s.Document.Node(hatch.Id).Name == "Hatch 参数检查", "Compact inspector editing must commit to the selected node.");
s.Undo(); Require(s.Document.Node(hatch.Id).Name == hatch.Name, "An inspector edit must remain undoable.");

Expander Section(string title) => workspace.GetVisualDescendants().OfType<Expander>()
    .Single(section => (section.Header as TextBlock)?.Text?.StartsWith(title, StringComparison.Ordinal) == true);
var advanced = Section("高级分块与纹理");
Require(!advanced.IsExpanded, "Advanced Hatch fields should start collapsed.");
advanced.IsExpanded = true; Frame();
var advancedEditor = advanced.GetVisualDescendants().OfType<TextBox>().Single(box => box.Watermark?.ToString() == "最小块面积 %");
advancedEditor.BringIntoView(); Frame(); advancedEditor.Focus();
var originalMinimumArea = s.Document.Node(hatch.Id).Setting("min-area-percent");
advancedEditor.Text = "7"; workspace.Canvas.Focus(); Frame();
Require(s.Document.Node(hatch.Id).Setting("min-area-percent") == "7", "An expanded advanced setting must remain editable.");
Require(Section("高级分块与纹理").IsExpanded, "Refreshing the inspector after an edit must preserve the expanded section.");
s.Undo(); Frame();
Require(s.Document.Node(hatch.Id).Setting("min-area-percent") == originalMinimumArea, "Undo must restore advanced setting edits.");
Require(Section("高级分块与纹理").IsExpanded, "Undo must preserve the expanded section.");
Section("高级分块与纹理").IsExpanded = false;
s.Select(pmt.Id, instanceId: pmt.Instances[0].Id); Frame();
var overrides = Section("激光参数覆盖");
Require(!overrides.IsExpanded, "PMT parameter overrides should start collapsed.");
overrides.IsExpanded = true; Frame();
Require(overrides.GetVisualDescendants().OfType<TextBox>().Any(), "Expanding PMT overrides must reveal editable parameters.");
overrides.IsExpanded = false;
s.Select(hatch.Id); Frame();

var workspaceGrid = (Grid)workspace.Canvas.Parent!;
var workspaceSplitter = workspaceGrid.Children.OfType<GridSplitter>().Single();
Point SplitterCenter() => workspaceSplitter.TranslatePoint(new Point(workspaceSplitter.Bounds.Width / 2, workspaceSplitter.Bounds.Height / 2), window)!.Value;
void RequireUsableSplitter()
{
    Frame();
    var columns = workspaceGrid.ColumnDefinitions;
    Require(columns[0].ActualWidth >= 319.9 && columns[2].ActualWidth >= 299.9, "Splitter gestures must respect both column minimum widths.");
    Require(Math.Abs(workspaceSplitter.Bounds.Width - 5) < .1, $"The splitter must retain its thin 5 px style. Actual control={workspaceSplitter.Bounds}, columns={string.Join(",", columns.Select(column => column.ActualWidth))}.");
    Require(workspaceSplitter.Bounds.Left >= 0 && workspaceSplitter.Bounds.Right <= workspaceGrid.Bounds.Width + .1,
        "The right inspector splitter must remain visible within the workspace.");
    Require(columns.Sum(column => column.ActualWidth) <= workspaceGrid.Bounds.Width + .1,
        "Dragging or shrinking the window must not push the inspector outside the workspace.");
}
var initialSplit = SplitterCenter();
Drag(initialSplit, new Point(window.Bounds.Width + 1000, initialSplit.Y)); RequireUsableSplitter();
var rightBoundary = SplitterCenter();
Drag(rightBoundary, rightBoundary - new Vector(180, 0)); RequireUsableSplitter();
Require(SplitterCenter().X < rightBoundary.X - 150, "A splitter at the right boundary must be draggable back into the workspace.");
var middleSplit = SplitterCenter();
Drag(middleSplit, new Point(-1000, middleSplit.Y)); RequireUsableSplitter();
var leftBoundary = SplitterCenter();
Drag(leftBoundary, leftBoundary + new Vector(180, 0)); RequireUsableSplitter();
Require(SplitterCenter().X > leftBoundary.X + 150, "A splitter at the left boundary must be draggable back into the workspace.");
window.Width = 1080; window.Height = 720; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); workspace.Canvas.FitAll(); Dispatcher.UIThread.RunJobs();
RequireUsableSplitter();
var compactSplit = SplitterCenter();
Drag(compactSplit, new Point(window.Bounds.Width + 1000, compactSplit.Y)); RequireUsableSplitter();
var compactBoundary = SplitterCenter();
Drag(compactBoundary, compactBoundary - new Vector(80, 0)); RequireUsableSplitter();
Require(SplitterCenter().X < compactBoundary.X - 60, "The compact window splitter must remain reachable after dragging to the edge.");
workspaceGrid.ColumnDefinitions[0].Width = new GridLength(.75, GridUnitType.Star);
workspaceGrid.ColumnDefinitions[2].Width = new GridLength(.25, GridUnitType.Star);
Frame(); workspace.Canvas.FitAll();
Console.WriteLine($"Compact bounds: window={window.Bounds}, workspace={workspace.Bounds}, canvas={workspace.Canvas.Bounds}");
Capture("processing-workflow-compact");

// If real processing outputs are present, load those exact files for visual QA.
// They are preview-only states: this harness neither runs manufacturing nor claims it completed.
var resultsRoot = Path.GetFullPath("加工文件");
if (Directory.Exists(resultsRoot))
{
    var executor = new ProcessingExecutor(new PythonProcessingRunner("python3", AppContext.BaseDirectory));
    var texturePreview = Directory.EnumerateDirectories(resultsRoot, "Texture_*").Order(StringComparer.Ordinal)
        .Select(path => Path.Combine(path, "texture.png")).FirstOrDefault(File.Exists);
    if (texturePreview is not null && File.Exists(samplePath))
        executor.States[texture.Id] = new(ProcessingExecutor.Configuration(s.Document, texture.Id), "预览就绪",
            new([new(texture.Id + ":texture", Path.GetFileName(samplePath), samplePath, 0, texturePreview)], Info: "现有纹理预览"));
    var hatchDirectory = Directory.EnumerateDirectories(resultsRoot, "Hatch_*").Order(StringComparer.Ordinal)
        .FirstOrDefault(path => Directory.EnumerateFiles(path, "*.preview.png").Any());
    if (hatchDirectory is not null)
    {
        var layers = Directory.EnumerateFiles(hatchDirectory, "*.dxf").Order(StringComparer.Ordinal)
            .Select((path, index) => new ProcessingLayer(hatch.Id + ":preview:" + index, Path.GetFileNameWithoutExtension(path),
                path, index + 1, Path.ChangeExtension(path, ".preview.png")))
            .Where(layer => File.Exists(layer.PreviewPath)).ToArray();
        executor.States[hatch.Id] = new(ProcessingExecutor.Configuration(s.Document, hatch.Id), "预览就绪",
            new(layers, Info: $"已载入现有 DXF 预览 · {layers.Length} 层"));
    }
    typeof(ProcessingWorkspace).GetField("_executor", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, executor);
    workspace.Canvas.Executor = executor;
    workspace.Canvas.InvalidateVisual();
}

var tabs = window.GetVisualDescendants().OfType<TabControl>()
    .Single(tab => tab.Items.OfType<TabItem>().Any(item => ReferenceEquals(item.Content, workspace)));
foreach (var theme in new[] { "Light", "Dark" })
{
    var suffix = theme.ToLowerInvariant();
    Appearance(theme);
    window.Width = 1440; window.Height = 900; window.UpdateLayout(); Frame(); workspace.Canvas.FitAll();
    s.Select(hatch.Id); Capture("industrial-workflow-hatch-" + suffix);
    s.Select(matrix.Id, instanceId: matrix.Instances[0].Id); Capture("industrial-workflow-matrix-" + suffix);
    tabs.SelectedIndex = 1; Frame(); Capture("industrial-pipeline-" + suffix);
    var pipelineSplitters = window.GetVisualDescendants().OfType<GridSplitter>().Where(splitter => splitter.IsEffectivelyVisible).ToArray();
    Require(pipelineSplitters.Length > 0, "The processing flow must expose its panel splitter.");
    foreach (var splitter in pipelineSplitters)
    {
        Require(Math.Abs(splitter.Bounds.Width - workspaceSplitter.Bounds.Width) < .1,
            "The processing flow and node workflow must use the same thin splitter width.");
        Require(splitter.Background == workspaceSplitter.Background && splitter.Cursor?.ToString() == workspaceSplitter.Cursor?.ToString(),
            "Both workflows must share the splitter appearance and resize cursor.");
    }
    tabs.SelectedIndex = 0; Frame();
    s.Select(hatch.Id); Frame();
    var beforeDialog = s.Document.ToJson();
    var dialogType = typeof(ProcessingWorkspace).Assembly.GetType("GrayscaleLayersMac.WorkspaceConfirmDialog")!;
    var dialog = (Window)Activator.CreateInstance(dialogType,
        ["打开其他项目？", "当前项目有未保存的修改。继续打开将放弃这些修改。", "放弃并打开"])!;
    var dialogResult = dialog.ShowDialog<bool>(window); Frame();
    Capture("industrial-confirm-" + suffix, dialog);
    Require(dialog.Bounds.Height < 280 && dialog.Bounds.Width >= 400, "The confirmation should fit its content without an oversized empty body.");
    // Child windows do not expose the headless keyboard bridge on every Avalonia backend;
    // invoke the dialog's cancel command directly, which is also the IsCancel/Escape path.
    dialog.Close(false); Frame();
    Require(dialogResult.IsCompletedSuccessfully && !dialogResult.Result, "Cancel must dismiss the confirmation and keep editing.");
    Require(s.Document.ToJson() == beforeDialog, "Dismissing the confirmation must preserve the current project.");
    window.Width = 1080; window.Height = 720; window.UpdateLayout(); Frame(); workspace.Canvas.FitAll();
    s.Select(hatch.Id); Capture("industrial-workflow-compact-" + suffix);
    tabs.SelectedIndex = 1; Frame(); Capture("industrial-pipeline-compact-" + suffix);
    tabs.SelectedIndex = 0; Frame();
}
Console.WriteLine("PASS: port dragging, node dragging/resizing, Escape, saved sizes, undo/redo, keyboard copy/paste, text-focus isolation, advanced-section edit/undo persistence, extreme splitter drags and window resizing, shared splitter style, compact confirmation Escape, live theme redraw, and light/dark node/pipeline layouts at 1440×900 and 1080×720.");
}
finally
{
    appearanceMethod.Invoke(app, [originalAppearance, false]);
    window.Close();
}
