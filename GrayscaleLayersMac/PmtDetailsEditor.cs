using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace GrayscaleLayersMac;

/// <summary>
/// 选中 PMT 单元时右侧竖栏里的可编辑参数列表。
/// 整体置于预览区右侧的紧凑竖栏中：上方是单元标识+文件名，
/// 中间是 <see cref="LaserPmtConfiguration.Parameters"/> 全部 16 项（一行一项，
/// 整数用无加减按钮的 <see cref="TextBox"/>、布尔用三态 <see cref="CheckBox"/>），
/// 最底下是"保存"与"还原"图标按钮 + 状态行。
/// </summary>
public sealed class PmtDetailsEditor : UserControl
{
    /// <summary>当用户按下"保存"时触发，携带新参数与目标 job 编号。</summary>
    public event EventHandler<PmtDetailsSaveEventArgs>? SaveRequested;

    /// <summary>当用户按下"还原"时触发，便于上级回滚激光参数矩阵预览。</summary>
    public event EventHandler? ResetRequested;

    private readonly TextBlock _identifierText = new()
    {
        FontSize = 11.5,
        FontWeight = FontWeight.SemiBold,
        Foreground = UiTheme.TextPrimaryBrush,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _jsonFileText = new()
    {
        FontSize = 9.5,
        Foreground = UiTheme.TextSecondaryBrush,
        FontFamily = UiTheme.MonoFont,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly StackPanel _parameterRows = new() { Spacing = 2 };
    private readonly TextBlock _statusText = new()
    {
        FontSize = 10.5,
        Foreground = UiTheme.TextSecondaryBrush,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _saveButton = new() { Content = UiIcons.CreateSmall(UiIcon.Save) };
    private readonly Button _resetButton = new() { Content = UiIcons.CreateSmall(UiIcon.Undo) };

    private readonly List<ParameterRow> _rows = new();
    private LaserPmtJobLayout? _job;
    private Dictionary<string, string> _baselineParameters = new(StringComparer.Ordinal);
    private string? _jobIdentifier;

    public PmtDetailsEditor()
    {
        Width = 220;
        UiTheme.ApplyIconStyle(_saveButton, "保存覆盖");
        UiTheme.ApplyIconStyle(_resetButton, "还原基础");
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
            Spacing = 2,
            Margin = new Thickness(0, 0, 0, 4),
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
            Spacing = 6,
            Margin = new Thickness(0, 4, 0, 0),
            Children = { _saveButton, _resetButton, _statusText }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 4
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

    /// <summary>把当前编辑器里的值以字典形式返回；空值或非法值不包含。</summary>
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
        private readonly TextBox? _textBox;
        private readonly CheckBox? _checkBox;
        private readonly Control _editor;
        private bool _isEnabled;
        private bool _hasError;

        public LaserPmtParameterDefinition Definition => _definition;

        public ParameterRow(LaserPmtParameterDefinition definition, StackPanel container)
        {
            _definition = definition;
            if (definition.IsBoolean)
            {
                _checkBox = new CheckBox
                {
                    Content = definition.DisplayName,
                    IsThreeState = true,
                    FontSize = 11,
                    Foreground = UiTheme.TextPrimaryBrush
                };
                _checkBox.IsCheckedChanged += (_, _) => UpdateErrorIndicator();
                _editor = _checkBox;
                container.Children.Add(_checkBox);
                return;
            }

            var label = new TextBlock
            {
                Text = definition.DisplayName,
                FontSize = 10,
                Foreground = UiTheme.TextSecondaryBrush,
                Margin = new Thickness(0, 0, 0, 1)
            };
            _textBox = new TextBox
            {
                Watermark = "沿用基础加工参数",
                FontFamily = UiTheme.MonoFont,
                FontSize = 11
            };
            UiTheme.ApplyInputStyle(_textBox);
            // ApplyInputStyle 会设定默认内边距/最小高度，这里收紧以匹配紧凑竖栏。
            _textBox.MinHeight = 0;
            _textBox.Height = 24;
            _textBox.Padding = new Thickness(5, 2);
            _textBox.TextChanged += (_, _) => UpdateErrorIndicator();
            _editor = _textBox;
            container.Children.Add(new StackPanel
            {
                Spacing = 1,
                Margin = new Thickness(0, 0, 0, 3),
                Children = { label, _textBox }
            });
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
                    _checkBox.IsThreeState = true;
                    _checkBox.IsChecked = null;
                    return;
                }
                _checkBox.IsThreeState = false;
                _checkBox.IsChecked = boolean;
            }
            else
            {
                if (_textBox is null)
                    return;
                if (!hasValue || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var integer)
                    || integer < _definition.Minimum || integer > _definition.Maximum)
                {
                    _textBox.Text = null;
                    return;
                }
                _textBox.Text = integer.ToString(CultureInfo.InvariantCulture);
            }
            UpdateErrorIndicator();
        }

        public void Reset()
        {
            LoadFrom(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            _editor.IsEnabled = enabled;
        }

        public string? ReadValueOrNull()
        {
            if (!_isEnabled || _hasError)
                return null;
            if (_definition.IsBoolean)
            {
                if (_checkBox is null || _checkBox.IsChecked is null)
                    return null;
                return _checkBox.IsChecked.Value ? "true" : "false";
            }
            if (_textBox is null)
                return null;
            var text = _textBox.Text;
            if (string.IsNullOrWhiteSpace(text))
                return null;
            return text.Trim();
        }

        public bool Validate() => !_hasError;

        private void UpdateErrorIndicator()
        {
            if (_textBox is null)
                return;
            if (string.IsNullOrWhiteSpace(_textBox.Text))
            {
                _hasError = false;
                UiTheme.SetInputError(_textBox, false);
                return;
            }
            var ok = int.TryParse(
                    _textBox.Text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value)
                && value >= _definition.Minimum
                && value <= _definition.Maximum;
            _hasError = !ok;
            UiTheme.SetInputError(_textBox, !ok);
        }
    }
}

public sealed record PmtDetailsSaveEventArgs(string JobIdentifier, IReadOnlyDictionary<string, string> Parameters);
