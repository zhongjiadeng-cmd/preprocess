namespace GrayscaleLayersMac;

public sealed class DxfOverlayState
{
    private double _textureOpacity = 0.55;

    public bool TextureAvailable { get; private set; }
    public bool ShowTexture { get; set; } = true;
    public bool ShowLines { get; set; } = true;
    public bool ShowDirectionArrows { get; set; } = true;
    public bool IsTopView { get; set; }

    public DxfOverlayState(bool startInTopView = false)
    {
        IsTopView = startInTopView;
    }

    public double TextureOpacity
    {
        get => _textureOpacity;
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _textureOpacity = Math.Clamp(value, 0, 1);
        }
    }

    public bool ShouldDrawTexture => TextureAvailable && ShowTexture;
    public bool ShouldDrawDirectionArrows => ShowLines && ShowDirectionArrows;

    public void SetTextureAvailable(bool available) => TextureAvailable = available;
}
