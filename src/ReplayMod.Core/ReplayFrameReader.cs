using System.IO;
using System.Text;

namespace ReplayMod.Core;

// A length-framed event has its own short-lived reader. It must not share
// delta-input history with another event or retain a per-reader cache entry.
internal sealed class ReplayFrameReader : BinaryReader
{
    internal ReplayFrameReader(Stream stream):base(stream){}
    internal ReplayFrameReader(Stream stream,Encoding encoding):base(stream,encoding){}
}
