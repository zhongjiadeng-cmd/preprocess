using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

public sealed class LaserPmtPanel : StackPanel
{
    private sealed record EditorRow(
        Grid Container,
        ComboBox Parameter,
        TextBox Values,
        Button Delete);

    private readonly TextBox _baseDirectory = new() { Watermark = "第 3 步完成后自动填写，也可导入已有加工目录" };
    private readonly Button _pickBaseButton = new() { Content = "选择目录…" };
    private readonly NumericUpDown _workpieceWidth = NumberBox(200, 0.01m, 100000, 3);
    private readonly NumericUpDown _workpieceHeight = NumberBox(200, 0.01m, 100000, 3);
    private readonly NumericUpDown _columns = NumberBox(1, 1, 1000, 0);
    private readonly NumericUpDown _pmtCount = NumberBox(1, 1, 1000, 0, 1);
    private readonly NumericUpDown _hatchSpacing = NumberBox(0.1m, 0.01m, 1000, 3, 0.001m);
    private readonly TextBox _outputName = new() { Watermark = "留空则自动生成 LaserPMT_时间戳" };
    private readonly TextBox _prefix = new() { Text = "pmt_" };
    private readonly NumericUpDown _start = NumberBox(1, 1, int.MaxValue, 0);
    private readonly NumericUpDown _increment = NumberBox(1, 1, int.MaxValue, 0, 1);
    private readonly NumericUpDown _padding = NumberBox(4, 1, 18, 0, 1);
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _summary = new()
    {
        FontSize = 11.5,
        Foreground = UiTheme.TextSecondaryBrush,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly List<EditorRow> _editors = [];

    public event EventHandler? PickBaseDirectoryRequested;
    public event EventHandler? ConfigurationChanged;

    public string BaseDirectory
    {
        get => _baseDirectory.Text?.Trim() ?? string.Empty;
        set => _baseDirectory.Text = value;
    }

    public string OutputName
    {
        get => _outputName.Text?.Trim() ?? string.Empty;
        set => _outputName.Text = value;
    }

    public void ReflectWorkflow(LaserPmtWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        _pmtCount.Value = workflow.Targets.OfType<LaserPmtTarget>().Count();
    }

    public LaserPmtPanel()
    {
        Spacing = 12;
        foreach (var input in new Control[] { _baseDirectory, _outputName, _prefix })
            UiTheme.ApplyInputStyle(input);
        UiTheme.ApplySecondaryStyle(_pickBaseButton);
        _pickBaseButton.Click += (_, _) => PickBaseDirectoryRequested?.Invoke(this, EventArgs.Empty);

        Children.Add(Field("基础加工目录", _baseDirectory, _pickBaseButton));
        Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"),
            ColumnSpacing = 12,
            Children =
            {
                Labeled("工件宽度（mm）", _workpieceWidth, 0),
                Labeled("工件高度（mm）", _workpieceHeight, 1),
                Labeled("PMT 数量", _pmtCount, 2),
                Labeled("每行数量", _columns, 3),
                Labeled("Hatch 间距（mm）", _hatchSpacing, 4)
            }
        });
        Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 10,
            Children =
            {
                Labeled("编号前缀", _prefix, 0),
                Labeled("起始编号", _start, 1),
                Labeled("编号步长", _increment, 2),
                Labeled("补零位数", _padding, 3)
            }
        });
        Children.Add(Labeled("LaserPMT 输出名称", _outputName, 0));

        var add = new Button { Content = "+ 添加参数", HorizontalAlignment = HorizontalAlignment.Left };
        UiTheme.ApplyQuietStyle(add, small: true);
        add.Click += (_, _) => AddFirstUnusedParameter();
        Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "自定义参数值",
                            FontSize = 12,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = UiTheme.TextSecondaryBrush,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        Place(add, 1)
                    }
                },
                _rows
            }
        });
        Children.Add(new Border
        {
            Padding = new Thickness(10, 8),
            CornerRadius = UiTheme.ControlRadius,
            Background = UiTheme.SunkenBrush,
            BorderBrush = UiTheme.BorderSubtleBrush,
            BorderThickness = new Thickness(1),
            Child = _summary
        });

        foreach (var control in new Control[]
        {
            _baseDirectory, _outputName, _prefix,
            _workpieceWidth, _workpieceHeight, _pmtCount, _columns, _hatchSpacing,
            _start, _increment, _padding
        })
        {
            if (control is TextBox text)
                text.TextChanged += (_, _) => Refresh();
            else if (control is NumericUpDown number)
                number.ValueChanged += (_, _) => Refresh();
        }
        Refresh();
    }

    public LaserPmtWorkflow CreateWorkflow(LaserPmtBaseMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var workpiece = new LaserPmtWorkflowBounds(
            0,
            0,
            decimal.ToDouble(_workpieceWidth.Value ?? 0),
            decimal.ToDouble(_workpieceHeight.Value ?? 0));
        var numbering = new LaserPmtWorkflowNumbering(
            _prefix.Text?.Trim() ?? string.Empty,
            decimal.ToInt32(_increment.Value ?? 0),
            decimal.ToInt32(_padding.Value ?? 0));
        var workflow = new LaserPmtWorkflow(
            metadata.Identity,
            workpiece,
            decimal.ToDouble(_hatchSpacing.Value ?? 0),
            new LaserPmtCanvasViewport(1, 0, 0),
            new LaserPmtBaseParameterNode(
                "base-parameters",
                new LaserPmtWorkflowPoint(-150, 0),
                metadata.Parameters,
                new HashSet<string>(StringComparer.Ordinal)),
            [],
            [],
            [],
            decimal.ToInt32(_columns.Value ?? 1),
            decimal.ToInt32(_start.Value ?? 1),
            1,
            numbering);
        workflow = LaserPmtWorkflowEditor.SetPmtCount(
            workflow,
            decimal.ToInt32(_pmtCount.Value ?? 1),
            metadata.UnitWidth,
            metadata.UnitHeight,
            () => $"pmt-{Guid.NewGuid():N}");
        var pmts = workflow.Targets.OfType<LaserPmtTarget>()
            .OrderBy(target => target.Number)
            .ToArray();
        foreach (var (row, index) in GetRows().Select((row, index) => (row, index)))
        {
            var nodeId = $"parameter-{Guid.NewGuid():N}";
            var seed = new LaserPmtSingleParameterNode(
                nodeId,
                new LaserPmtWorkflowPoint(-80, index * 34),
                row.Name,
                row.ValuesText,
                []);
            var reconciliation = LaserPmtWorkflowCompiler.ReconcilePorts(
                seed,
                row.ValuesText,
                () => $"port-{Guid.NewGuid():N}");
            if (!reconciliation.Success)
                throw new ArgumentException(reconciliation.Error);
            workflow = LaserPmtWorkflowEditor.AddParameterNode(workflow, reconciliation.Node!);
            for (var targetIndex = 0; targetIndex < pmts.Length; targetIndex++)
            {
                var port = reconciliation.Node!.Ports[targetIndex % reconciliation.Node.Ports.Count];
                workflow = LaserPmtWorkflowEditor.AddConnection(
                    workflow,
                    new LaserPmtConnection(
                        $"connection-{Guid.NewGuid():N}", nodeId, port.Id, pmts[targetIndex].Id));
            }
        }
        return workflow;
    }

    public bool TryBuildWorkflowRequest(
        LaserPmtWorkflow workflow,
        string outputParent,
        string ownerToken,
        out string requestJson,
        out string resolvedOutputName,
        out int targetCount,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        requestJson = string.Empty;
        resolvedOutputName = string.IsNullOrWhiteSpace(OutputName)
            ? $"LaserPMT_{DateTime.Now:yyyyMMdd_HHmmss}"
            : OutputName;
        targetCount = workflow.Targets.Count;
        if (string.IsNullOrWhiteSpace(BaseDirectory) || !Directory.Exists(BaseDirectory))
        {
            error = "请选择包含 machine.json 与 patches 的有效基础加工目录。";
            return false;
        }
        if (resolvedOutputName is "." or ".." ||
            resolvedOutputName.Contains('/') || resolvedOutputName.Contains('\\'))
        {
            error = "LaserPMT 输出名称不能包含路径。";
            return false;
        }
        var compilation = LaserPmtWorkflowCompiler.Compile(workflow);
        var geometryErrors = LaserPmtWorkflowEditor.ValidateGeometry(workflow);
        if (!compilation.IsValid || geometryErrors.Count > 0)
        {
            error = string.Join(Environment.NewLine,
                compilation.Errors.Select(item => item.Message)
                    .Concat(geometryErrors.Select(item => item.Message)));
            return false;
        }
        try
        {
            using var workflowDocument = System.Text.Json.JsonDocument.Parse(
                LaserPmtWorkflowSerializer.Serialize(workflow));
            requestJson = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["request_version"] = 2,
                    ["base_machine_dir"] = BaseDirectory,
                    ["output_dir"] = outputParent,
                    ["output_name"] = resolvedOutputName,
                    ["owner_token"] = ownerToken,
                    ["workflow"] = workflowDocument.RootElement.Clone()
                },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            error = exception.Message;
            return false;
        }
    }

    public bool TryBuildRequest(
        string outputParent,
        string ownerToken,
        out string requestJson,
        out string resolvedOutputName,
        out int jobCount,
        out string error)
    {
        requestJson = string.Empty;
        resolvedOutputName = string.IsNullOrWhiteSpace(OutputName)
            ? $"LaserPMT_{DateTime.Now:yyyyMMdd_HHmmss}"
            : OutputName;
        jobCount = 0;
        if (string.IsNullOrWhiteSpace(BaseDirectory) || !Directory.Exists(BaseDirectory))
        {
            error = "请选择包含 machine.json 与 patches 的有效基础加工目录。";
            return false;
        }
        if (!File.Exists(Path.Combine(BaseDirectory, "machine.json")) ||
            !Directory.Exists(Path.Combine(BaseDirectory, "patches")))
        {
            error = "基础加工目录缺少 machine.json 或 patches。";
            return false;
        }
        if (resolvedOutputName is "." or ".." ||
            resolvedOutputName.Contains('/') || resolvedOutputName.Contains('\\'))
        {
            error = "LaserPMT 输出名称不能包含路径。";
            return false;
        }
        var workpieceWidth = decimal.ToDouble(_workpieceWidth.Value ?? 0);
        var workpieceHeight = decimal.ToDouble(_workpieceHeight.Value ?? 0);
        var columns = decimal.ToInt32(_columns.Value ?? 0);
        if (workpieceWidth <= 0 || workpieceHeight <= 0)
        {
            error = "工件宽度和高度必须大于 0。";
            return false;
        }
        if (columns <= 0)
        {
            error = "每行数量必须大于 0。";
            return false;
        }
        var rows = GetRows();
        if (!LaserPmtConfiguration.TryParseRows(rows, out _, out jobCount, out error))
            return false;
        try
        {
            requestJson = LaserPmtConfiguration.BuildRequestJson(
                BaseDirectory,
                outputParent,
                resolvedOutputName,
                workpieceWidth,
                workpieceHeight,
                columns,
                _prefix.Text?.Trim() ?? string.Empty,
                decimal.ToInt32(_start.Value ?? 0),
                decimal.ToInt32(_increment.Value ?? 0),
                decimal.ToInt32(_padding.Value ?? 0),
                rows,
                ownerToken);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            error = exception.Message;
            return false;
        }
    }

    private IReadOnlyList<LaserPmtParameterRow> GetRows() => _editors.Select(editor =>
    {
        var definition = (LaserPmtParameterDefinition?)editor.Parameter.SelectedItem;
        return new LaserPmtParameterRow(
            definition?.Name ?? string.Empty,
            editor.Values.Text ?? string.Empty);
    }).ToArray();

    private void AddFirstUnusedParameter()
    {
        var used = _editors
            .Select(editor => (editor.Parameter.SelectedItem as LaserPmtParameterDefinition)?.Name)
            .ToHashSet(StringComparer.Ordinal);
        var definition = LaserPmtConfiguration.Parameters.FirstOrDefault(item => !used.Contains(item.Name));
        if (definition is not null)
            AddParameter(definition.Name, definition.IsBoolean ? "true, false" : "0");
    }

    private void AddParameter(string name, string values)
    {
        var selector = new ComboBox
        {
            ItemsSource = LaserPmtConfiguration.Parameters,
            SelectedItem = LaserPmtConfiguration.Parameters.First(item => item.Name == name),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        selector.ItemTemplate = new FuncDataTemplate<LaserPmtParameterDefinition>(
            (item, _) => new TextBlock { Text = item.DisplayName }, true);
        var valueBox = new TextBox
        {
            Text = values,
            Watermark = "逗号分隔，例如 20, 30, 40",
            FontFamily = UiTheme.MonoFont
        };
        var delete = new Button { Content = "删除" };
        UiTheme.ApplyInputStyle(selector);
        UiTheme.ApplyInputStyle(valueBox);
        UiTheme.ApplyQuietStyle(delete, small: true);
        var container = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("0.9*,1.3*,Auto"),
            ColumnSpacing = 8,
            Children = { Place(selector, 0), Place(valueBox, 1), Place(delete, 2) }
        };
        var editor = new EditorRow(container, selector, valueBox, delete);
        _editors.Add(editor);
        _rows.Children.Add(container);
        selector.SelectionChanged += (_, _) => Refresh();
        valueBox.TextChanged += (_, _) => Refresh();
        delete.Click += (_, _) =>
        {
            _editors.Remove(editor);
            _rows.Children.Remove(container);
            Refresh();
        };
        Refresh();
    }

    private void Refresh()
    {
        var rows = GetRows();
        if (!LaserPmtConfiguration.TryParseRows(rows, out _, out var count, out var error))
        {
            _summary.Text = error;
            _summary.Foreground = UiTheme.DangerTextBrush;
        }
        else
        {
            var columns = Math.Max(1, decimal.ToInt32(_columns.Value ?? 1));
            var effectiveColumns = Math.Min(columns, count);
            var matrixRows = (count + effectiveColumns - 1) / effectiveColumns;
            _summary.Text = $"{count} 个组合 · {matrixRows} 行 × {effectiveColumns} 列 · " +
                "四周及单元间距将在生成时按工件尺寸自动均分";
            _summary.Foreground = UiTheme.TextSecondaryBrush;
        }
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    private static NumericUpDown NumberBox(
        decimal value,
        decimal increment,
        decimal maximum,
        int decimals,
        decimal minimum = 0)
    {
        var box = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Increment = increment,
            FormatString = decimals == 0 ? "0" : $"0.{new string('#', decimals)}",
            FontFamily = UiTheme.MonoFont,
            ShowButtonSpinner = true
        };
        UiTheme.ApplyInputStyle(box);
        return box;
    }

    private static Control Field(string label, Control field, Button button) => new StackPanel
    {
        Spacing = 7,
        Children =
        {
            UiTheme.FieldLabel(label),
            new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8,
                Children = { Place(field, 0), Place(button, 1) }
            }
        }
    };

    private static Control Labeled(string label, Control control, int column)
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
}
