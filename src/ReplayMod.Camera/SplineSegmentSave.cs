using ReplayMod.Data;

namespace ReplayMod.Camera;

public struct SplineSegmentSave
{
	public PositionSnapshot p0;

	public PositionSnapshot p1;

	public PositionSnapshot p2;

	public PositionSnapshot p3;

	public static SplineSegmentSave Create(CameraWaypoint p0, CameraWaypoint p1, CameraWaypoint p2, CameraWaypoint p3)
	{
		PositionSnapshot positionSnapshot = new PositionSnapshot
		{
			position = p0.transform.position.ToGlobalPosition(),
			rotation = p0.transform.rotation,
			time = p0.Time
		};
		PositionSnapshot positionSnapshot2 = new PositionSnapshot
		{
			position = p1.transform.position.ToGlobalPosition(),
			rotation = p1.transform.rotation,
			time = p1.Time
		};
		PositionSnapshot positionSnapshot3 = new PositionSnapshot
		{
			position = p2.transform.position.ToGlobalPosition(),
			rotation = p2.transform.rotation,
			time = p2.Time
		};
		PositionSnapshot positionSnapshot4 = new PositionSnapshot
		{
			position = p3.transform.position.ToGlobalPosition(),
			rotation = p3.transform.rotation,
			time = p3.Time
		};
		return new SplineSegmentSave
		{
			p0 = positionSnapshot,
			p1 = positionSnapshot2,
			p2 = positionSnapshot3,
			p3 = positionSnapshot4
		};
	}
}
