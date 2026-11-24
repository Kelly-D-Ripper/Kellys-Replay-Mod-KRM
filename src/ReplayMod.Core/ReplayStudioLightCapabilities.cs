using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReplayMod.Core;

// Read the current renderer without depending on or changing its URP assembly.
// Native graphics settings can change the per-object light budget at runtime.
public static class ReplayStudioLightCapabilities
{
    private const string PipelineType = "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private static Type boundType;
    private static PropertyInfo maxLights, renderingMode, shadows, softShadows, cookies;
    private static float nextRefresh;
    private static bool initialized;
    private static bool dynamicAvailable, shadowsAvailable, softShadowsAvailable, cookiesAvailable;
    private static int lightsPerObject;
    private static string summary = "Custom lights are waiting for the game renderer.";

    public static bool DynamicLightsAvailable { get { Refresh(); return dynamicAvailable; } }
    public static bool ShadowsAvailable { get { Refresh(); return shadowsAvailable; } }
    public static bool SoftShadowsAvailable { get { Refresh(); return softShadowsAvailable; } }
    public static bool CookiesAvailable { get { Refresh(); return cookiesAvailable; } }
    public static int LightsPerObject { get { Refresh(); return lightsPerObject; } }
    public static string Summary { get { Refresh(); return summary; } }

    public static void Refresh()
    {
        float now = Time.unscaledTime;
        if (initialized && now < nextRefresh) return;
        initialized = true;
        nextRefresh = now + 0.5f;
        dynamicAvailable = shadowsAvailable = softShadowsAvailable = cookiesAvailable = false;
        lightsPerObject = 0;
        try
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                summary = "Custom lights unavailable: the game renderer is not ready.";
                return;
            }
            Type type = pipeline.GetType();
            if (boundType != type) Bind(type);
            if (maxLights == null || renderingMode == null)
            {
                summary = "Custom lights unavailable: this renderer is unsupported.";
                return;
            }
            int mode = Convert.ToInt32(renderingMode.GetValue(pipeline, null));
            int nativeCap = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 ? 4 : 8;
            lightsPerObject = Math.Max(0, Math.Min(nativeCap, Convert.ToInt32(maxLights.GetValue(pipeline, null))));
            dynamicAvailable = (mode == 1 || mode == 2) && lightsPerObject > 0;
            shadowsAvailable = dynamicAvailable && mode == 1 && ReadBool(shadows, pipeline) && SystemInfo.supportsShadows;
            softShadowsAvailable = shadowsAvailable && ReadBool(softShadows, pipeline);
            cookiesAvailable = dynamicAvailable && mode == 1 && ReadBool(cookies, pipeline);
            if (!dynamicAvailable)
            {
                summary = "Game dynamic lights are Off. Enable them in the game graphics settings.";
                return;
            }
            summary = "Up to " + lightsPerObject + " lights per object, shared with game lights. "
                + (shadowsAvailable ? "Local shadows supported." : "Local shadows unavailable in this renderer.");
            if (mode == 2) summary += " Per-vertex lighting reduces beam detail.";
        }
        catch
        {
            // A changed/incompatible renderer must not break replay playback.
            dynamicAvailable = shadowsAvailable = softShadowsAvailable = cookiesAvailable = false;
            lightsPerObject = 0;
            summary = "Custom-light capabilities could not be read from this renderer.";
        }
    }

    private static void Bind(Type type)
    {
        boundType = type;
        maxLights = renderingMode = shadows = softShadows = cookies = null;
        bool supported = false;
        for (Type candidate = type; candidate != null; candidate = candidate.BaseType)
            if (candidate.FullName == PipelineType) { supported = true; break; }
        if (!supported) return;
        maxLights = type.GetProperty("maxAdditionalLightsCount", PublicInstance);
        renderingMode = type.GetProperty("additionalLightsRenderingMode", PublicInstance);
        shadows = type.GetProperty("supportsAdditionalLightShadows", PublicInstance);
        softShadows = type.GetProperty("supportsSoftShadows", PublicInstance);
        cookies = type.GetProperty("supportsLightCookies", PublicInstance);
    }

    private static bool ReadBool(PropertyInfo property, object owner)
    {
        return property != null && property.GetValue(owner, null) is bool value && value;
    }
}
