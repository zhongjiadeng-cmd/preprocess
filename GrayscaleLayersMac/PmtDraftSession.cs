using System.Collections.ObjectModel;

namespace GrayscaleLayersMac;

public enum PmtNavigationDirection
{
    Left,
    Right,
    Up,
    Down
}

public sealed record PmtDraftSnapshot(
    PmtSourceCatalog Sources,
    LaserPmtWorkflow Workflow,
    LaserPmtWorkflow? PreviewWorkflow,
    string OutputName,
    IReadOnlySet<string> SelectedTargetIds,
    string? PrimaryTargetId,
    long CurrentRevision,
    long SavedRevision)
{
    public bool IsDirty => CurrentRevision != SavedRevision;
    public LaserPmtWorkflow DisplayWorkflow => PreviewWorkflow ?? Workflow;
}

public sealed class PmtDraftChangedEventArgs(bool isTransientPreview) : EventArgs
{
    public bool IsTransientPreview { get; } = isTransientPreview;
}

public sealed class PmtDraftSession
{
    private PmtSourceCatalog _sources;
    private LaserPmtWorkflow _workflow;
    private LaserPmtWorkflow? _previewWorkflow;
    private string _outputName;
    private HashSet<string> _selectedTargetIds = new(StringComparer.Ordinal);
    private string? _primaryTargetId;
    private long _currentRevision;
    private long _savedRevision;

    public event EventHandler<PmtDraftChangedEventArgs>? Changed;

    public PmtDraftSnapshot Snapshot => new(
        _sources,
        _workflow,
        _previewWorkflow,
        _outputName,
        new HashSet<string>(_selectedTargetIds, StringComparer.Ordinal),
        _primaryTargetId,
        _currentRevision,
        _savedRevision);

