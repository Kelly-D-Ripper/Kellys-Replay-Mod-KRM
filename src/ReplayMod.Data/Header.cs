using System;
using System.IO;
using NuclearOption.SceneLoading;

namespace ReplayMod.Data;

public struct Header
{
	public int appId;

	public byte major;

	public byte minor;

	public byte patch;

	public MapKey.KeyType keyType;

	public string mapPrefabName;

	public DateTime recordingDate;

	public double duration;

	public long eventCount;

	public static Header Read(BinaryReader br)
	{
		int num = br.ReadInt32();
		byte b = br.ReadByte();
		byte b2 = br.ReadByte();
		byte b3 = br.ReadByte();
		MapKey.KeyType keyType = (MapKey.KeyType)br.ReadByte();
		string text = br.ReadString();
		DateTime dateTime = new DateTime(br.ReadInt64());
		br.ReadInt32();
		double num2 = br.ReadDouble();
		long num3 = br.ReadInt64();
		return new Header
		{
			appId = num,
			major = b,
			minor = b2,
			patch = b3,
			keyType = keyType,
			mapPrefabName = text,
			recordingDate = dateTime,
			duration = num2,
			eventCount = num3
		};
	}
}
