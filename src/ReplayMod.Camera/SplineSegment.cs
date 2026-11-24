using System;
using System.Collections.Generic;
using ReplayMod.Patches;
using UnityEngine;

namespace ReplayMod.Camera;

public class SplineSegment
{
	public double StartTime => p0.Time;
	public double EndTime => p3.Time;
	private CameraWaypoint p0;

	private CameraWaypoint p3;

	private CameraWaypoint p1;

	private CameraWaypoint p2;

	private LineRenderer curveVisualizer;

	private LineRenderer p0p1_line;

	private LineRenderer p2p3_line;

	private Color segmentColor;

	private int curvePointsCount = 40;

	private Vector3[] p0p1Buffer = new Vector3[2];

	private Vector3[] p2p3Buffer = new Vector3[2];

	private Vector3[] curveBuffer;

	public SplineSegment(CameraWaypoint p0, CameraWaypoint p3)
	{
		Vector3 position = Vector3.Lerp(p0.transform.position, p3.transform.position, 0.3f);
		Vector3 position2 = Vector3.Lerp(p0.transform.position, p3.transform.position, 0.6f);
		segmentColor = CameraManager.i.GetSegmentColor();
		this.p0 = p0;
		this.p3 = p3;
		p1 = CameraWaypoint.Create(position);
		p2 = CameraWaypoint.Create(position2);
		p1.SetParent(p0);
		p2.SetParent(p3);
		p0p1_line = CreateLine();
		p2p3_line = CreateLine();
		curveVisualizer = CreateLine();
		Subscribe();
		DatumPatch.onShiftOrigin = (Action)Delegate.Combine(DatumPatch.onShiftOrigin, new Action(UpdateLines));
		UpdateLines();
		SaveSegment();
	}

	private void UpdateLines()
	{
		if (!(p0p1_line == null) && !(p2p3_line == null) && !(curveVisualizer == null))
		{
			p0p1Buffer[0] = p0.transform.position;
			p0p1Buffer[1] = p1.transform.position;
			p0p1_line.positionCount = 2;
			p0p1_line.SetPositions(p0p1Buffer);
			p2p3Buffer[0] = p2.transform.position;
			p2p3Buffer[1] = p3.transform.position;
			p2p3_line.positionCount = 2;
			p2p3_line.SetPositions(p2p3Buffer);
			if (curveBuffer == null || curveBuffer.Length != curvePointsCount)
			{
				curveBuffer = new Vector3[curvePointsCount];
			}
			curveVisualizer.positionCount = curvePointsCount;
			float num = 1f / (float)(curvePointsCount - 1);
			for (int i = 0; i < curvePointsCount; i++)
			{
				float t = num * (float)i;
				curveBuffer[i] = Interpolate(p0.transform.position, p1.transform.position, p2.transform.position, p3.transform.position, t);
			}
			curveBuffer[0] = p0.transform.position;
			curveBuffer[curvePointsCount - 1] = p3.transform.position;
			curveVisualizer.SetPositions(curveBuffer);
		}
	}

	private LineRenderer CreateLine()
	{
		GameObject gameObject = new GameObject();
		gameObject.name = "Replay camera spline guide";
		LineRenderer lineRenderer = gameObject.AddComponent<LineRenderer>();
		lineRenderer.startWidth = 0.2f;
		lineRenderer.endWidth = 0.2f;
		ReplayCameraPreview.Attach(gameObject,lineRenderer,segmentColor,CameraPreviewKind.Spline);
		lineRenderer.startColor = segmentColor;
		lineRenderer.endColor = segmentColor;
		lineRenderer.positionCount = 0;
		return lineRenderer;
	}

