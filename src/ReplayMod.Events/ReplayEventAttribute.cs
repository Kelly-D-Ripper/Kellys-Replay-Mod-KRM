using System;

namespace ReplayMod.Events;

[AttributeUsage(AttributeTargets.Class)]
public class ReplayEventAttribute : Attribute
{
	public EventType Type { get; }

	public ReplayEventAttribute(EventType type)
	{
		Type = type;
	}
}