    public PmtDraftSession(
        PmtSourceCatalog sources,
        LaserPmtWorkflow workflow,
        string outputName = "")
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _outputName = outputName?.Trim() ?? string.Empty;
        ValidateSourceAgreement(_sources, _workflow);
    }

    public static PmtDraftSession Create(
        PmtSourceCatalog sources,
        LaserPmtWorkflowBounds workpiece,
        double hatchSpacing,
        string outputName = "")
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Sources.Count == 0)
            throw new ArgumentException("至少需要一个 PMT 原始来源。", nameof(sources));
        var workflowSources = sources.Sources.Select(source => new LaserPmtWorkflowSource(
            source.Id,
            source.Directory,
            source.DisplayName,
            source.Mark,
            source.ColorArgb,
            source.NativeWidth,
            source.NativeHeight,
            source.Fingerprint)).ToArray();
        var baseNodes = sources.Sources.Select((source, index) =>
            new LaserPmtBaseParameterNode(
                $"base-{source.Id}",
                new LaserPmtWorkflowPoint(-180, index * 100),
                source.BaseParameters,
                new HashSet<string>(StringComparer.Ordinal))
            {
                SourceId = source.Id
            }).ToArray();
        var workflow = new LaserPmtWorkflow(
            workflowSources,
            workpiece,
            hatchSpacing,
            new LaserPmtCanvasViewport(1, 0, 0),
            baseNodes,
            [],
            [],
            [],
            1,
            1,
            1,
            new LaserPmtWorkflowNumbering(string.Empty, 1, 1));
        return new PmtDraftSession(sources, workflow, outputName);
    }

    public void PreviewMatrix(int rows, int columns) =>
        SetPreview(CreateMatrixWorkflow(rows, columns));

    public void CancelMatrixPreview()
    {
        if (_previewWorkflow is null)
            return;
        _previewWorkflow = null;
        RaiseChanged(isTransientPreview: true);
    }

    public void CommitMatrix(int rows, int columns)
    {
        _selectedTargetIds.Clear();
        _primaryTargetId = null;
        Commit(CreateMatrixWorkflow(rows, columns));
    }

    public void ApplyWorkflow(LaserPmtWorkflow workflow) => Commit(workflow);

    public void SetOutputName(string outputName)
    {
        var normalized = outputName?.Trim() ?? string.Empty;
        if (string.Equals(_outputName, normalized, StringComparison.Ordinal))
            return;
        _outputName = normalized;
        IncrementRevision();
    }

    public void SelectSingle(string? targetId)
    {
        _selectedTargetIds.Clear();
        if (targetId is not null)
        {
            EnsurePmt(targetId);
            _selectedTargetIds.Add(targetId);
        }
        _primaryTargetId = targetId;
        RaiseChanged(isTransientPreview: true);
    }

    public void ToggleSelection(string targetId)
    {
        EnsurePmt(targetId);
        if (!_selectedTargetIds.Add(targetId))
            _selectedTargetIds.Remove(targetId);
        _primaryTargetId = _selectedTargetIds.Contains(targetId)
            ? targetId
            : _selectedTargetIds.LastOrDefault();
        RaiseChanged(isTransientPreview: true);
    }

    public void SelectInDirection(PmtNavigationDirection direction)
    {
        var pmts = _workflow.Targets.OfType<LaserPmtTarget>().ToArray();
        if (pmts.Length == 0)
            return;
        var current = pmts.FirstOrDefault(target => target.Id == _primaryTargetId);
        if (current is null)
        {
            SelectSingle(pmts.OrderBy(target => target.Bounds.Top)
                .ThenBy(target => target.Bounds.Left).First().Id);
            return;
        }
        var center = Center(current.Bounds);
        var next = pmts
            .Where(target => target.Id != current.Id)
            .Select(target => (Target: target, Delta: Delta(center, Center(target.Bounds))))
            .Where(item => IsInDirection(item.Delta, direction))
            .OrderBy(item => AlignmentScore(item.Delta, direction))
            .ThenBy(item => Math.Sqrt(
                item.Delta.X * item.Delta.X + item.Delta.Y * item.Delta.Y))
            .Select(item => item.Target)
            .FirstOrDefault();
        if (next is not null)
            SelectSingle(next.Id);
    }

    public void NudgeSelected(PmtNavigationDirection direction, double millimetres)
    {
        if (!double.IsFinite(millimetres) || millimetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(millimetres));
        if (_selectedTargetIds.Count == 0)
            return;
        var dx = direction switch
        {
            PmtNavigationDirection.Left => -millimetres,
            PmtNavigationDirection.Right => millimetres,
            _ => 0
        };
        var dy = direction switch
        {
            PmtNavigationDirection.Up => -millimetres,
            PmtNavigationDirection.Down => millimetres,
            _ => 0
        };
        var updated = _workflow;
        foreach (var id in _selectedTargetIds)
        {
            var target = updated.Targets.OfType<LaserPmtTarget>().Single(item => item.Id == id);
            updated = LaserPmtWorkflowEditor.MovePmt(
                updated,
                id,
                target.Bounds.Left + dx,
                target.Bounds.Top + dy);
        }
        Commit(updated);
    }

    public void DeleteSelected()
    {
        if (_selectedTargetIds.Count == 0)
            return;
        var updated = _workflow;
        foreach (var id in _selectedTargetIds)
            updated = LaserPmtWorkflowEditor.DeletePmt(updated, id);
        _selectedTargetIds.Clear();
        _primaryTargetId = null;
        Commit(updated);
    }

    public void RenumberByPosition() =>
        Commit(LaserPmtWorkflowEditor.RenumberByPosition(_workflow));

    public void MarkSaved(long revision)
    {
        if (revision < 0 || revision > _currentRevision)
            throw new ArgumentOutOfRangeException(nameof(revision));
        _savedRevision = Math.Max(_savedRevision, revision);
        RaiseChanged(isTransientPreview: true);
    }

    private LaserPmtWorkflow CreateMatrixWorkflow(int rows, int columns)
    {
        if (rows <= 0)
            throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns));
        var count = checked(rows * columns);
        if (count > LaserPmtConfiguration.MaximumJobs)
            throw new ArgumentOutOfRangeException(nameof(rows), "PMT 数量超过上限。");
        var active = _sources.ActiveSource
            ?? throw new InvalidOperationException("请先选择 PMT 原始来源。");
        var bounds = LaserPmtWorkflowEditor.CalculateAutomaticBounds(
            _workflow.Workpiece,
            count,
            columns,
            active.NativeWidth,
            active.NativeHeight);
        var retainedTargets = _workflow.Targets
            .Where(target => target is not LaserPmtTarget)
            .ToArray();
        var pmts = bounds.Select((item, index) =>
            (LaserPmtWorkflowTarget)new LaserPmtTarget(
                $"pmt-{Guid.NewGuid():N}",
                index + 1,
                item,
                false)
            {
                SourceId = active.Id,
                NativeWidth = active.NativeWidth,
                NativeHeight = active.NativeHeight,
                IsSizeLocked = true
            }).ToArray();
        var retainedIds = retainedTargets.Select(target => target.Id).ToHashSet(StringComparer.Ordinal);
        return new LaserPmtWorkflow(
            _workflow.Sources,
            _workflow.Workpiece,
            _workflow.HatchSpacing,
            _workflow.Viewport,
            _workflow.BaseNodes,
            _workflow.ParameterNodes,
            retainedTargets.Concat(pmts).ToArray(),
            _workflow.Connections.Where(connection => retainedIds.Contains(connection.TargetId)).ToArray(),
            columns,
            count + 1,
            _workflow.NextCreationOrder,
            new LaserPmtWorkflowNumbering(string.Empty, 1, 1));
    }

    private void SetPreview(LaserPmtWorkflow workflow)
    {
        _previewWorkflow = workflow;
        RaiseChanged(isTransientPreview: true);
    }

    private void Commit(LaserPmtWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ValidateSourceAgreement(_sources, workflow);
        _workflow = workflow;
        _previewWorkflow = null;
        var validIds = workflow.Targets.OfType<LaserPmtTarget>()
            .Select(target => target.Id)
            .ToHashSet(StringComparer.Ordinal);
        _selectedTargetIds.IntersectWith(validIds);
        if (_primaryTargetId is not null && !validIds.Contains(_primaryTargetId))
            _primaryTargetId = _selectedTargetIds.LastOrDefault();
        IncrementRevision();
    }

    private void IncrementRevision()
    {
        _currentRevision = checked(_currentRevision + 1);
        RaiseChanged(isTransientPreview: false);
    }

    private void RaiseChanged(bool isTransientPreview) =>
        Changed?.Invoke(this, new PmtDraftChangedEventArgs(isTransientPreview));

    private void EnsurePmt(string targetId)
    {
        if (!_workflow.Targets.OfType<LaserPmtTarget>().Any(target => target.Id == targetId))
            throw new ArgumentException($"找不到 PMT：{targetId}", nameof(targetId));
    }

    private static void ValidateSourceAgreement(PmtSourceCatalog sources, LaserPmtWorkflow workflow)
    {
        var catalogIds = sources.Sources.Select(source => source.Id).ToHashSet(StringComparer.Ordinal);
        var workflowIds = workflow.Sources.Select(source => source.Id).ToHashSet(StringComparer.Ordinal);
        if (!catalogIds.SetEquals(workflowIds))
            throw new ArgumentException("PMT 草稿与原始来源目录不一致。", nameof(workflow));
    }

    private static LaserPmtWorkflowPoint Center(LaserPmtWorkflowBounds bounds) =>
        new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

    private static LaserPmtWorkflowPoint Delta(
        LaserPmtWorkflowPoint from,
        LaserPmtWorkflowPoint to) => new(to.X - from.X, to.Y - from.Y);

    private static bool IsInDirection(LaserPmtWorkflowPoint delta, PmtNavigationDirection direction) =>
        direction switch
        {
            PmtNavigationDirection.Left => delta.X < 0,
            PmtNavigationDirection.Right => delta.X > 0,
            PmtNavigationDirection.Up => delta.Y < 0,
            PmtNavigationDirection.Down => delta.Y > 0,
            _ => false
        };

    private static double AlignmentScore(
        LaserPmtWorkflowPoint delta,
        PmtNavigationDirection direction)
    {
        var primary = direction is PmtNavigationDirection.Left or PmtNavigationDirection.Right
            ? Math.Abs(delta.X)
            : Math.Abs(delta.Y);
        var orthogonal = direction is PmtNavigationDirection.Left or PmtNavigationDirection.Right
            ? Math.Abs(delta.Y)
            : Math.Abs(delta.X);
        return orthogonal / primary;
    }
}
