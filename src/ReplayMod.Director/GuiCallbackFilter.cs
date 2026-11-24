using System;
using System.Collections.Generic;
using System.Reflection;

namespace ReplayMod.Director;

// Pure reflection policy: only Unity's managed, parameterless GUI message is eligible.
public static class GuiCallbackFilter
{
    public static bool IsSupported(MethodInfo method, Type behaviourBase)
    {
        if (method == null || behaviourBase == null || method.Name != "OnGUI" || method.IsStatic || method.IsAbstract ||
            method.IsGenericMethod || method.ContainsGenericParameters || method.ReturnType != typeof(void) ||
            method.DeclaringType == null || !behaviourBase.IsAssignableFrom(method.DeclaringType) ||
            method.GetParameters().Length != 0)
            return false;
        var implementation = method.GetMethodImplementationFlags();
        if ((implementation & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL ||
            (implementation & MethodImplAttributes.ManagedMask) != MethodImplAttributes.Managed ||
            (implementation & MethodImplAttributes.InternalCall) != 0)
            return false;
        try { return method.GetMethodBody() != null; }
        catch (Exception) { return false; }
    }

    public static IEnumerable<MethodInfo> FindCallbacks(Type type, Type behaviourBase)
    {
        if (type == null || behaviourBase == null || !behaviourBase.IsAssignableFrom(type)) yield break;
        // Private Unity messages in a base class are not returned by inherited GetMethods.
        for (var declaring = type; declaring != null && behaviourBase.IsAssignableFrom(declaring); declaring = declaring.BaseType)
            foreach (var method in declaring.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (IsSupported(method, behaviourBase)) yield return method;
    }
}
