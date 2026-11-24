using System.IO;

namespace ReplayMod.Events;

public interface IReplayEvent
{
	double Time { get; set; }

	EventType EventType { get; }

	void Reset();

	void Write(BinaryWriter bw);

	void Read(BinaryReader br);

	void Execute(object worker = null);
}
