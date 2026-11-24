using System;
using System.Reflection;

namespace ReplayMod.Misc;

public static class ReflectionCache
{
	public static MethodInfo baseUnitDisabled;

	public static Action<Unit, bool, bool> baseUnitDisabledDelegate;

	public static MethodInfo baseUnitOnDestroy;

	public static Action<Unit> baseUnitOnDestroyDelegate;

	static ReflectionCache()
	{
	}

	public static void Init()
	{
		baseUnitDisabled = typeof(Unit).GetMethod("UnitDisabled", BindingFlags.Instance | BindingFlags.Public);
		baseUnitDisabledDelegate = (Action<Unit, bool, bool>)Delegate.CreateDelegate(typeof(Action<Unit, bool, bool>), null, baseUnitDisabled);
		baseUnitOnDestroy = typeof(Unit).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
		baseUnitOnDestroyDelegate = (Action<Unit>)Delegate.CreateDelegate(typeof(Action<Unit>), null, baseUnitOnDestroy);
	}
}
