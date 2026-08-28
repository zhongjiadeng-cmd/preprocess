namespace GrayscaleLayersMac;

public enum SharedPreviewKind
{
    Texture,
    Dxf,
    Layer
}

public sealed class SharedPreviewSelection
{
    public SharedPreviewKind Current { get; private set; } = SharedPreviewKind.Texture;
    public bool HasTexture { get; private set; }
    public bool HasDxf { get; private set; }
    public bool HasLayers { get; private set; }

    public void BeginTextureImport()
    {
        HasTexture = false;
        Current = SharedPreviewKind.Texture;
    }

    public void CompleteTextureImport()
    {
        HasTexture = true;
        Current = SharedPreviewKind.Texture;
    }

    public void FailTextureImport()
    {
        HasTexture = false;
        Current = SharedPreviewKind.Texture;
    }

    public void CompleteDxfLoad()
    {
        HasDxf = true;
        Current = SharedPreviewKind.Dxf;
    }

    public void ClearDxf() => HasDxf = false;

    public void CompleteLayers() => HasLayers = true;

    public void ClearLayers() => HasLayers = false;

    public void Select(SharedPreviewKind kind) => Current = kind;
}
