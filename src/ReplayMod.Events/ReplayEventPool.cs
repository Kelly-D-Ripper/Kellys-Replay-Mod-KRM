using System.Collections.Concurrent;
using System.Threading;

namespace ReplayMod.Events;

public static class ReplayEventPool<T> where T : class, IReplayEvent, new()
{
	private static readonly ConcurrentQueue<T> _pool = new ConcurrentQueue<T>();
	public const int MaximumRetained = 4096;
	private static int _retained;
	public static int RetainedCount => Volatile.Read(ref _retained);

	public static T Get()
	{
		if (_pool.TryDequeue(out var result)) { Interlocked.Decrement(ref _retained); return result; }
		return new T();
	}

	public static void Return(T item)
	{
		item.Reset();
		if (Interlocked.Increment(ref _retained) <= MaximumRetained) _pool.Enqueue(item);
		else Interlocked.Decrement(ref _retained);
	}
}
