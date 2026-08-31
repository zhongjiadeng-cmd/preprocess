namespace GrayscaleLayersMac;

/// <summary>
/// 预览区的三个视图。灰度分层不再是独立的视图——它作为纹理界面里的第 1..N 层存在。
/// </summary>
public enum SharedPreviewKind
{
    Texture,
    Dxf,
    Pmt
}

public sealed class SharedPreviewSelection
{
    public SharedPreviewKind Current { get; private set; } = SharedPreviewKind.Texture;
    public bool HasTexture { get; private set; }
    public bool HasDxf { get; private set; }
    public bool HasPmt { get; private set; }

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

    public void CompletePmtLoad()
    {
        HasPmt = true;
        Current = SharedPreviewKind.Pmt;
    }

    public void ClearPmt() => HasPmt = false;

    public void Select(SharedPreviewKind kind) => Current = kind;
}
