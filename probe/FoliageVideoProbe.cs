using System;
using System.Collections;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;
using UnityEngine.Rendering;

// These video captures are test evidence only; none of this is in the mod DLL.
[HarmonyPatch(typeof(ReplayMod.Patches.ReplayDetailRenderOrder), "RunNativeNow")]
internal static class FoliageNativeTimingProbe
{
    internal static int calls;
    [HarmonyPrepare] private static bool Prepare() => Environment.GetEnvironmentVariable("REPLAY_FOLIAGE_NATIVE") == "1";
    [HarmonyPrefix] private static bool Prefix(ref bool __result) { calls++; __result = true; return false; }
}

internal sealed class FoliageFrameCapture : MonoBehaviour
{
    internal string directory;
    internal bool active;
    internal int frames;
    internal int width=960;
    internal string error;
    RenderTexture full, small;
    Texture2D image;

    internal void Begin()
    {
        full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
        int height = Mathf.Max(2, Mathf.RoundToInt((float)width * Screen.height / Screen.width / 2f) * 2);
        small = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        image = new Texture2D(width, height, TextureFormat.RGB24, false);
        active = true;
        StartCoroutine(Capture());
    }

    IEnumerator Capture()
    {
        while (active)
        {
            yield return new WaitForEndOfFrame();
            if (!active) break;
            try
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                // D3D screen capture into a render texture has the opposite
                // vertical orientation to Texture2D's encoded output.
                Graphics.Blit(full, small, new Vector2(1,-1), new Vector2(0,1));
                var previous = RenderTexture.active;
                try { RenderTexture.active = small; image.ReadPixels(new Rect(0, 0, small.width, small.height), 0, 0); image.Apply(); }
                finally { RenderTexture.active = previous; }
                File.WriteAllBytes(Path.Combine(directory, "frame-" + frames.ToString("D4") + ".jpg"), image.EncodeToJPG(85));
                if(frames==20&&Environment.GetEnvironmentVariable("REPLAY_FOLIAGE_ORIENT_CHECK")=="1")
                    ScreenCapture.CaptureScreenshot(Path.Combine(directory,"orientation-native.png"));
                File.AppendAllText(Path.Combine(directory, "times.csv"), frames + "," + Time.realtimeSinceStartup.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + ReplayManager.i.Playback.currentVirtualTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\n");
                frames++;
            }
            catch (Exception ex) { error = ex.ToString(); active = false; }
        }
    }

    void OnDestroy()
    {
        active = false;
        if (full != null) { full.Release(); Destroy(full); }
        if (small != null) { small.Release(); Destroy(small); }
        if (image != null) Destroy(image);
    }
}

internal static class FoliageVideoProbe
{
    public static async UniTask Run(Action<string> log)
    {
        string directory = Environment.GetEnvironmentVariable("REPLAY_FOLIAGE_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || Directory.GetFiles(directory).Length != 0)
            throw new Exception("An empty isolated foliage output directory is required");
        bool native = Environment.GetEnvironmentVariable("REPLAY_FOLIAGE_NATIVE") == "1";
        var player = ReplayManager.i.Playback; player.Pause(); await player.TimelineJump(2, 0, false);
        var d = ReplayDirector.i; d.EnterFreeCamera(); var camera = SceneSingleton<CameraStateManager>.i;
        if (camera == null || camera.mainCamera == null) throw new Exception("Replay camera unavailable");
        var origin = camera.transform.position.ToGlobalPosition(); Quaternion q = Quaternion.Euler(25, 0, 0);
        if (Environment.GetEnvironmentVariable("REPLAY_FOLIAGE_HORIZON") == "1")
        {
            var rayStart=new GlobalPosition(origin.x,10000,origin.z).ToLocalPosition();
            var hits=Physics.RaycastAll(rayStart,Vector3.down,20000).Where(hit=>hit.collider!=null&&hit.collider.name.IndexOf("terrain",StringComparison.OrdinalIgnoreCase)>=0).OrderBy(hit=>hit.distance).ToArray();
            if(hits.Length==0)throw new Exception("No terrain beneath camera path origin");
            origin=(hits[0].point+Vector3.up*180).ToGlobalPosition();q=Quaternion.Euler(7,0,0);
            log("FOLIAGE VIDEO low horizontal path origin="+origin+" terrain="+hits[0].collider.name);
        }
        CameraKey Point(double t, float x, float z) => new CameraKey { time=t, x=origin.x+x, y=origin.y, z=origin.z+z, qx=q.x, qy=q.y, qz=q.z, qw=q.w, fov=20, easing=1 };
        d.Project.cameras.Clear(); d.Project.cameras.Add(Point(2,0,0)); d.Project.cameras.Add(Point(4,500,100)); d.Project.cameras.Add(Point(6,500,600));
        d.Project.animatePathFov=true; d.Project.smoothCameraPath=true;
        if (!d.PlayCameraPath()) throw new Exception("Path did not start: " + d.Status);
        int oldFps=Application.targetFrameRate, oldVsync=QualitySettings.vSyncCount;
        var capture = ReplayManager.i.gameObject.AddComponent<FoliageFrameCapture>(); capture.directory=directory;
        if(int.TryParse(Environment.GetEnvironmentVariable("REPLAY_FOLIAGE_WIDTH"),out int width))capture.width=Mathf.Clamp(width,320,1920);
        FoliageOrderObserver.active=true;
        RenderPipelineManager.beginCameraRendering+=FoliageOrderObserver.BeforeRender;
        try
        {
            Application.targetFrameRate=30; QualitySettings.vSyncCount=0; d.SetPlaybackSpeed(1); d.SetClean(true);
            capture.Begin(); player.Resume(); float deadline=Time.realtimeSinceStartup+35;
            while (player.currentVirtualTime < 5.8 && capture.error == null)
            { if (Time.realtimeSinceStartup > deadline) throw new Exception("Path playback stalled"); await UniTask.NextFrame(); }
            player.Pause(); capture.active=false;
            if (capture.error != null) throw new Exception("Capture failed: " + capture.error);
            if (capture.frames < 30) throw new Exception("Too few captured frames: " + capture.frames);
            if (native && FoliageNativeTimingProbe.calls < 30) throw new Exception("Native timing override did not run enough times");
            if (!native && (FoliageOrderObserver.matches < 30 || FoliageOrderObserver.maxPosition > .01 || FoliageOrderObserver.maxAngle > .05 || FoliageOrderObserver.maxFov > .01))
                throw new Exception("Replay foliage camera did not match the rendered camera");
            log("FOLIAGE VIDEO " + (native ? "native timing" : "replay timing") + " frames=" + capture.frames + " nativeCalls=" + FoliageNativeTimingProbe.calls + " matchedRenders=" + FoliageOrderObserver.matches + " maxPosition=" + FoliageOrderObserver.maxPosition + " maxAngle=" + FoliageOrderObserver.maxAngle + " maxFov=" + FoliageOrderObserver.maxFov + " output=" + directory);
        }
        finally { FoliageOrderObserver.active=false; RenderPipelineManager.beginCameraRendering-=FoliageOrderObserver.BeforeRender; capture.active=false; UnityEngine.Object.Destroy(capture); Application.targetFrameRate=oldFps; QualitySettings.vSyncCount=oldVsync; d.SetClean(false); d.StopCameraPathPreview(); }
    }
}
