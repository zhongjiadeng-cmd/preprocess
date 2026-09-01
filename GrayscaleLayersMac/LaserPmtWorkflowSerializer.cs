using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrayscaleLayersMac;

public static class LaserPmtWorkflowSerializer
{
    public const int CurrentFormatVersion = 2;
    public const int MaximumDocumentBytes = 16 * 1024 * 1024;

    public static string Serialize(LaserPmtWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var compilation = LaserPmtWorkflowCompiler.Compile(workflow);
        if (!compilation.IsValid)
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                compilation.Errors.Select(error => error.Message)));

        var root = new JsonObject
        {
            ["format_version"] = CurrentFormatVersion,
            ["coordinate_system"] = new JsonObject { ["origin"] = "workpiece-top-left" },
            ["base_machine_identity"] = workflow.BaseMachineIdentity,
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
                ["next_creation_order"] = workflow.NextCreationOrder,
                ["prefix"] = workflow.Numbering.Prefix,
                ["increment"] = workflow.Numbering.Increment,
                ["padding"] = workflow.Numbering.Padding
            },
            ["base_node"] = BaseNode(workflow.BaseNode),
            ["parameter_nodes"] = new JsonArray(workflow.ParameterNodes.Select(ParameterNode).ToArray()),
            ["targets"] = new JsonArray(workflow.Targets.Select(TargetNode).ToArray()),
            ["connections"] = new JsonArray(workflow.Connections.Select(ConnectionNode).ToArray()),
            ["compiled_targets"] = new JsonArray(compilation.Targets.Select(CompiledTargetNode).ToArray())
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static LaserPmtWorkflow Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumDocumentBytes)
            throw new InvalidDataException("PMT 工作流不存在、为空或过大。");
        return Parse(File.ReadAllText(path));
    }

    public static LaserPmtWorkflow Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            CheckUniqueProperties(document.RootElement, "根节点");
            var root = document.RootElement;
            RequireProperties(root, "根节点",
                "format_version", "coordinate_system", "base_machine_identity", "workpiece",
                "hatch_spacing", "viewport", "numbering_state", "base_node",
                "parameter_nodes", "targets", "connections", "compiled_targets");
            if (ReadInt(root, "format_version") != CurrentFormatVersion)
                throw new InvalidDataException("不支持的 PMT 工作流版本。");
            var coordinate = ReadObject(root, "coordinate_system");
            RequireProperties(coordinate, "coordinate_system", "origin");
            if (ReadString(coordinate, "origin") != "workpiece-top-left")
                throw new InvalidDataException("不支持的 PMT 坐标原点。");

            var viewport = ReadObject(root, "viewport");
            RequireProperties(viewport, "viewport", "zoom", "pan_x", "pan_y");
            var numbering = ReadObject(root, "numbering_state");
            RequireProperties(numbering, "numbering_state",
                "pmt_columns", "next_pmt_number", "next_creation_order",
                "prefix", "increment", "padding");

            var workflow = new LaserPmtWorkflow(
                ReadString(root, "base_machine_identity"),
                ReadBounds(ReadObject(root, "workpiece"), "workpiece"),
                ReadFiniteDouble(root, "hatch_spacing"),
                new LaserPmtCanvasViewport(
                    ReadFiniteDouble(viewport, "zoom"),
                    ReadFiniteDouble(viewport, "pan_x"),
                    ReadFiniteDouble(viewport, "pan_y")),
                ReadBaseNode(ReadObject(root, "base_node")),
                ReadParameterNodes(ReadArray(root, "parameter_nodes")),
                ReadTargets(ReadArray(root, "targets")),
                ReadConnections(ReadArray(root, "connections")),
                ReadInt(numbering, "pmt_columns"),
                ReadInt(numbering, "next_pmt_number"),
                ReadLong(numbering, "next_creation_order"),
                new LaserPmtWorkflowNumbering(
                    ReadString(numbering, "prefix"),
                    ReadInt(numbering, "increment"),
                    ReadInt(numbering, "padding")));

            var expected = LaserPmtWorkflowCompiler.Compile(workflow);
            if (!expected.IsValid)
                throw new InvalidDataException("保存的 PMT 工作流无法重新编译。");
            var saved = ReadCompiledTargets(ReadArray(root, "compiled_targets"));
            if (!CompiledTargetsEqual(expected.Targets, saved))
                throw new InvalidDataException("保存的 PMT 编译结果与源工作流不一致。");
            return workflow;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("PMT 工作流格式无效。", exception);
        }
    }

    private static JsonObject BaseNode(LaserPmtBaseParameterNode node) => new()
    {
        ["id"] = node.Id,
        ["position"] = PointNode(node.Position),
        ["parameters"] = StringDictionaryNode(node.Parameters),
        ["removed_parameters"] = new JsonArray(node.RemovedParameters
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (JsonNode?)JsonValue.Create(name)).ToArray())
    };

    private static JsonObject ParameterNode(LaserPmtSingleParameterNode node) => new()
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

    private static JsonObject TargetNode(LaserPmtWorkflowTarget target) => target switch
    {
        LaserPmtTarget pmt => new JsonObject
        {
            ["type"] = "pmt",
            ["id"] = pmt.Id,
            ["number"] = pmt.Number,
            ["bounds"] = BoundsNode(pmt.Bounds),
            ["was_manually_moved"] = pmt.WasManuallyMoved
        },
        LaserPmtTimestampTarget timestamp => new JsonObject
        {
            ["type"] = "timestamp",
            ["id"] = timestamp.Id,
            ["creation_order"] = timestamp.CreationOrder,
            ["text"] = timestamp.Text,
            ["bounds"] = BoundsNode(timestamp.Bounds)
        },
        _ => throw new ArgumentException($"不支持的目标类型：{target.GetType().Name}")
    };

    private static JsonObject ConnectionNode(LaserPmtConnection connection) => new()
    {
        ["id"] = connection.Id,
        ["source_node_id"] = connection.SourceNodeId,
        ["source_port_id"] = connection.SourcePortId,
        ["target_id"] = connection.TargetId
    };

    private static JsonObject CompiledTargetNode(LaserPmtCompiledTarget target)
    {
        var node = new JsonObject
        {
            ["target_id"] = target.TargetId,
            ["kind"] = target.Kind == LaserPmtCompiledTargetKind.Pmt ? "pmt" : "timestamp",
            ["identifier"] = target.Identifier,
            ["bounds"] = BoundsNode(target.Bounds),
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
        foreach (var definition in LaserPmtConfiguration.Parameters)
            if (values.TryGetValue(definition.Name, out var value))
                node[definition.Name] = value;
        return node;
    }

    private static JsonObject TypedDictionaryNode(IReadOnlyDictionary<string, object> values)
    {
        var node = new JsonObject();
        foreach (var definition in LaserPmtConfiguration.Parameters)
        {
            if (!values.TryGetValue(definition.Name, out var value))
                continue;
            node[definition.Name] = value switch
            {
                bool boolean => JsonValue.Create(boolean),
                int integer => JsonValue.Create(integer),
                _ => throw new ArgumentException($"参数 {definition.Name} 类型无效。")
            };
        }
        return node;
    }

    private static LaserPmtBaseParameterNode ReadBaseNode(JsonElement element)
    {
        RequireProperties(element, "base_node", "id", "position", "parameters", "removed_parameters");
        var position = ReadPoint(ReadObject(element, "position"), "base_node.position");
        var parameters = ReadStringDictionary(ReadObject(element, "parameters"), "base_node.parameters");
        var removed = ReadArray(element, "removed_parameters")
            .EnumerateArray()
            .Select((value, index) => ReadArrayString(value, $"removed_parameters[{index}]"))
            .ToHashSet(StringComparer.Ordinal);
        return new LaserPmtBaseParameterNode(ReadString(element, "id"), position, parameters, removed);
    }

    private static IReadOnlyList<LaserPmtSingleParameterNode> ReadParameterNodes(JsonElement array)
    {
        var result = new List<LaserPmtSingleParameterNode>();
        foreach (var element in array.EnumerateArray())
        {
            RequireProperties(element, "parameter_node",
                "id", "position", "parameter_name", "values_text", "ports");
            var ports = new List<LaserPmtParameterPort>();
            foreach (var port in ReadArray(element, "ports").EnumerateArray())
            {
                RequireProperties(port, "parameter_port", "id", "value");
                ports.Add(new LaserPmtParameterPort(ReadString(port, "id"), ReadString(port, "value")));
            }
            result.Add(new LaserPmtSingleParameterNode(
                ReadString(element, "id"),
                ReadPoint(ReadObject(element, "position"), "parameter_node.position"),
                ReadString(element, "parameter_name"),
                ReadString(element, "values_text"),
                ports));
        }
        return result;
    }

    private static IReadOnlyList<LaserPmtWorkflowTarget> ReadTargets(JsonElement array)
    {
        var result = new List<LaserPmtWorkflowTarget>();
        foreach (var element in array.EnumerateArray())
        {
            var type = ReadString(element, "type");
            if (type == "pmt")
            {
                RequireProperties(element, "pmt_target",
                    "type", "id", "number", "bounds", "was_manually_moved");
                result.Add(new LaserPmtTarget(
                    ReadString(element, "id"),
                    ReadInt(element, "number"),
                    ReadBounds(ReadObject(element, "bounds"), "pmt_target.bounds"),
                    ReadBoolean(element, "was_manually_moved")));
            }
            else if (type == "timestamp")
            {
                RequireProperties(element, "timestamp_target",
                    "type", "id", "creation_order", "text", "bounds");
                result.Add(new LaserPmtTimestampTarget(
                    ReadString(element, "id"),
                    ReadLong(element, "creation_order"),
                    ReadString(element, "text"),
                    ReadBounds(ReadObject(element, "bounds"), "timestamp_target.bounds")));
            }
            else
                throw new InvalidDataException($"不支持的目标类型：{type}");
        }
        return result;
    }

    private static IReadOnlyList<LaserPmtConnection> ReadConnections(JsonElement array)
    {
        var result = new List<LaserPmtConnection>();
        foreach (var element in array.EnumerateArray())
        {
            RequireProperties(element, "connection",
                "id", "source_node_id", "source_port_id", "target_id");
            result.Add(new LaserPmtConnection(
                ReadString(element, "id"),
                ReadString(element, "source_node_id"),
                ReadString(element, "source_port_id"),
                ReadString(element, "target_id")));
        }
        return result;
    }

    private static IReadOnlyList<LaserPmtCompiledTarget> ReadCompiledTargets(JsonElement array)
    {
        var result = new List<LaserPmtCompiledTarget>();
        foreach (var element in array.EnumerateArray())
        {
            var kind = ReadString(element, "kind");
            if (kind == "pmt")
                RequireProperties(element, "compiled_pmt",
                    "target_id", "kind", "identifier", "pmt_number", "bounds", "parameters");
            else if (kind == "timestamp")
                RequireProperties(element, "compiled_timestamp",
                    "target_id", "kind", "identifier", "creation_order", "timestamp_text", "bounds", "parameters");
            else
                throw new InvalidDataException($"不支持的编译目标类型：{kind}");
            result.Add(new LaserPmtCompiledTarget(
                ReadString(element, "target_id"),
                kind == "pmt" ? LaserPmtCompiledTargetKind.Pmt : LaserPmtCompiledTargetKind.Timestamp,
                ReadString(element, "identifier"),
                kind == "pmt" ? ReadInt(element, "pmt_number") : null,
                kind == "timestamp" ? ReadLong(element, "creation_order") : null,
                kind == "timestamp" ? ReadString(element, "timestamp_text") : null,
                ReadBounds(ReadObject(element, "bounds"), "compiled_target.bounds"),
                ReadTypedParameters(ReadObject(element, "parameters"))));
        }
        return result;
    }

    private static bool CompiledTargetsEqual(
        IReadOnlyList<LaserPmtCompiledTarget> expected,
        IReadOnlyList<LaserPmtCompiledTarget> saved)
    {
        if (expected.Count != saved.Count)
            return false;
        for (var index = 0; index < expected.Count; index++)
        {
            var left = expected[index];
            var right = saved[index];
            if (left.TargetId != right.TargetId || left.Kind != right.Kind || left.Identifier != right.Identifier ||
                left.PmtNumber != right.PmtNumber || left.CreationOrder != right.CreationOrder ||
                left.TimestampText != right.TimestampText || left.Bounds != right.Bounds ||
                left.Parameters.Count != right.Parameters.Count)
                return false;
            foreach (var pair in left.Parameters)
                if (!right.Parameters.TryGetValue(pair.Key, out var value) || !Equals(pair.Value, value))
                    return false;
        }
        return true;
    }

    private static IReadOnlyDictionary<string, string> ReadStringDictionary(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{label} 必须是对象。");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                throw new InvalidDataException($"{label}.{property.Name} 必须是非空字符串。");
            values.Add(property.Name, property.Value.GetString()!);
        }
        return values;
    }

    private static IReadOnlyDictionary<string, object> ReadTypedParameters(JsonElement element)
    {
        RequireProperties(element, "compiled parameters",
            LaserPmtConfiguration.Parameters.Select(item => item.Name).ToArray());
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var definition in LaserPmtConfiguration.Parameters)
        {
            var value = element.GetProperty(definition.Name);
            if (definition.IsBoolean)
            {
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException($"参数 {definition.Name} 必须是布尔值。");
                values.Add(definition.Name, value.GetBoolean());
            }
            else
            {
                if (!value.TryGetInt32(out var integer))
                    throw new InvalidDataException($"参数 {definition.Name} 必须是整数。");
                values.Add(definition.Name, integer);
            }
        }
        return values;
    }

    private static LaserPmtWorkflowPoint ReadPoint(JsonElement element, string label)
    {
        RequireProperties(element, label, "x", "y");
        return new LaserPmtWorkflowPoint(ReadFiniteDouble(element, "x"), ReadFiniteDouble(element, "y"));
    }

    private static LaserPmtWorkflowBounds ReadBounds(JsonElement element, string label)
    {
        RequireProperties(element, label, "left", "top", "width", "height");
        return new LaserPmtWorkflowBounds(
            ReadFiniteDouble(element, "left"),
            ReadFiniteDouble(element, "top"),
            ReadFiniteDouble(element, "width"),
            ReadFiniteDouble(element, "height"));
    }

    private static JsonElement ReadObject(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{name} 必须是对象。");
        return value;
    }

    private static JsonElement ReadArray(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{name} 必须是数组。");
        return value;
    }

    private static string ReadString(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"{name} 必须是非空字符串。");
        return value.GetString()!;
    }

    private static string ReadArrayString(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"{label} 必须是非空字符串。");
        return value.GetString()!;
    }

    private static int ReadInt(JsonElement owner, string name)
    {
        if (!owner.GetProperty(name).TryGetInt32(out var value))
            throw new InvalidDataException($"{name} 必须是整数。");
        return value;
    }

    private static long ReadLong(JsonElement owner, string name)
    {
        if (!owner.GetProperty(name).TryGetInt64(out var value))
            throw new InvalidDataException($"{name} 必须是整数。");
        return value;
    }

    private static double ReadFiniteDouble(JsonElement owner, string name)
    {
        if (!owner.GetProperty(name).TryGetDouble(out var value) || !double.IsFinite(value))
            throw new InvalidDataException($"{name} 必须是有限数值。");
        return value;
    }

    private static bool ReadBoolean(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"{name} 必须是布尔值。");
        return value.GetBoolean();
    }

    private static void RequireProperties(JsonElement element, string label, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{label} 必须是对象。");
        var actual = element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
            throw new InvalidDataException($"{label} 字段不完整或包含未知字段。");
    }

    private static void CheckUniqueProperties(JsonElement element, string label)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"{label} 包含重复字段：{property.Name}");
                CheckUniqueProperties(property.Value, $"{label}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                CheckUniqueProperties(item, $"{label}[{index++}]");
        }
    }
}
