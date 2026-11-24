using System;
using ReplayMod.Director;
using UnityEngine;

namespace ReplayMod.Core;

// A single binding owns this texture. No imported assets, shared material or
// native light is changed. The runtime assigns the result only to its own spot.
public sealed class ReplayStudioLightCookie : IDisposable
{
    public const int Size = 128;
    private Texture2D texture;
    private byte[] pixels;
    private int profile;
    private float width, height, softness, nextUpload;
    private bool applied;

    public Texture CookieFor(StudioLightSettings settings, bool supported)
    {
        if (!supported || !ReplayStudioLightCapabilities.CookiesAvailable || settings == null
            || settings.type != 1 || settings.beamProfile == 0
            || !StudioLightBeamProfile.Valid(settings.beamProfile, settings.beamWidth,
                settings.beamHeight, settings.beamSoftness)) return null;
        bool changed = !applied || profile != settings.beamProfile || width != settings.beamWidth
            || height != settings.beamHeight || softness != settings.beamSoftness;
        if (!changed && texture != null) return texture;
        float now = Time.unscaledTime;
        // Coalesce a dragged slider while still applying its final value when
        // the replay is paused. The previous texture remains valid meanwhile.
        if (texture != null && applied && now < nextUpload) return texture;
        if (pixels == null) pixels = new byte[Size * Size * 4];
        if (!StudioLightBeamProfile.Rasterize(settings.beamProfile, settings.beamWidth,
            settings.beamHeight, settings.beamSoftness, Size, pixels)) return null;
        if (texture == null)
        {
            texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true)
            {
                name = "Replay studio spot mask",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };
        }
        texture.LoadRawTextureData(pixels);
        texture.Apply(false, false);
        profile = settings.beamProfile;
        width = settings.beamWidth;
        height = settings.beamHeight;
        softness = settings.beamSoftness;
        nextUpload = now + .1f;
        applied = true;
        return texture;
    }

    public void Clear()
    {
        if (texture != null) UnityEngine.Object.Destroy(texture);
        texture = null;
        pixels = null;
        applied = false;
        nextUpload = 0;
    }

    public void Dispose() => Clear();
}
