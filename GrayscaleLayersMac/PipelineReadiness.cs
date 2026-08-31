namespace GrayscaleLayersMac;

internal static class PipelineReadiness
{
    public static string Describe(
        bool isRunning,
        string? inputPath,
        string? layerOutputPath,
        string? dxfOutputPath)
    {
        if (isRunning)
            return "正在执行流程；可以继续查看预览与日志。";

        var missing = new List<string>(3);
        if (string.IsNullOrWhiteSpace(inputPath))
            missing.Add("原始灰度图");
        if (string.IsNullOrWhiteSpace(layerOutputPath))
            missing.Add("分层 TIFF 目录");
        if (string.IsNullOrWhiteSpace(dxfOutputPath))
            missing.Add("DXF 目录");

        return missing.Count == 0
            ? "已准备：可以执行全部四步流程。"
            : $"尚需设置：{string.Join("、", missing)}。";
    }
}
