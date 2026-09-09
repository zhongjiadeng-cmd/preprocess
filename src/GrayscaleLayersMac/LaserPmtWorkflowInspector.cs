using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

public sealed class LaserPmtWorkflowInspector : Border
{
    private readonly StackPanel _content = new() { Spacing = 6 };
    private readonly TextBlock _title = new()
    {
        Text = "属性", FontSize = 12.5, FontWeight = FontWeight.SemiBold,
        Foreground = UiTheme.TextPrimaryBrush, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _error = new()
    {
        FontSize = 10.5, Foreground = UiTheme.DangerTextBrush,
        TextWrapping = TextWrapping.Wrap, IsVisible = false
    };
    private LaserPmtWorkflowCanvas? _canvas;
    private bool _isApplyingLive;

    public Control DragHandle { get; }

    public LaserPmtWorkflowInspector()
    {
        Width = 236;
        CornerRadius = UiTheme.ControlRadius;
        Background = UiTheme.CardBrush;
        BorderBrush = UiTheme.BorderMediumBrush;
        BorderThickness = new Thickness(1);
        BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 18, OffsetY = 5, Color = Color.FromArgb(42, 0, 0, 0)
        });
        DragHandle = new Border
        {
            Padding = new Thickness(9, 6),
            Background = UiTheme.GhostBrush,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 7,
                Children = { UiIcons.CreateSmall(UiIcon.Nodes), Place(_title, 1) }
            }
        };
        Child = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                DragHandle,
                Place(new ScrollViewer
                {
                    Margin = new Thickness(9, 7, 9, 9),
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = _content
                }, row: 1)
            }
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

    private void OnCanvasChanged(object? sender, EventArgs e)
    {
        if (!_isApplyingLive)
            Refresh();
    }

    private void Refresh()
    {
        _content.Children.Clear();
        ShowError(null);
        var workflow = _canvas?.Workflow;
        if (workflow is null)
        {
            _title.Text = "属性";
            AddHint("尚未创建 PMT 工作流。");
            return;
        }
        if (_canvas!.IsWorkpieceSelected)
        {
            _title.Text = "工件";
            AddWorkpieceEditor(workflow);
            return;
        }
        var selectedId = _canvas.SelectedId;
        if (selectedId is null)
        {
            _title.Text = "属性";
            AddHint("选择画布元素后编辑。");
            return;
        }
        var baseNode = workflow.BaseNodes.FirstOrDefault(item => item.Id == selectedId);
        if (baseNode is not null)
        {
            _title.Text = "基础参数";
            AddBaseEditor(baseNode);
            return;
        }
        var node = workflow.ParameterNodes.FirstOrDefault(item => item.Id == selectedId);
        if (node is not null)
        {
            _title.Text = LaserPmtConfiguration.Parameters
                .First(item => item.Name == node.ParameterName).DisplayName;
            AddParameterNodeEditor(node);
            return;
        }
        var target = workflow.Targets.FirstOrDefault(item => item.Id == selectedId);
        if (target is not null)
        {
            _title.Text = target is LaserPmtTimestampTarget ? "时间戳" : "PMT";
            AddTargetEditor(workflow, target);
            return;
        }
        var connection = workflow.Connections.FirstOrDefault(item => item.Id == selectedId);
        if (connection is not null)
        {
            _title.Text = "参数连线";
            AddHint($"端口：{connection.SourcePortId}\n目标：{connection.TargetId}");
            AddKeyboardHint();
        }
    }

    private void AddWorkpieceEditor(LaserPmtWorkflow workflow)
    {
        var width = TextBoxFor(workflow.Workpiece.Width);
        var height = TextBoxFor(workflow.Workpiece.Height);
        _content.Children.Add(TwoFields("宽 mm", width, "高 mm", height));
        width.TextChanged += (_, _) => ApplyDouble(width, value => value > 0,
            current => LaserPmtWorkflowEditor.SetWorkpiece(
                current, current.Workpiece with { Width = ParseDouble(width.Text!) }));
        height.TextChanged += (_, _) => ApplyDouble(height, value => value > 0,
            current => LaserPmtWorkflowEditor.SetWorkpiece(
                current, current.Workpiece with { Height = ParseDouble(height.Text!) }));
        AddError();
    }

    private void AddBaseEditor(LaserPmtBaseParameterNode node)
    {
        foreach (var definition in LaserPmtConfiguration.Parameters)
        {
            var enabled = new CheckBox
            {
                IsChecked = !node.RemovedParameters.Contains(definition.Name),
                VerticalAlignment = VerticalAlignment.Center
            };
            var value = TextBoxFor(node.Parameters[definition.Name]);
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("24,*,74"),
                ColumnSpacing = 5,
                Children =
                {
                    enabled,
                    Place(new TextBlock
                    {
                        Text = definition.DisplayName.Split('（')[0],
                        FontSize = 10.5,
                        Foreground = UiTheme.TextSecondaryBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }, 1),
                    Place(value, 2)
                }
            };
            enabled.Click += (_, _) => ApplyLive(current =>
                LaserPmtWorkflowEditor.SetBaseParameterEnabled(
                    current, node.Id, definition.Name, enabled.IsChecked == true));
            value.TextChanged += (_, _) => ApplyParameterValue(value, definition,
                current => LaserPmtWorkflowEditor.SetBaseParameterValue(
                    current, node.Id, definition.Name, value.Text ?? string.Empty));
            _content.Children.Add(row);
        }
        AddError();
    }

    private void AddParameterNodeEditor(LaserPmtSingleParameterNode node)
    {
        var values = TextBoxFor(node.ValuesText);
        values.Watermark = "例如 20, 40, 60";
        _content.Children.Add(Field("参数组（逗号分隔）", values));
        var ports = new TextBlock
        {
            Text = PortSummary(node), FontSize = 10.5,
            Foreground = UiTheme.TextSecondaryBrush, TextWrapping = TextWrapping.Wrap
        };
        _content.Children.Add(ports);
        values.TextChanged += (_, _) =>
        {
            if (!LaserPmtConfiguration.TryParseExplicitValues(
                    node.ParameterName, values.Text ?? string.Empty, out _, out var error))
            {
                ShowError(error);
                return;
            }
            if (ApplyLive(current => LaserPmtWorkflowEditor.UpdateParameterNodeValues(
                    current, node.Id, values.Text ?? string.Empty,
                    () => $"port-{Guid.NewGuid():N}").Workflow))
                ports.Text = PortSummary(_canvas!.Workflow!.ParameterNodes.Single(item => item.Id == node.Id));
        };
        AddError();
        AddKeyboardHint();
    }

    private void AddTargetEditor(LaserPmtWorkflow workflow, LaserPmtWorkflowTarget target)
    {
        if (target is LaserPmtTimestampTarget timestamp)
            AddTimestampEditor(timestamp);
        else if (target is LaserPmtTarget pmt)
            AddPmtEditor(pmt);
        AddEditableCompiledParameters(workflow, target.Id);
        AddError();
        AddKeyboardHint();
    }

    private void AddTimestampEditor(LaserPmtTimestampTarget timestamp)
    {
        var text = TextBoxFor(timestamp.Text);
        text.MaxLength = 8;
        var left = TextBoxFor(timestamp.Bounds.Left);
        var top = TextBoxFor(timestamp.Bounds.Top);
        var width = TextBoxFor(timestamp.Bounds.Width);
        var height = TextBoxFor(timestamp.Bounds.Height);
        _content.Children.Add(Field("月日时分 MMddHHmm", text));
        _content.Children.Add(TwoFields("X mm", left, "Y mm", top));
        _content.Children.Add(TwoFields("宽 mm", width, "高 mm", height));
        text.TextChanged += (_, _) => ApplyLive(current =>
            LaserPmtWorkflowEditor.UpdateTimestampText(current, timestamp.Id, text.Text ?? string.Empty));
        left.TextChanged += (_, _) => ApplyDouble(left, _ => true, current =>
            LaserPmtWorkflowEditor.MoveTimestamp(current, timestamp.Id,
                ParseDouble(left.Text!), CurrentTarget(current, timestamp.Id).Bounds.Top));
        top.TextChanged += (_, _) => ApplyDouble(top, _ => true, current =>
            LaserPmtWorkflowEditor.MoveTimestamp(current, timestamp.Id,
                CurrentTarget(current, timestamp.Id).Bounds.Left, ParseDouble(top.Text!)));
        width.TextChanged += (_, _) => ApplyDouble(width, value => value > 0, current =>
            LaserPmtWorkflowEditor.ResizeTimestamp(current, timestamp.Id,
                ParseDouble(width.Text!), CurrentTarget(current, timestamp.Id).Bounds.Height));
        height.TextChanged += (_, _) => ApplyDouble(height, value => value > 0, current =>
            LaserPmtWorkflowEditor.ResizeTimestamp(current, timestamp.Id,
                CurrentTarget(current, timestamp.Id).Bounds.Width, ParseDouble(height.Text!)));
    }

    private void AddPmtEditor(LaserPmtTarget pmt)
    {
        var number = TextBoxFor(pmt.Number.ToString(CultureInfo.InvariantCulture));
        var left = TextBoxFor(pmt.Bounds.Left);
        var top = TextBoxFor(pmt.Bounds.Top);
        var width = TextBoxFor(pmt.Bounds.Width);
        var height = TextBoxFor(pmt.Bounds.Height);
        var locked = new CheckBox { Content = "锁定尺寸", IsChecked = pmt.IsSizeLocked, FontSize = 10.5 };
        width.IsEnabled = height.IsEnabled = !pmt.IsSizeLocked;
        _content.Children.Add(Field("编号", number));
        _content.Children.Add(TwoFields("X mm", left, "Y mm", top));
        _content.Children.Add(TwoFields("宽 mm", width, "高 mm", height));
        _content.Children.Add(locked);
        number.TextChanged += (_, _) =>
        {
            if (!int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            {
                ShowError("编号必须是正整数。");
                return;
            }
            ApplyLive(current => LaserPmtWorkflowEditor.SetPmtNumber(current, pmt.Id, value));
        };
        left.TextChanged += (_, _) => ApplyDouble(left, _ => true, current =>
            LaserPmtWorkflowEditor.MovePmt(current, pmt.Id,
                ParseDouble(left.Text!), CurrentPmt(current, pmt.Id).Bounds.Top));
        top.TextChanged += (_, _) => ApplyDouble(top, _ => true, current =>
            LaserPmtWorkflowEditor.MovePmt(current, pmt.Id,
                CurrentPmt(current, pmt.Id).Bounds.Left, ParseDouble(top.Text!)));
        width.TextChanged += (_, _) => ApplyDouble(width, value => value > 0, current =>
            LaserPmtWorkflowEditor.ResizePmt(current, pmt.Id,
                ParseDouble(width.Text!), CurrentPmt(current, pmt.Id).Bounds.Height));
        height.TextChanged += (_, _) => ApplyDouble(height, value => value > 0, current =>
            LaserPmtWorkflowEditor.ResizePmt(current, pmt.Id,
                CurrentPmt(current, pmt.Id).Bounds.Width, ParseDouble(height.Text!)));
        locked.Click += (_, _) =>
        {
            if (ApplyLive(current => LaserPmtWorkflowEditor.SetPmtSizeLock(
                    current, pmt.Id, locked.IsChecked == true, restoreNativeSize: false)))
                width.IsEnabled = height.IsEnabled = locked.IsChecked != true;
        };
    }

    private void AddEditableCompiledParameters(LaserPmtWorkflow workflow, string targetId)
    {
        var compilation = LaserPmtWorkflowCompiler.Compile(workflow);
        var compiled = compilation.Targets.FirstOrDefault(item => item.TargetId == targetId);
        AddSectionLabel("最终参数");
        if (compiled is null)
        {
            AddHint(string.Join("\n", compilation.Errors.Where(error => error.TargetId == targetId)
                .Select(error => error.Message)), true);
            return;
        }
        foreach (var definition in LaserPmtConfiguration.Parameters)
        {
            if (!compiled.Parameters.TryGetValue(definition.Name, out var value))
                continue;
            if (definition.IsBoolean)
            {
                var check = new CheckBox
                {
                    Content = definition.DisplayName.Split('（')[0],
                    IsChecked = (bool)value, FontSize = 10.5
                };
                check.Click += (_, _) => ApplyLive(current =>
                    LaserPmtWorkflowEditor.SetDirectParameterOverride(current, targetId,
                        definition.Name, check.IsChecked == true ? "true" : "false"));
                _content.Children.Add(check);
            }
            else
            {
                var input = TextBoxFor(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                input.TextChanged += (_, _) => ApplyParameterValue(input, definition, current =>
                    LaserPmtWorkflowEditor.SetDirectParameterOverride(current, targetId,
                        definition.Name, input.Text ?? string.Empty));
                _content.Children.Add(CompactField(definition.DisplayName.Split('（')[0], input));
            }
        }
    }

    private void ApplyDouble(TextBox input, Func<double, bool> validate,
        Func<LaserPmtWorkflow, LaserPmtWorkflow> update)
    {
        if (!TryParseCompleteDouble(input.Text, out var value) || !validate(value))
        {
            ShowError("请输入有效数值。");
            return;
        }
        ApplyLive(update);
    }

    private void ApplyParameterValue(TextBox input, LaserPmtParameterDefinition definition,
        Func<LaserPmtWorkflow, LaserPmtWorkflow> update)
    {
        if (!LaserPmtConfiguration.TryParseExplicitValues(definition.Name,
                input.Text ?? string.Empty, out var values, out var error) || values.Count != 1)
        {
            ShowError(error.Length == 0 ? "这里只能输入一个参数值。" : error);
            return;
        }
        ApplyLive(update);
    }

    private bool ApplyLive(Func<LaserPmtWorkflow, LaserPmtWorkflow> update)
    {
        if (_canvas?.Workflow is not { } workflow)
            return false;
        try
        {
            _isApplyingLive = true;
            _canvas.UpdateWorkflow(update(workflow));
            ShowError(null);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ShowError(exception.Message);
            return false;
        }
        finally { _isApplyingLive = false; }
    }

    private void AddError() => _content.Children.Add(_error);
    private void ShowError(string? message)
    {
        _error.Text = message ?? string.Empty;
        _error.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
    private void AddSectionLabel(string text) => _content.Children.Add(new TextBlock
    {
        Text = text, Margin = new Thickness(0, 3, 0, 0), FontSize = 10.5,
        FontWeight = FontWeight.SemiBold, Foreground = UiTheme.TextPrimaryBrush
    });
    private void AddHint(string text, bool danger = false) => _content.Children.Add(new TextBlock
    {
        Text = text, FontSize = 10.5,
        Foreground = danger ? UiTheme.DangerTextBrush : UiTheme.TextSecondaryBrush,
        TextWrapping = TextWrapping.Wrap
    });
    private void AddKeyboardHint() => AddHint("Delete 删除所选元素");
    private static Control Field(string label, Control control) => new StackPanel
    {
        Spacing = 3, Children = { UiTheme.FieldLabel(label), control }
    };
    private static Control CompactField(string label, Control control) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("*,74"), ColumnSpacing = 6,
        Children =
        {
            new TextBlock
            {
                Text = label, FontSize = 10.5, Foreground = UiTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            },
            Place(control, 1)
        }
    };
    private static Control TwoFields(string firstLabel, Control first, string secondLabel, Control second) =>
        new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6,
            Children = { Field(firstLabel, first), Place(Field(secondLabel, second), 1) }
        };
    private static TextBox TextBoxFor(double value) => TextBoxFor(
        value.ToString("0.###", CultureInfo.InvariantCulture));
    private static TextBox TextBoxFor(string value)
    {
        var box = new TextBox
        {
            Text = value, Height = 27, MinHeight = 27, Padding = new Thickness(6, 3),
            FontSize = 11, FontFamily = UiTheme.MonoFont
        };
        UiTheme.ApplyInputStyle(box);
        return box;
    }
    private static bool TryParseCompleteDouble(string? text, out double value)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.EndsWith('.') || trimmed is "-" or "+")
        {
            value = 0;
            return false;
        }
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }
    private static double ParseDouble(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static LaserPmtWorkflowTarget CurrentTarget(LaserPmtWorkflow workflow, string id) =>
        workflow.Targets.Single(item => item.Id == id);
    private static LaserPmtTarget CurrentPmt(LaserPmtWorkflow workflow, string id) =>
        workflow.Targets.OfType<LaserPmtTarget>().Single(item => item.Id == id);
    private static string PortSummary(LaserPmtSingleParameterNode node) =>
        string.Join("  ·  ", node.Ports.Select((port, index) => $"{index + 1}: {port.Value}"));
    private static T Place<T>(T control, int column = 0, int row = 0) where T : Control
    {
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
        return control;
    }
}