	private Vector3 Interpolate(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
	{
		t = Mathf.Clamp01(t);
		Vector3 vector = (1f - t) * (1f - t) * (1f - t) * p0;
		Vector3 vector2 = 3f * (1f - t) * (1f - t) * t * p1;
		Vector3 vector3 = 3f * (1f - t) * t * t * p2;
		Vector3 vector4 = t * t * t * p3;
		return vector + vector2 + vector3 + vector4;
	}

	public bool TryInterpolate(double time, out Vector3 position, out Quaternion rotation)
	{
		position = Vector3.zero;
		rotation = Quaternion.identity;
		if (p0 != null && p3 != null && p3.Time > p0.Time && p0.Time <= time && time <= p3.Time)
		{
			float t = (float)((time - p0.Time) / (p3.Time - p0.Time));
			position = Interpolate(p0.transform.position, p1.transform.position, p2.transform.position, p3.transform.position, t);
			rotation = Quaternion.Slerp(p0.transform.rotation, p3.transform.rotation, t);
			return true;
		}
		return false;
	}

	private void Subscribe()
	{
		CameraWaypoint cameraWaypoint = p0;
		cameraWaypoint.onPositionChanged = (Action)Delegate.Combine(cameraWaypoint.onPositionChanged, new Action(UpdateLines));
		CameraWaypoint cameraWaypoint2 = p1;
		cameraWaypoint2.onPositionChanged = (Action)Delegate.Combine(cameraWaypoint2.onPositionChanged, new Action(UpdateLines));
		CameraWaypoint cameraWaypoint3 = p2;
		cameraWaypoint3.onPositionChanged = (Action)Delegate.Combine(cameraWaypoint3.onPositionChanged, new Action(UpdateLines));
		CameraWaypoint cameraWaypoint4 = p3;
		cameraWaypoint4.onPositionChanged = (Action)Delegate.Combine(cameraWaypoint4.onPositionChanged, new Action(UpdateLines));
		CameraWaypoint cameraWaypoint5 = p0;
		cameraWaypoint5.onPositionChanged = (Action)Delegate.Combine(cameraWaypoint5.onPositionChanged, new Action(SaveSegment));
		CameraWaypoint cameraWaypoint6 = p1;
		cameraWaypoint6.onPositionChanged = (Action)Delegate.Combine(cameraWaypoint6.onPositionChanged, new Action(SaveSegment));
		CameraWaypoint cameraWaypoint7 = p2;
		cameraWaypoint7.onPositionChanged = (Action)Delegate.Combine(cameraWaypoint7.onPositionChanged, new Action(SaveSegment));
		CameraWaypoint cameraWaypoint8 = p3;
		cameraWaypoint8.onPositionChanged = (Action)Delegate.Combine(cameraWaypoint8.onPositionChanged, new Action(SaveSegment));
		CameraWaypoint cameraWaypoint9 = p3;
		cameraWaypoint9.onDelete = (Action)Delegate.Combine(cameraWaypoint9.onDelete, new Action(Delete));
	}

	private void SaveSegment()
	{
		SplineSegmentSave data = SplineSegmentSave.Create(p0, p1, p2, p3);
		CameraManager.i.SaveData(this, data);
	}

	public void Restore(SplineSegmentSave save)
	{
		p0 = CameraManager.i.GetConnectionPoint(save.p0);
		p1 = CameraWaypoint.Create(save.p1);
		p2 = CameraWaypoint.Create(save.p2);
		p3 = CameraWaypoint.Create(save.p3);
		p1.SetParent(p0);
		p2.SetParent(p3);
		CameraManager.i.AddSegmentEnd(p3);
		Subscribe();
		p0p1_line = CreateLine();
		p2p3_line = CreateLine();
		curveVisualizer = CreateLine();
		UpdateLines();
	}

	public void Delete()
	{
		CameraWaypoint cameraWaypoint = p0;
		cameraWaypoint.onPositionChanged = (Action)Delegate.Remove(cameraWaypoint.onPositionChanged, new Action(UpdateLines));
		CameraWaypoint cameraWaypoint2 = p0;
		cameraWaypoint2.onPositionChanged = (Action)Delegate.Remove(cameraWaypoint2.onPositionChanged, new Action(SaveSegment));
		DatumPatch.onShiftOrigin = (Action)Delegate.Remove(DatumPatch.onShiftOrigin, new Action(UpdateLines));
		UnityEngine.Object.Destroy(p2);
		UnityEngine.Object.Destroy(p1);
		if (p0p1_line != null) UnityEngine.Object.Destroy(p0p1_line.gameObject);
		if (p2p3_line != null) UnityEngine.Object.Destroy(p2p3_line.gameObject);
		if (curveVisualizer != null) UnityEngine.Object.Destroy(curveVisualizer.gameObject);
		CameraManager.i.RemoveSpline(this);
	}
	public void ApplyControlPoints(SplineSegmentSave save)
	{
		p1.transform.SetPositionAndRotation(save.p1.position.ToLocalPosition(),save.p1.rotation);p1.Time=save.p1.time;
		p2.transform.SetPositionAndRotation(save.p2.position.ToLocalPosition(),save.p2.rotation);p2.Time=save.p2.time;
		UpdateLines();SaveSegment();
	}

	public void SetRenderActive(bool render)
	{
		if (curveVisualizer != null) ReplayCameraPreview.SetVisible(curveVisualizer.gameObject,render);
		if (p0p1_line != null) ReplayCameraPreview.SetVisible(p0p1_line.gameObject,render);
		if (p2p3_line != null) ReplayCameraPreview.SetVisible(p2p3_line.gameObject,render);
		if (p0 != null) p0.SetRenderActive(render);
		if (p1 != null) p1.SetRenderActive(render);
		if (p2 != null) p2.SetRenderActive(render);
		if (p3 != null) p3.SetRenderActive(render);
	}
	internal IEnumerable<GameObject> PreviewObjects()
	{
		if(curveVisualizer!=null)yield return curveVisualizer.gameObject;
		if(p0p1_line!=null)yield return p0p1_line.gameObject;
		if(p2p3_line!=null)yield return p2p3_line.gameObject;
		if(p0!=null)yield return p0.PreviewObject;
		if(p1!=null)yield return p1.PreviewObject;
		if(p2!=null)yield return p2.PreviewObject;
		if(p3!=null)yield return p3.PreviewObject;
	}
}
