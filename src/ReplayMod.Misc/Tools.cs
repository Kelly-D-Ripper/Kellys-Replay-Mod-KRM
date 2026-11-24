using System.Collections.Generic;
using ReplayMod.Data;

namespace ReplayMod.Misc;

public static class Tools
{
	public static int FindLastIndex(List<PositionSnapshot> positions, double time)
	{
		int num = 0;
		int num2 = positions.Count - 1;
		int result = -1;
		while (num <= num2)
		{
			int num3 = (num + num2) / 2;
			if (positions[num3].time <= time)
			{
				result = num3;
				num = num3 + 1;
			}
			else
			{
				num2 = num3 - 1;
			}
		}
		return result;
	}

	public static int FindLastIndex(List<TurretSnapshot> positions, double time)
	{
		int num = 0;
		int num2 = positions.Count - 1;
		int result = -1;
		while (num <= num2)
		{
			int num3 = (num + num2) / 2;
			if (positions[num3].time <= time)
			{
				result = num3;
				num = num3 + 1;
			}
			else
			{
				num2 = num3 - 1;
			}
		}
		return result;
	}

	public static int FindLastIndex(List<Inputs> positions, double time)
	{
		int num = 0;
		int num2 = positions.Count - 1;
		int result = -1;
		while (num <= num2)
		{
			int num3 = (num + num2) / 2;
			if (positions[num3].time <= time)
			{
				result = num3;
				num = num3 + 1;
			}
			else
			{
				num2 = num3 - 1;
			}
		}
		return result;
	}
}
