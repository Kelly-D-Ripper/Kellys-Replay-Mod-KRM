using System.Collections.Concurrent;
using System.IO;
using ReplayMod.Events;

namespace ReplayMod.Core;

public static class ReplayFileWriter
{
    public static long Drain(BlockingCollection<IReplayEvent> queue, BinaryWriter writer)
    {
        long count = 0;
        foreach (var e in queue.GetConsumingEnumerable())
        {
            try
            {
                writer.Write((byte)e.EventType);
                e.Write(writer);
                count++;
            }
            finally { ReplayEventFactory.Return(e); }
        }
        writer.Flush();
        return count;
    }
}
