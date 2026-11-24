using System.Collections.Generic;
using UnityEngine;

namespace ReplayMod.Camera;

public enum CameraPreviewKind { Spline, LegacyTrail }

// Registry contains only objects created by this replay camera editor. It does
// not search for spheres or alter arbitrary scene/debug/gameplay objects.
public sealed class ReplayCameraPreview:MonoBehaviour
{
    private static readonly HashSet<ReplayCameraPreview> owned=new HashSet<ReplayCameraPreview>();
    private static readonly CameraPreviewVisibility<ReplayCameraPreview> visibility=new CameraPreviewVisibility<ReplayCameraPreview>();
    private CameraPreviewKind kind;
    private Renderer previewRenderer;
    private Material ownedMaterial;
    private bool ready;

    public static void Attach(GameObject obj,Renderer renderer,Color color,CameraPreviewKind owner)
    {
        if(obj==null||renderer==null)return;
        var preview=obj.AddComponent<ReplayCameraPreview>();preview.kind=owner;preview.previewRenderer=renderer;
        bool registered=visibility.Register(preview,true);
        if(registered)owned.Add(preview);
        // Unity primitives supply a built-in Standard material, which is not a
        // valid URP preview. Assign a supported native shader before rendering.
        try
        {
            var shader=registered?FindSupportedShader():null;
            if(shader!=null)
            {
                preview.ownedMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
                if(preview.ownedMaterial.HasProperty("_BaseColor"))preview.ownedMaterial.SetColor("_BaseColor",color);
                if(preview.ownedMaterial.HasProperty("_Color"))preview.ownedMaterial.SetColor("_Color",color);
                renderer.sharedMaterial=preview.ownedMaterial;preview.ready=true;
            }
        }
        catch(System.Exception){preview.ready=false;}
        finally{preview.ApplyVisibility();}
    }
    private static Shader FindSupportedShader()
    {
        // Lit is present in the inspected native aircraft/prefab resources.
        // A missing or unsupported shader hides this preview, never magenta.
        foreach(string name in new[]{"Universal Render Pipeline/Unlit","Universal Render Pipeline/Lit","Universal Render Pipeline/Simple Lit"})
        {var shader=Shader.Find(name);if(shader!=null&&shader.isSupported)return shader;}
        return null;
    }
    public static void SetVisible(GameObject obj,bool visible)
    {
        if(obj==null)return;
        var preview=obj.GetComponent<ReplayCameraPreview>();
        if(preview==null)return;
        visibility.SetRequested(preview,visible);preview.ApplyVisibility();
    }
    public static void SetCaptureHidden(bool hidden)
    {
        if(!visibility.SetCaptureHidden(hidden))return;
        // Only a user toggle allocates a snapshot; normal frame enforcement is
        // inert. Lifecycle callbacks may remove objects while visibility changes.
        var snapshot=new ReplayCameraPreview[owned.Count];owned.CopyTo(snapshot);
        foreach(var preview in snapshot)if(preview!=null)preview.ApplyVisibility();
    }
    public static void DestroyOwned(CameraPreviewKind owner)
    {
        var snapshot=new ReplayCameraPreview[owned.Count];owned.CopyTo(snapshot);
        foreach(var preview in snapshot)
        {
            if(preview==null||preview.kind!=owner)continue;
            // Deferred destruction cannot expose a stale object for one frame.
            preview.gameObject.SetActive(false);owned.Remove(preview);visibility.Forget(preview);
            Object.Destroy(preview.gameObject);
        }
    }
    private void ApplyVisibility()
    {
        bool show=ready&&visibility.IsVisible(this);
        if(previewRenderer!=null)previewRenderer.enabled=show;
        if(gameObject.activeSelf!=show)gameObject.SetActive(show);
    }
    private void OnDestroy()
    {
        owned.Remove(this);visibility.Forget(this);
        if(ownedMaterial!=null)Object.Destroy(ownedMaterial);
        ownedMaterial=null;
    }
}
