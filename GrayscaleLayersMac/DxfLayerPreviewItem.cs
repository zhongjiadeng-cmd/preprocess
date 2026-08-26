namespace GrayscaleLayersMac;

public sealed record DxfLayerPreviewItem
{
    public string Name { get; }
    public string DxfPath { get; }
    public string? TexturePath { get; }
    public double WidthMm { get; }
    public double HeightMm { get; }
    public bool HasTexture => TexturePath is not null;

    public DxfLayerPreviewItem(
        string name, string dxfPath, string? texturePath,
        double widthMm, double heightMm)
    {
        if (texturePath is not null &&
            (!double.IsFinite(widthMm) || widthMm <= 0 ||
             !double.IsFinite(heightMm) || heightMm <= 0))
            throw new ArgumentOutOfRangeException(nameof(widthMm));
        (Name, DxfPath, TexturePath, WidthMm, HeightMm) =
            (name, dxfPath, texturePath, widthMm, heightMm);
    }

    public static DxfLayerPreviewItem Imported(string path) =>
        new($"导入 · {Path.GetFileName(path)}", path, null, 0, 0);

    public override string ToString() => Name;
}
