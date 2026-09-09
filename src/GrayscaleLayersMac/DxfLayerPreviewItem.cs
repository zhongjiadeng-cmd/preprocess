namespace GrayscaleLayersMac;

public sealed record DxfLayerPreviewItem
{
    public string Name { get; }
    public string DxfPath { get; }
    public string? TexturePath { get; }
    public DxfTextureRegistration? TextureRegistration { get; }
    internal DxfPreviewControl.PreparedDxfPreview? PreparedPreview { get; }
    public double WidthMm => TextureRegistration?.FrameWidthMm ?? 0;
    public double HeightMm => TextureRegistration?.FrameHeightMm ?? 0;
    public bool HasTexture => TexturePath is not null;

    public DxfLayerPreviewItem(
        string name, string dxfPath, string? texturePath,
        double widthMm, double heightMm)
        : this(
            name,
            dxfPath,
            texturePath,
            texturePath is null
                ? null
                : new DxfTextureRegistration(
                    widthMm, heightMm, widthMm, heightMm, 1, 1))
    {
    }

    public DxfLayerPreviewItem(
        string name,
        string dxfPath,
        string? texturePath,
        DxfTextureRegistration? textureRegistration)
    {
        if ((texturePath is null) != (textureRegistration is null))
            throw new ArgumentException(
                "Texture path and physical registration must be supplied together.");
        (Name, DxfPath, TexturePath, TextureRegistration) =
            (name, dxfPath, texturePath, textureRegistration);
    }

    internal DxfLayerPreviewItem(
        string name,
        DxfPreviewControl.PreparedDxfPreview preparedPreview)
        : this(name, preparedPreview.Path, null, null)
    {
        PreparedPreview = preparedPreview;
    }

    public static DxfLayerPreviewItem Imported(string path) =>
        new($"导入 · {Path.GetFileName(path)}", path, null, null);

    public override string ToString() => Name;
}
