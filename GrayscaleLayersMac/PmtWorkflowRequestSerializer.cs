using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrayscaleLayersMac;

/// <summary>
/// Serializes the standalone, multi-source PMT save request. The legacy
/// LaserPmtWorkflowSerializer remains the version-two compatibility reader.
/// </summary>
public static class PmtWorkflowRequestSerializer
{
    public const int FormatVersion = 3;

    public static string Serialize(
        PmtDraftSnapshot snapshot,
        string outputDirectory,
        string ownerToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);
        if (snapshot.PreviewWorkflow is not null)
            throw new InvalidOperationException("请先确认或取消 PMT 矩阵预览。");
        if (string.IsNullOrWhiteSpace(snapshot.OutputName))
            throw new InvalidOperationException("请设置 PMT 加工文件名。");

        var workflow = snapshot.Workflow;
        var compilation = LaserPmtWorkflowCompiler.Compile(workflow);
        var geometryErrors = LaserPmtWorkflowEditor.ValidateGeometry(workflow);
        if (!compilation.IsValid || geometryErrors.Count > 0)
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                compilation.Errors.Select(error => error.Message)
                    .Concat(geometryErrors.Select(error => error.Message))));

        var catalogById = snapshot.Sources.Sources.ToDictionary(source => source.Id, StringComparer.Ordinal);
        if (workflow.Sources.Any(source => !catalogById.ContainsKey(source.Id)))
            throw new InvalidOperationException("PMT 工作流引用了未导入的原始加工文件。");

        var root = new JsonObject
        {
            ["request_version"] = FormatVersion,
            ["output_dir"] = Path.GetFullPath(outputDirectory),
            ["output_name"] = snapshot.OutputName,
            ["owner_token"] = ownerToken,
            ["sources"] = new JsonArray(workflow.Sources.Select(source =>
            {
                var catalog = catalogById[source.Id];
                return (JsonNode)new JsonObject
                {
                    ["id"] = source.Id,
                    ["directory"] = catalog.Directory,
                    ["identity"] = source.Identity,
                    ["display_name"] = source.DisplayName,
                    ["mark"] = source.Mark,
                    ["color_argb"] = source.ColorArgb,
                    ["native_width"] = source.NativeWidth,
                    ["native_height"] = source.NativeHeight,
                    ["fingerprint"] = source.Fingerprint
                };
            }).ToArray()),
            ["workflow"] = WorkflowNode(workflow, compilation.Targets)
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject WorkflowNode(
        LaserPmtWorkflow workflow,
        IReadOnlyList<LaserPmtCompiledTarget> compiledTargets) => new()
    {
        ["format_version"] = FormatVersion,
        ["coordinate_system"] = new JsonObject { ["origin"] = "workpiece-top-left" },
        ["workpiece"] = BoundsNode(workflow.Workpiece),
        ["hatch_spacing"] = workflow.HatchSpacing,
        ["viewport"] = new JsonObject
        {
            ["zoom"] = workflow.Viewport.Zoom,
            ["pan_x"] = workflow.Viewport.PanX,
            ["pan_y"] = workflow.Viewport.PanY
        },
        ["numbering_state"] = new JsonObject
        {
            ["pmt_columns"] = workflow.PmtColumns,
            ["next_pmt_number"] = workflow.NextPmtNumber,
            ["next_creation_order"] = workflow.NextCreationOrder
        },
        ["base_nodes"] = new JsonArray(workflow.BaseNodes.Select(BaseNode).ToArray()),
        ["parameter_nodes"] = new JsonArray(workflow.ParameterNodes.Select(ParameterNode).ToArray()),
        ["targets"] = new JsonArray(workflow.Targets.Select(TargetNode).ToArray()),
        ["connections"] = new JsonArray(workflow.Connections.Select(ConnectionNode).ToArray()),
        ["compiled_targets"] = new JsonArray(compiledTargets.Select(CompiledTargetNode).ToArray()),
        ["generation"] = null
    };

    private static JsonNode BaseNode(LaserPmtBaseParameterNode node) => new JsonObject
    {
        ["id"] = node.Id,
        ["source_id"] = node.SourceId,
        ["position"] = PointNode(node.Position),
        ["parameters"] = StringDictionaryNode(node.Parameters),
        ["removed_parameters"] = new JsonArray(node.RemovedParameters
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (JsonNode?)JsonValue.Create(name)).ToArray())
    };

    private static JsonNode ParameterNode(LaserPmtSingleParameterNode node) => new JsonObject
    {
        ["id"] = node.Id,
        ["position"] = PointNode(node.Position),
        ["parameter_name"] = node.ParameterName,
        ["values_text"] = node.ValuesText,
        ["ports"] = new JsonArray(node.Ports.Select(port => (JsonNode)new JsonObject
        {
            ["id"] = port.Id,
            ["value"] = port.Value
        }).ToArray())
    };

    private static JsonNode TargetNode(LaserPmtWorkflowTarget target) => target switch
    {
        LaserPmtTarget pmt => new JsonObject
        {
            ["type"] = "pmt",
            ["id"] = pmt.Id,
            ["source_id"] = pmt.SourceId,
            ["number"] = pmt.Number,
            ["bounds"] = BoundsNode(pmt.Bounds),
            ["native_width"] = pmt.NativeWidth,
            ["native_height"] = pmt.NativeHeight,
            ["is_size_locked"] = pmt.IsSizeLocked,
            ["was_manually_moved"] = pmt.WasManuallyMoved,
            ["direct_parameter_overrides"] = StringDictionaryNode(pmt.DirectParameterOverrides)
        },
        LaserPmtTimestampTarget timestamp => new JsonObject
        {
            ["type"] = "timestamp",
            ["id"] = timestamp.Id,
            ["source_id"] = timestamp.SourceId,
            ["creation_order"] = timestamp.CreationOrder,
            ["text"] = timestamp.Text,
            ["bounds"] = BoundsNode(timestamp.Bounds),
            ["direct_parameter_overrides"] = StringDictionaryNode(timestamp.DirectParameterOverrides)
        },
        _ => throw new ArgumentException($"不支持的目标类型：{target.GetType().Name}")
    };

    private static JsonNode ConnectionNode(LaserPmtConnection connection) => new JsonObject
    {
        ["id"] = connection.Id,
        ["source_node_id"] = connection.SourceNodeId,
        ["source_port_id"] = connection.SourcePortId,
        ["target_id"] = connection.TargetId
    };

    private static JsonNode CompiledTargetNode(LaserPmtCompiledTarget target)
    {
        var node = new JsonObject
        {
            ["target_id"] = target.TargetId,
            ["kind"] = target.Kind == LaserPmtCompiledTargetKind.Pmt ? "pmt" : "timestamp",
            ["source_id"] = target.SourceId,
            ["identifier"] = target.Identifier,
            ["bounds"] = BoundsNode(target.Bounds),
            ["native_width"] = target.NativeWidth,
            ["native_height"] = target.NativeHeight,
            ["scale_x"] = target.ScaleX,
            ["scale_y"] = target.ScaleY,
            ["parameters"] = TypedDictionaryNode(target.Parameters)
        };
        if (target.Kind == LaserPmtCompiledTargetKind.Pmt)
            node["pmt_number"] = target.PmtNumber;
        else
        {
            node["creation_order"] = target.CreationOrder;
            node["timestamp_text"] = target.TimestampText;
        }
        return node;
    }

    private static JsonObject PointNode(LaserPmtWorkflowPoint point) => new()
    {
        ["x"] = point.X,
        ["y"] = point.Y
    };

    private static JsonObject BoundsNode(LaserPmtWorkflowBounds bounds) => new()
    {
        ["left"] = bounds.Left,
        ["top"] = bounds.Top,
        ["width"] = bounds.Width,
        ["height"] = bounds.Height
    };

    private static JsonObject StringDictionaryNode(IReadOnlyDictionary<string, string> values)
    {
        var node = new JsonObject();
        foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            node[pair.Key] = pair.Value;
        return node;
    }

    private static JsonObject TypedDictionaryNode(IReadOnlyDictionary<string, object> values)
    {
        var node = new JsonObject();
        foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            node[pair.Key] = pair.Value switch
            {
                bool boolean => JsonValue.Create(boolean),
                int integer => JsonValue.Create(integer),
                _ => throw new ArgumentException($"参数 {pair.Key} 类型无效。")
            };
        return node;
    }
}
