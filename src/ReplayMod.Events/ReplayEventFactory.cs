using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ReplayMod.Events;

public static class ReplayEventFactory
{
	private static Dictionary<EventType, Func<IReplayEvent>> _creators = new Dictionary<EventType, Func<IReplayEvent>>();

	private static Dictionary<Type, Action<IReplayEvent>> _returners = new Dictionary<Type, Action<IReplayEvent>>();

	public static T GetEvent<T>() where T : class, IReplayEvent, new()
	{
		return ReplayEventPool<T>.Get();
	}

	public static void Return(IReplayEvent ev)
	{
		if (ev != null)
		{
			Type type = ev.GetType();
			if (!_returners.TryGetValue(type, out var value))
			{
				throw new ArgumentException($"No return delegate registered for type {type}");
			}
			value(ev);
		}
	}

	public static void RegisterTypes()
	{
		Assembly assembly = typeof(IReplayEvent).Assembly;
		IEnumerable<Type> enumerable = from type in assembly.GetTypes()
			where typeof(IReplayEvent).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface
			select type;
		foreach (Type item in enumerable)
		{
			ReplayEventAttribute customAttribute = item.GetCustomAttribute<ReplayEventAttribute>();
			if (customAttribute == null)
			{
				Plugin.logger.LogWarning((object)("Event type [" + item.Name + "] missing attribute, skipping registration"));
				continue;
			}
			MethodInfo methodInfo = typeof(ReplayEventFactory).GetMethod("RegisterEvent", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(item);
			methodInfo.Invoke(null, new object[1] { customAttribute.Type });
			Plugin.logger.LogInfo((object)$"Event type {item} registered");
		}
	}

	private static void RegisterEvent<T>(EventType type) where T : class, IReplayEvent, new()
	{
		_creators[type] = () => ReplayEventPool<T>.Get();
		_returners[typeof(T)] = delegate(IReplayEvent ev)
		{
			ReplayEventPool<T>.Return((T)ev);
		};
	}

	public static IReplayEvent Create(EventType type)
	{
		if (_creators.TryGetValue(type, out var value))
		{
			return value();
		}
		throw new ArgumentOutOfRangeException("type", type, "Unknown event type");
	}

	public static IReplayEvent CreateAndRead(EventType type, BinaryReader reader)
	{
		IReplayEvent replayEvent = Create(type);
		try { replayEvent.Read(reader); return replayEvent; }
		catch { Return(replayEvent); throw; }
	}

	public static void ReturnToPool(this IReplayEvent ev)
	{
		Return(ev);
	}
}
