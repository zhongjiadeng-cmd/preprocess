using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

public sealed class LaserPmtWorkflowInspector : Border
{
    private readonly StackPanel _content = new() { Spacing = 10 };
    private LaserPmtWorkflowCanvas? _canvas;

    public LaserPmtWorkflowInspector()
    {
        Width = 260;
        Padding = new Thickness(12);
        CornerRadius = UiTheme.ControlRadius;
        Background = UiTheme.CardBrush;
        BorderBrush = UiTheme.BorderSubtleBrush;
        BorderThickness = new Thickness(1);
        Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _content
        };
        Refresh();
    }

    public void Attach(LaserPmtWorkflowCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_canvas is not null)
        {
            _canvas.SelectionChanged -= OnCanvasChanged;
            _canvas.WorkflowChanged -= OnCanvasChanged;
        }
        _canvas = canvas;
        canvas.SelectionChanged += OnCanvasChanged;
        canvas.WorkflowChanged += OnCanvasChanged;
        Refresh();
    }

    private void OnCanvasChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock
        {
            Text = "属性",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = UiTheme.TextPrimaryBrush
        });
        var workflow = _canvas?.Workflow;
        var selectedId = _canvas?.SelectedId;
        if (workflow is null)
        {
            AddHint("导入基础加工目录后创建工作流。");
            return;
        }
        if (selectedId is null)
        {
            AddHint("选择 PMT、时间戳、参数节点或连线进行编辑。拖动画布空白处平移，滚轮缩放。");
            AddValidation(workflow);
            return;
        }
        if (selectedId == workflow.BaseNode.Id)
        {
            AddBaseEditor(workflow);
            return;
        }
        var node = workflow.ParameterNodes.FirstOrDefault(item => item.Id == selectedId);
        if (node is not null)
        {
            AddParameterNodeEditor(workflow, node);
            return;
        }
        var target = workflow.Targets.FirstOrDefault(item => item.Id == selectedId);
        if (target is not null)
        {
            AddTargetEditor(workflow, target);
            return;
        }
        var connection = workflow.Connections.FirstOrDefault(item => item.Id == selectedId);
        if (connection is not null)
        {
            AddLabel("参数连线");
            AddHint($"{connection.SourceNodeId}\n端口：{connection.SourcePortId}\n目标：{connection.TargetId}");
            AddDeleteButton();
        }
    }

    private void AddBaseEditor(LaserPmtWorkflow workflow)
    {
        AddLabel("基础参数（默认连接全部目标）");
        foreach (var definition in LaserPmtConfiguration.Parameters)
        {
            var check = new CheckBox
            {
                Content = $"{definition.DisplayName} · {workflow.BaseNode.Parameters[definition.Name]}",
                IsChecked = !workflow.BaseNode.RemovedParameters.Contains(definition.Name),
                FontSize = 11.5
            };
            check.Click += (_, _) => Apply(current => LaserPmtWorkflowEditor.SetBaseParameterEnabled(
                current, definition.Name, check.IsChecked == true));
            _content.Children.Add(check);
        }
    }

    private void AddParameterNodeEditor(
        LaserPmtWorkflow workflow,
        LaserPmtSingleParameterNode node)
    {
        var definition = LaserPmtConfiguration.Parameters.First(item => item.Name == node.ParameterName);
        AddLabel(definition.DisplayName);
        var values = new TextBox
        {
            Text = node.ValuesText,
            Watermark = "逗号分隔多组值",
            FontFamily = UiTheme.MonoFont
        };
        UiTheme.ApplyInputStyle(values);
        _content.Children.Add(values);
        AddHint(string.Join(" · ", node.Ports.Select((port, index) => $"{index + 1}: {port.Value}")));
        var apply = new Button { Content = "更新参数组" };
        UiTheme.ApplySecondaryStyle(apply);
        apply.Click += (_, _) => Apply(current => LaserPmtWorkflowEditor.UpdateParameterNodeValues(
            current,
            node.Id,
            values.Text ?? string.Empty,
            () => $"port-{Guid.NewGuid():N}").Workflow);
        _content.Children.Add(apply);
        AddDeleteButton();
    }

    private void AddTargetEditor(LaserPmtWorkflow workflow, LaserPmtWorkflowTarget target)
    {
        AddLabel(target is LaserPmtTimestampTarget ? "时间戳" : "PMT");
        if (target is LaserPmtTimestampTarget timestamp)
        {
            var text = new TextBox { Text = timestamp.Text, MaxLength = 8, FontFamily = UiTheme.MonoFont };
            UiTheme.ApplyInputStyle(text);
            var width = NumberBox((decimal)timestamp.Bounds.Width);
            var height = NumberBox((decimal)timestamp.Bounds.Height);
            _content.Children.Add(Field("月日时分（MMddHHmm）", text));
            _content.Children.Add(Field("宽度（mm）", width));
            _content.Children.Add(Field("高度（mm）", height));
            var apply = new Button { Content = "应用时间戳" };
            UiTheme.ApplySecondaryStyle(apply);
            apply.Click += (_, _) => Apply(current =>
            {
                var updated = LaserPmtWorkflowEditor.UpdateTimestampText(
                    current, timestamp.Id, text.Text ?? string.Empty);
                return LaserPmtWorkflowEditor.ResizeTimestamp(
                    updated,
                    timestamp.Id,
                    decimal.ToDouble(width.Value ?? 0),
                    decimal.ToDouble(height.Value ?? 0));
            });
            _content.Children.Add(apply);
        }
        else if (target is LaserPmtTarget pmt)
        {
            var number = NumberBox(pmt.Number);
            var left = NumberBox((decimal)pmt.Bounds.Left);
            var top = NumberBox((decimal)pmt.Bounds.Top);
            var width = NumberBox((decimal)pmt.Bounds.Width);
            var height = NumberBox((decimal)pmt.Bounds.Height);
            var locked = new CheckBox { Content = "锁定原始尺寸", IsChecked = pmt.IsSizeLocked };
            width.IsEnabled = height.IsEnabled = !pmt.IsSizeLocked;
            _content.Children.Add(Field("编号", number));
            _content.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 8,
                Children = { Field("X（mm）", left), Place(Field("Y（mm）", top), 1) }
            });
            _content.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 8,
                Children = { Field("宽（mm）", width), Place(Field("高（mm）", height), 1) }
            });
            _content.Children.Add(locked);
            number.ValueChanged += (_, _) => Apply(current =>
                LaserPmtWorkflowEditor.SetPmtNumber(current, pmt.Id, decimal.ToInt32(number.Value ?? 0)));
            left.ValueChanged += (_, _) => Apply(current =>
                LaserPmtWorkflowEditor.MovePmt(current, pmt.Id,
                    decimal.ToDouble(left.Value ?? 0), current.Targets.OfType<LaserPmtTarget>().Single(item => item.Id == pmt.Id).Bounds.Top));
            top.ValueChanged += (_, _) => Apply(current =>
                LaserPmtWorkflowEditor.MovePmt(current, pmt.Id,
                    current.Targets.OfType<LaserPmtTarget>().Single(item => item.Id == pmt.Id).Bounds.Left,
                    decimal.ToDouble(top.Value ?? 0)));
            width.ValueChanged += (_, _) => Apply(current =>
                LaserPmtWorkflowEditor.ResizePmt(current, pmt.Id,
                    decimal.ToDouble(width.Value ?? 0), current.Targets.OfType<LaserPmtTarget>().Single(item => item.Id == pmt.Id).Bounds.Height));
            height.ValueChanged += (_, _) => Apply(current =>
                LaserPmtWorkflowEditor.ResizePmt(current, pmt.Id,
                    current.Targets.OfType<LaserPmtTarget>().Single(item => item.Id == pmt.Id).Bounds.Width,
                    decimal.ToDouble(height.Value ?? 0)));
            locked.Click += (_, _) => Apply(current => LaserPmtWorkflowEditor.SetPmtSizeLock(
                current, pmt.Id, locked.IsChecked == true, restoreNativeSize: false));
        }
        AddEditableCompiledParameters(workflow, target.Id);
        AddDeleteButton();
    }

    private void AddEditableCompiledParameters(LaserPmtWorkflow workflow, string targetId)
    {
        var compilation = LaserPmtWorkflowCompiler.Compile(workflow);
        var compiled = compilation.Targets.FirstOrDefault(item => item.TargetId == targetId);
        AddLabel("最终参数（实时）");
        if (compiled is null)
        {
            AddHint(string.Join("\n", compilation.Errors
                .Where(error => error.TargetId == targetId)
                .Select(error => error.Message)));
            return;
        }
        foreach (var definition in LaserPmtConfiguration.Parameters)
        {
            if (!compiled.Parameters.TryGetValue(definition.Name, out var value))
                continue;
            if (definition.IsBoolean)
            {
                var check = new CheckBox { Content = definition.DisplayName, IsChecked = (bool)value };
                check.Click += (_, _) => Apply(current => LaserPmtWorkflowEditor.SetDirectParameterOverride(
                    current, targetId, definition.Name, check.IsChecked == true ? "true" : "false"));
                _content.Children.Add(check);
            }
            else
            {
                var number = NumberBox(Convert.ToDecimal(value));
                number.ValueChanged += (_, _) => Apply(current => LaserPmtWorkflowEditor.SetDirectParameterOverride(
                    current, targetId, definition.Name,
                    decimal.ToInt32(number.Value ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                _content.Children.Add(Field(definition.DisplayName, number));
            }
        }
    }

    private void AddCompiledParameters(LaserPmtWorkflow workflow, string targetId)
    {
        var compilation = LaserPmtWorkflowCompiler.Compile(workflow);
        var compiled = compilation.Targets.FirstOrDefault(item => item.TargetId == targetId);
        AddLabel("最终参数");
        if (compiled is null)
        {
            AddHint(string.Join("\n", compilation.Errors
                .Where(error => error.TargetId == targetId)
                .Select(error => error.Message)));
            return;
        }
        foreach (var definition in LaserPmtConfiguration.Parameters)
        {
            if (!compiled.Parameters.TryGetValue(definition.Name, out var value))
                continue;
            var sourceConnection = workflow.Connections.FirstOrDefault(connection =>
                connection.TargetId == targetId &&
                workflow.ParameterNodes.Any(node =>
                    node.Id == connection.SourceNodeId && node.ParameterName == definition.Name));
            var sourceLabel = "基础";
            if (sourceConnection is not null)
            {
                var sourceNode = workflow.ParameterNodes.Single(node => node.Id == sourceConnection.SourceNodeId);
                var portNumber = sourceNode.Ports.ToList()
                    .FindIndex(port => port.Id == sourceConnection.SourcePortId) + 1;
                sourceLabel = $"线 {portNumber}";
            }
            AddHint($"{definition.DisplayName}: {value}  ·  {sourceLabel}");
        }
    }

    private void AddValidation(LaserPmtWorkflow workflow)
    {
        var compilation = LaserPmtWorkflowCompiler.Compile(workflow);
        var geometry = LaserPmtWorkflowEditor.ValidateGeometry(workflow);
        AddHint(compilation.IsValid && geometry.Count == 0
            ? $"{workflow.Targets.OfType<LaserPmtTarget>().Count()} 个 PMT · " +
              $"{workflow.Targets.OfType<LaserPmtTimestampTarget>().Count()} 个时间戳 · 可生成"
            : string.Join("\n", compilation.Errors.Select(item => item.Message)
                .Concat(geometry.Select(item => item.Message))));
    }

    private void AddDeleteButton()
    {
        var delete = new Button { Content = "删除所选元素" };
        UiTheme.ApplyQuietStyle(delete);
        delete.Click += (_, _) => _canvas?.DeleteSelection();
        _content.Children.Add(delete);
    }

    private void Apply(Func<LaserPmtWorkflow, LaserPmtWorkflow> update)
    {
        if (_canvas?.Workflow is not { } workflow)
            return;
        try
        {
            _canvas.UpdateWorkflow(update(workflow));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            AddHint(exception.Message, true);
        }
    }

    private void AddLabel(string text) => _content.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Foreground = UiTheme.TextPrimaryBrush,
        TextWrapping = TextWrapping.Wrap
    });

    private void AddHint(string text, bool danger = false) => _content.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 11,
        Foreground = danger ? UiTheme.DangerTextBrush : UiTheme.TextSecondaryBrush,
        TextWrapping = TextWrapping.Wrap
    });

    private static Control Field(string label, Control control) => new StackPanel
    {
        Spacing = 5,
        Children = { UiTheme.FieldLabel(label), control }
    };

    private static T Place<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static NumericUpDown NumberBox(decimal value)
    {
        var box = new NumericUpDown
        {
            Minimum = 0.001m,
            Maximum = 100000,
            Increment = 0.1m,
            Value = value,
            FormatString = "0.###",
            FontFamily = UiTheme.MonoFont
        };
        UiTheme.ApplyInputStyle(box);
        return box;
    }
}
