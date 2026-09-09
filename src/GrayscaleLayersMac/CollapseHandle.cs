using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GrayscaleLayersMac;

/// <summary>
/// 抽屉把手的方向：决定胶囊的长轴方向，以及箭头在展开 / 折叠两态之间绕哪个轴翻转。
/// </summary>
internal enum CollapseHandleOrientation
{
    /// <summary>横向胶囊（56×20），骑在被收拢区域的上下边缘；箭头在「朝下 / 朝上」间翻转。</summary>
    Horizontal,

    /// <summary>纵向胶囊（20×56），骑在被收拢区域的左右边缘；箭头在「朝左 / 朝右」间翻转。</summary>
    Vertical
}

/// <summary>
/// 骑在被收拢区域边缘上、居中悬浮的抽屉把手：胶囊外形 + 人字箭头，点击即在展开 / 折叠间切换。
/// 把手只负责呈现与点击，具体收多大由宿主订阅 <see cref="Toggled"/> 后决定
/// —— 日志面板收的是高度，图层缩略图收的是宽度，两者共用同一个把手。
/// </summary>
internal sealed class CollapseHandle : Button
{
    private static readonly TimeSpan IconMotion = TimeSpan.FromMilliseconds(320);
    private static readonly Easing Motion = new CubicEaseOut();

    private readonly RotateTransform _rotation;
    private readonly double _expandedAngle;
    private readonly double _collapsedAngle;
    private readonly string _expandedTip;
    private readonly string _collapsedTip;
    private bool _collapsed;
    private bool _motionAttached;

    /// <param name="orientation">收拢方向，决定胶囊长轴与箭头翻转轴。</param>
    /// <param name="expandedTip">展开态（点击会收起）的提示文案。</param>
    /// <param name="collapsedTip">折叠态（点击会展开）的提示文案。</param>
    public CollapseHandle(
        CollapseHandleOrientation orientation,
        string expandedTip,
        string collapsedTip)
    {
        _expandedTip = expandedTip;
        _collapsedTip = collapsedTip;

        var horizontal = orientation == CollapseHandleOrientation.Horizontal;

        // 人字几何默认朝下，Avalonia 的 RotateTransform 正角度为顺时针：
        // 横向 0°=朝下(下缩) ↔ 180°=朝上(上拉)；纵向 90°=朝左(收起) ↔ 270°=朝右(展开)。
        _expandedAngle = horizontal ? 0 : 90;
        _collapsedAngle = horizontal ? 180 : 270;

        _rotation = new RotateTransform { Angle = _expandedAngle };

        var chevron = UiIcons.CreateSmall(UiIcon.Collapse);
        chevron.Width = 15;
        chevron.Height = 15;
        chevron.RenderTransform = _rotation;
        chevron.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        Content = chevron;
        Width = horizontal ? 56 : 20;
        Height = horizontal ? 20 : 56;
        Padding = new Thickness(0);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        // 类内部 HorizontalAlignment / VerticalAlignment 会被 Layoutable 的同名属性遮蔽，
        // 因此这里必须用全限定名取枚举值。
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        // 骑在宿主边缘之上，避免被卡片裁掉。
        ZIndex = 1;
        Classes.Add("panel-handle");
        UiTheme.AttachButtonTransitions(this);

        // 光标与过渡动画依赖平台服务（ICursorFactory / IGlobalClock），
        // 因此统一推迟到真正挂上可视化树时再装配。
        AttachedToVisualTree += (_, _) =>
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            AttachMotion();
        };
        Click += (_, _) => SetCollapsed(!_collapsed);

        ApplyTooltip();
    }

    /// <summary>是否处于折叠态。</summary>
    public bool IsCollapsed => _collapsed;

    /// <summary>箭头的目标旋转角，随折叠状态在两态角度间切换。</summary>
    public double ChevronAngle => _rotation.Angle;

    /// <summary>当前提示文案。</summary>
    public string TooltipText => ToolTip.GetTip(this)?.ToString() ?? string.Empty;

    /// <summary>点击把手导致折叠状态切换后触发，宿主据此调整被收拢区域的尺寸。</summary>
    public event EventHandler? Toggled;

    /// <summary>
    /// 切换折叠状态并同步箭头角度与提示文案，随后触发 <see cref="Toggled"/>。
    /// 状态未变化时不触发，便于宿主在恢复持久化状态时先静默设定再订阅。
    /// </summary>
    public void SetCollapsed(bool value)
    {
        if (_collapsed == value)
            return;

        _collapsed = value;
        _rotation.Angle = value ? _collapsedAngle : _expandedAngle;
        ApplyTooltip();
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 装配箭头的旋转过渡。带 Transitions 的属性一旦变化就需要 IGlobalClock，
    /// 无头环境缺失该服务，所以推迟到挂载时再装；角度初值在装配前已写好，不会从旧值起步抖动。
    /// </summary>
    private void AttachMotion()
    {
        if (_motionAttached)
            return;

        _motionAttached = true;
        if (!MotionPreferences.AnimateSpatialProperties)
            return;
        _rotation.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = RotateTransform.AngleProperty,
                Duration = IconMotion,
                Easing = Motion
            }
        };
    }

    private void ApplyTooltip()
    {
        var text = _collapsed ? _collapsedTip : _expandedTip;
        ToolTip.SetTip(this, text);
        AutomationProperties.SetName(this, text);
    }
}
