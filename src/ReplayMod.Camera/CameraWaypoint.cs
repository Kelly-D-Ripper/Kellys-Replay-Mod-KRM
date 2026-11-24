using System;
using System.Collections.Generic;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Camera;

public class CameraWaypoint : MonoBehaviour
{
	public Action onPositionChanged;

	public Action onDestroy;

	private GameObject sphere;

	public Action onDelete;

	public bool controlPoint = false;

	private List<CameraWaypoint> _controls = new List<CameraWaypoint>();

	public double Time;

	public CameraWaypoint Parent { get; private set; }

	public static CameraWaypoint Create(Vector3 position, Quaternion rotation = default(Quaternion), double time = 0.0)
	{
		GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		gameObject.name = "Replay camera spline point";
		gameObject.transform.position = position;
		gameObject.transform.rotation = rotation;
		gameObject.transform.localScale = Vector3.one * 2f;
		gameObject.transform.SetParent(Datum.origin);
		Collider component = gameObject.GetComponent<Collider>();
		component.isTrigger = true;
		CameraWaypoint cameraWaypoint = gameObject.AddComponent<CameraWaypoint>();
		cameraWaypoint.Time = time;
		cameraWaypoint.sphere = gameObject;
		ReplayCameraPreview.Attach(gameObject,gameObject.GetComponent<Renderer>(),Color.yellow,CameraPreviewKind.Spline);
		return cameraWaypoint;
	}

	public static CameraWaypoint Create(PositionSnapshot snapshot)
	{
		return Create(snapshot.position.ToLocalPosition(), snapshot.rotation, snapshot.time);
	}

	private void OnDestroy()
	{
		onPositionChanged = null;
		onDestroy?.Invoke();
		onDestroy = null;
		UnityEngine.Object.Destroy(sphere);
		DetachFromParent();
	}

	public void AddControlPoint(CameraWaypoint point)
	{
		_controls.Add(point);
	}

	public void DeleteControlPoint(CameraWaypoint point)
	{
		if (_controls.Contains(point))
		{
			_controls.Remove(point);
		}
	}

	public void DetachFromParent()
	{
		if (Parent != null)
		{
			Parent.DeleteControlPoint(this);
		}
	}

	public void SetParent(CameraWaypoint joint)
	{
		Parent = joint;
		controlPoint = true;
		joint.AddControlPoint(this);
	}

	public void MirrorControlPoint(CameraWaypoint mirrorFrom)
	{
		if (mirrorFrom == null || !mirrorFrom.controlPoint)
		{
			Plugin.logger.LogWarning((object)"point is null or not a control point");
			return;
		}
		foreach (CameraWaypoint control in _controls)
		{
			if (mirrorFrom != control)
			{
				Vector3 vector = mirrorFrom.transform.position - mirrorFrom.Parent.transform.position;
				control.transform.position = control.Parent.transform.position - vector;
				control.onPositionChanged?.Invoke();
			}
		}
	}

	public void SetRenderActive(bool render)
	{
		ReplayCameraPreview.SetVisible(sphere,render);
	}
	internal GameObject PreviewObject => sphere;
}
