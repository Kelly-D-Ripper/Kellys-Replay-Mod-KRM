// Only external Unity/game types are substituted. Event, codec, buffer, pool and
// writer implementations are linked directly from the production source.
namespace UnityEngine
{
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x; this.y=y; } }
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)
        {t=Math.Clamp(t,0,1);return new Vector3(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);}
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public static Quaternion Slerp(Quaternion a,Quaternion b,float t)
        {
            var q=System.Numerics.Quaternion.Slerp(new System.Numerics.Quaternion(a.x,a.y,a.z,a.w),
                new System.Numerics.Quaternion(b.x,b.y,b.z,b.w),Math.Clamp(t,0,1));
            return new Quaternion {x=q.X,y=q.Y,z=q.Z,w=q.W};
        }
    }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; } }
    public static class Mathf { public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max); }
}
public struct GlobalPosition { public float x, y, z; public GlobalPosition(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
public class ControlInputs { public float pitch, roll, yaw, throttle, brake, customAxis1; }
namespace Mirage.Serialization
{
    public static class QuaternionPacker
    { public static uint PackAsInt(UnityEngine.Quaternion q) => throw new NotSupportedException(); public static UnityEngine.Quaternion UnpackFromInt(uint q) => throw new NotSupportedException(); }
}
namespace ReplayMod
{
    public static class Plugin { public static TestLogger logger = new(); public static void DebugLog(string s) {} public static void SafeLog(string s) {} }
    public class TestLogger { public void LogWarning(object v) {} public void LogInfo(object v) {} public void LogError(object v) {} }
}
namespace ReplayMod.Core { public class UnitController { public void Execute(object e) {} public void SetGear(object e) {} public void DespawnUnit(uint id, bool b, double? eventTime=null) {} public void WeaponManagerFire(object e) {} } }
public class WeaponMount { public string jsonKey; }
public struct LiveryKey { public enum KeyType { Builtin } }
public static class Encyclopedia { public static TestWeaponLookup WeaponLookup = new(); }
public sealed class TestWeaponLookup : Dictionary<string, WeaponMount>
{
    public bool ResolveNamesOnly;
    public new WeaponMount this[string key]
    {
        get
        {
            if (TryGetValue(key, out var mount)) return mount;
            if (!ResolveNamesOnly) throw new KeyNotFoundException(key);
            // Read-only codec audits need weapon names, never game prefabs.
            return base[key] = new WeaponMount { jsonKey = key };
        }
        set => base[key] = value;
    }
}
