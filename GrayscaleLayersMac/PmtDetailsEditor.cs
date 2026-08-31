using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

/// <summary>
/// 选中 PMT 单元时右侧显示的可编辑详情面板：
/// 上方是单元标识 + 文件名，下方是激光参数的竖向编辑列表（一行一项，对应
/// <see cref="LaserPmtConfiguration.Parameters"/> 全部 16 项），最下面是"保存覆盖"与"还原基础"。
/// </summary>
public sealed class PmtDetailsEditor : UserControl
{
    /// <summary>当用户按下"保存覆盖"时触发，携带新参数与目标 job 编号。</summary>
    public event EventHandler<PmtDetailsSaveEventArgs>? SaveRequested;

    /// <summary>当用户按下"还原基础"时触发，便于上级回滚激光参数矩阵预览。</summary>
    public event EventHandler? ResetRequested;

    private readonly TextBlock _identifierText = new()
    {
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Foreground = UiTheme.TextPrimaryBrush,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _jsonFileText = new()
    {
        FontSize = 11.5,
        Foreground = UiTheme.TextSecondaryBrush,
        FontFamily = UiTheme.MonoFont,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly StackPanel _parameterRows = new() { Spacing = 6 };
    private readonly TextBlock _statusText = new()
    {
        FontSize = 11.5,
        Foreground = UiTheme.TextSecondaryBrush,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly Button _saveButton = new() { Content = "保存覆盖" };
    private readonly Button _resetButton = new() { Content = "还原基础" };

    private readonly List<ParameterRow> _rows = new();
    private LaserPmtJobLayout? _job;
    private Dictionary<string, string> _baselineParameters = new(StringComparer.Ordinal);
    private string? _jobIdentifier;

    public PmtDetailsEditor()
    {
        UiTheme.ApplySecondaryStyle(_saveButton);
        UiTheme.ApplyGhostStyle(_resetButton, small: true);
        _saveButton.Click += (_, _) => RaiseSave();
        _resetButton.Click += (_, _) =>
        {
            if (_job is null)
                return;
            foreach (var row in _rows)
                row.Reset();
            _statusText.Text = "已还原为该单元生成时的覆盖参数。";
            _statusText.Foreground = UiTheme.TextSecondaryBrush;
            UpdateButtons();
            ResetRequested?.Invoke(this, EventArgs.Empty);
        };

        foreach (var definition in LaserPmtConfiguration.Parameters)
            _rows.Add(new ParameterRow(definition, _parameterRows));

        var header = new StackPanel
        {
            Spacing = 4,
            Children = { _identifierText, _jsonFileText }
        };
        var scroll = new ScrollViewer
        {
            Content = _parameterRows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _saveButton, _resetButton, _statusText }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 10
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(scroll, 1);
        Grid.SetRow(actions, 2);
        root.Children.Add(header);
        root.Children.Add(scroll);
        root.Children.Add(actions);

        Content = root;
        ShowEmptyState();
    }

    /// <summary>
    /// 切换到指定 job 的编辑态；传入 <c>null</c> 表示回到提示态。
    /// 切换时编辑器值同步为新 job 的覆盖参数，原 job 的修改会被丢弃。
    /// </summary>
    public void LoadJob(LaserPmtJobLayout? job)
    {
        _job = job;
        if (job is null)
        {
            ShowEmptyState();
            return;
        }
        _jobIdentifier = job.Identifier;
        _baselineParameters = new Dictionary<string, string>(job.Parameters, StringComparer.Ordinal);
        _identifierText.Text =
            $"{job.Identifier} · 第 {job.Row + 1} 行 / 第 {job.Column + 1} 列 · " +
            $"左上 ({job.Left:0.###}, {job.Top:0.###}) mm · 层间进给 {job.LayerFeedUm} μm";
        _jsonFileText.Text = job.JsonFile;
        foreach (var row in _rows)
            row.LoadFrom(job.Parameters);
        _statusText.Text = "未覆盖的参数将沿用基础加工值。";
        _statusText.Foreground = UiTheme.TextSecondaryBrush;
        UpdateButtons();
    }

    /// <summary>把当前编辑器里的值以字典形式返回；空值不包含。</summary>
    public IReadOnlyDictionary<string, string> CollectValidParameters()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in _rows)
        {
            var value = row.ReadValueOrNull();
            if (value is not null)
                result[row.Definition.Name] = value;
        }
        return result;
    }

    private void RaiseSave()
    {
        if (_job is null || _jobIdentifier is null)
            return;

        var collected = CollectValidParameters();
        if (!TryDetectErrors(collected, out var error))
        {
            _statusText.Text = error;
            _statusText.Foreground = UiTheme.DangerTextBrush;
            return;
        }
        _statusText.Text = "已保存到 PMT 布局和单元机器文件。";
        _statusText.Foreground = UiTheme.SuccessTextBrush;
        _baselineParameters = new Dictionary<string, string>(collected, StringComparer.Ordinal);
        UpdateButtons();
        SaveRequested?.Invoke(this, new PmtDetailsSaveEventArgs(_jobIdentifier, collected));
    }

    private bool TryDetectErrors(
        IReadOnlyDictionary<string, string> collected,
        out string error)
    {
        foreach (var row in _rows)
        {
            if (!row.Validate())
            {
                error = $"{row.Definition.DisplayName} 输入超出允许范围。";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    private void ShowEmptyState()
    {
        _jobIdentifier = null;
        _job = null;
        _baselineParameters = new Dictionary<string, string>(StringComparer.Ordinal);
        _identifierText.Text = "点击线框可查看编号、位置与参数。";
        _jsonFileText.Text = string.Empty;
        foreach (var row in _rows)
            row.LoadFrom(new Dictionary<string, string>(StringComparer.Ordinal));
        _statusText.Text = string.Empty;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var hasJob = _job is not null;
        _saveButton.IsEnabled = hasJob;
        _resetButton.IsEnabled = hasJob;
        foreach (var row in _rows)
            row.SetEnabled(hasJob);
    }

    private sealed class ParameterRow
    {
        private readonly LaserPmtParameterDefinition _definition;
        private readonly NumericUpDown? _numberBox;
        private readonly CheckBox? _checkBox;
        private readonly Control _editor;
        private readonly TextBlock _errorText = new()
        {
            FontSize = 10.5,
            Foreground = UiTheme.DangerTextBrush,
            TextWrapping = TextWrapping.Wrap
        };
        private bool _isEnabled;

        public LaserPmtParameterDefinition Definition => _definition;

        public ParameterRow(LaserPmtParameterDefinition definition, StackPanel container)
        {
            _definition = definition;
            if (definition.IsBoolean)
            {
                _checkBox = new CheckBox
                {
                    Content = definition.DisplayName,
                    IsThreeState = false
                };
                _checkBox.IsCheckedChanged += (_, _) => UpdateErrorIndicator();
                _editor = _checkBox;
            }
            else
            {
                _numberBox = new NumericUpDown
                {
                    Minimum = definition.Minimum,
                    Maximum = definition.Maximum == int.MaxValue ? decimal.MaxValue : definition.Maximum,
                    Increment = 1,
                    FormatString = "0",
                    FontFamily = UiTheme.MonoFont,
                    Watermark = "沿用基础加工参数"
                };
                UiTheme.ApplyInputStyle(_numberBox);
                _numberBox.ValueChanged += (_, _) => UpdateErrorIndicator();
                _editor = _numberBox;
            }

            var label = UiTheme.FieldLabel(
                definition.IsBoolean ? "勾选 = 启用覆盖" : $"{definition.DisplayName}（{definition.Name}）");
            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(_editor, 1);
            Grid.SetColumn(_errorText, 2);
            rowGrid.Children.Add(label);
            rowGrid.Children.Add(_editor);
            rowGrid.Children.Add(_errorText);
            container.Children.Add(rowGrid);
            _isEnabled = true;
        }

        public void LoadFrom(IReadOnlyDictionary<string, string> parameters)
        {
            var hasValue = parameters.TryGetValue(_definition.Name, out var raw);
            if (_definition.IsBoolean)
            {
                if (_checkBox is null)
                    return;
                if (!hasValue || !bool.TryParse(raw, out var boolean))
                {
                    _checkBox.IsChecked = null;
                    _checkBox.IsThreeState = true;
                    return;
                }
                _checkBox.IsThreeState = false;
                _checkBox.IsChecked = boolean;
            }
            else
            {
                if (_numberBox is null)
                    return;
                if (!hasValue || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
                {
                    _numberBox.Value = null;
                    return;
                }
                if (integer < _definition.Minimum || integer > _definition.Maximum)
                {
                    _numberBox.Value = null;
                    return;
                }
                _numberBox.Value = integer;
            }
            UpdateErrorIndicator();
        }

        public void Reset()
        {
            // 没有 baseline 就只清空编辑器
            LoadFrom(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            _editor.IsEnabled = enabled;
        }

        public string? ReadValueOrNull()
        {
            if (!_isEnabled)
                return null;
            if (_definition.IsBoolean)
            {
                if (_checkBox is null)
                    return null;
                if (_checkBox.IsThreeState || _checkBox.IsChecked is null)
                    return null;
                return _checkBox.IsChecked.Value ? "true" : "false";
            }
            if (_numberBox is null)
                return null;
            if (_numberBox.Value is null)
                return null;
            var raw = decimal.ToInt32(_numberBox.Value.Value);
            if (raw < _definition.Minimum || raw > _definition.Maximum)
                return null;
            return raw.ToString(CultureInfo.InvariantCulture);
        }

        public bool Validate()
        {
            if (!_isEnabled)
                return true;
            if (_definition.IsBoolean)
                return _checkBox is { IsThreeState: false } || _checkBox is null;
            return true;
        }

        private void UpdateErrorIndicator()
        {
            if (_numberBox is null)
            {
                _errorText.Text = string.Empty;
                return;
            }
            if (_numberBox.Value is null)
            {
                _errorText.Text = string.Empty;
                UiTheme.SetInputError(_numberBox, false);
                return;
            }
            var raw = decimal.ToInt32(_numberBox.Value.Value);
            var valid = raw >= _definition.Minimum && raw <= _definition.Maximum;
            UiTheme.SetInputError(_numberBox, !valid);
            _errorText.Text = valid
                ? string.Empty
                : $"允许 {_definition.Minimum}–{(_definition.Maximum == int.MaxValue ? "无穷" : _definition.Maximum.ToString(CultureInfo.InvariantCulture))}";
        }
    }
}

public sealed record PmtDetailsSaveEventArgs(string JobIdentifier, IReadOnlyDictionary<string, string> Parameters);
