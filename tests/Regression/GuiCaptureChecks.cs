using System.Reflection;
using System.Runtime.CompilerServices;
using ReplayMod.Director;

public static class GuiCaptureChecks
{
    public static int Run()
    {
        Check(GuiCallbackFilter.IsSupported(Method(typeof(ManagedGui), "OnGUI"), typeof(FakeBehaviour)), "managed instance GUI eligible");
        Console.WriteLine("PASS clean capture filters managed MonoBehaviour GUI callbacks");
        Check(!GuiCallbackFilter.IsSupported(Method(typeof(ManagedGui), "Update"), typeof(FakeBehaviour)) &&
              !GuiCallbackFilter.IsSupported(Method(typeof(ManagedGui), "FixedUpdate"), typeof(FakeBehaviour)), "gameplay callbacks untouched");
        Console.WriteLine("PASS clean capture excludes update and physics callbacks");
        foreach (var type in new[] { typeof(StaticGui), typeof(ArgumentGui), typeof(ValueGui), typeof(GenericGui), typeof(AbstractGui), typeof(NativeGui), typeof(PlainGui) })
            Check(!GuiCallbackFilter.IsSupported(Method(type, "OnGUI"), typeof(FakeBehaviour)), "unsafe callback excluded: " + type.Name);
        Console.WriteLine("PASS clean capture excludes static arguments returns generics abstract native and nonbehaviours");
        var inherited = GuiCallbackFilter.FindCallbacks(typeof(InheritedGui), typeof(FakeBehaviour)).ToArray();
        Check(inherited.Length == 1 && inherited[0].DeclaringType == typeof(PrivateGuiBase), "private inherited GUI discovered");
        Check(inherited.SequenceEqual(GuiCallbackFilter.FindCallbacks(typeof(PrivateGuiBase), typeof(FakeBehaviour))), "shared base callback has one method identity for deduplication");
        Console.WriteLine("PASS clean capture discovers private inherited GUI callbacks for deduplication");
        Check(!GuiCallbackFilter.IsSupported(null, typeof(FakeBehaviour)) && !GuiCallbackFilter.IsSupported(Method(typeof(ManagedGui), "OnGUI"), null) &&
              !GuiCallbackFilter.FindCallbacks(typeof(PlainGui), typeof(FakeBehaviour)).Any() && !GuiCallbackFilter.FindCallbacks(null, typeof(FakeBehaviour)).Any(), "null and unrelated targets fail closed");
        Console.WriteLine("PASS clean capture reflection filter rejects missing and unrelated targets");
        return 5;
    }
    private static MethodInfo Method(Type type, string name) => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Single(m => m.Name == name);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private class FakeBehaviour { }
    private class ManagedGui : FakeBehaviour { private void OnGUI() { } private void Update() { } private void FixedUpdate() { } }
    private class StaticGui : FakeBehaviour { private static void OnGUI() { } }
    private class ArgumentGui : FakeBehaviour { private void OnGUI(int value) { } }
    private class ValueGui : FakeBehaviour { private int OnGUI() => 0; }
    private class GenericGui : FakeBehaviour { private void OnGUI<T>() { } }
    private abstract class AbstractGui : FakeBehaviour { public abstract void OnGUI(); }
    private class NativeGui : FakeBehaviour { [MethodImpl(MethodImplOptions.InternalCall)] private extern void OnGUI(); }
    private class PlainGui { private void OnGUI() { } }
    private class PrivateGuiBase : FakeBehaviour { private void OnGUI() { } }
    private sealed class InheritedGui : PrivateGuiBase { }
}
