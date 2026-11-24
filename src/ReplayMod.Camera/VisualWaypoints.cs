using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Patches;
using UnityEngine;

namespace ReplayMod.Camera;

[Obsolete]
public class VisualWaypoints : MonoBehaviour
{
	public List<PositionSnapshot> CameraWaypoints = new List<PositionSnapshot>();

	private Dictionary<PositionSnapshot, GameObject> _3Dwaypoints = new Dictionary<PositionSnapshot, GameObject>();

	private LineRenderer _currentLine;

	public bool RenderEnabled { get; private set; } = true;

	private void Awake()
	{
		DatumPatch.onShiftOrigin = (Action)Delegate.Combine(DatumPatch.onShiftOrigin, new Action(UpdateLineRender));
	}

	public void Reset()
	{
		ReplayCameraPreview.DestroyOwned(CameraPreviewKind.LegacyTrail);
		if (_currentLine != null)
		{
			UnityEngine.Object.Destroy(_currentLine.gameObject);
		}
		foreach (GameObject value in _3Dwaypoints.Values)
		{
			if (value != null)
			{
				UnityEngine.Object.Destroy(value);
			}
		}
		CameraWaypoints.Clear();
		_3Dwaypoints.Clear();
	}

	public void Restore()
	{
		ReplayCameraPreview.DestroyOwned(CameraPreviewKind.LegacyTrail);
		_3Dwaypoints.Clear();_currentLine=null;
		foreach (PositionSnapshot cameraWaypoint in CameraWaypoints)
		{
			_3Dwaypoints[cameraWaypoint] = CreateWaypoint3DMarker(cameraWaypoint.position.ToLocalPosition(), cameraWaypoint.rotation);
		}
		UpdateLineRender();
	}

	public void AddWaypoint(PositionSnapshot position)
	{
		GameObject value = CreateWaypoint3DMarker(position.position.ToLocalPosition(), position.rotation);
		CameraWaypoints.Add(position);
		_3Dwaypoints.Add(position, value);
		UpdateLineRender();
	}

	private GameObject CreateWaypoint3DMarker(Vector3 position, Quaternion quaternion)
	{
		GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		gameObject.name = "Replay legacy trail point";
		gameObject.transform.position = position;
		gameObject.transform.rotation = quaternion;
		gameObject.transform.localScale = Vector3.one * 2f;
		Renderer component = gameObject.GetComponent<Renderer>();
		ReplayCameraPreview.Attach(gameObject,component,Color.yellow,CameraPreviewKind.LegacyTrail);
		gameObject.transform.SetParent(Datum.origin);
		Collider[] components = gameObject.GetComponents<Collider>();
		Collider[] array = components;
		foreach (Collider collider in array)
		{
			collider.enabled = false;
		}
		ReplayCameraPreview.SetVisible(gameObject,RenderEnabled);
		return gameObject;
	}

	public void DeleteLast()
	{
		if (CameraWaypoints.Count > 0)
		{
			PositionSnapshot key = CameraWaypoints[CameraWaypoints.Count - 1];
			CameraWaypoints.RemoveAt(CameraWaypoints.Count - 1);
			if (_3Dwaypoints.TryGetValue(key, out var value))
			{
				UnityEngine.Object.Destroy(value);
				_3Dwaypoints.Remove(key);
			}
		}
		UpdateLineRender();
	}

	private LineRenderer CreateLine()
	{
		GameObject gameObject = new GameObject();
		gameObject.name = "Replay legacy trail guide";
		LineRenderer lineRenderer = gameObject.AddComponent<LineRenderer>();
		lineRenderer.startWidth = 0.2f;
		lineRenderer.endWidth = 0.2f;
		ReplayCameraPreview.Attach(gameObject,lineRenderer,Color.yellow,CameraPreviewKind.LegacyTrail);
		lineRenderer.startColor = Color.yellow;
		lineRenderer.endColor = Color.yellow;
		lineRenderer.positionCount = 0;
		return lineRenderer;
	}

	private void UpdateLineRender()
	{
		if (_currentLine != null)
		{
			UnityEngine.Object.Destroy(_currentLine.gameObject);
		}
		if (CameraWaypoints.Count > 0)
		{
			_currentLine = CreateLine();
			_currentLine.positionCount = CameraWaypoints.Count;
			Vector3[] positions = CameraWaypoints.Select((PositionSnapshot w) => w.position.ToLocalPosition()).ToArray();
			_currentLine.SetPositions(positions);
			_currentLine.transform.SetParent(Datum.origin);
			ReplayCameraPreview.SetVisible(_currentLine.gameObject,RenderEnabled);
		}
		SetCaptureHidden(ReplayMod.Director.ReplayDirector.i?.Clean==true);
	}

	public void SetRenderActive(bool flag)
	{
		RenderEnabled = flag;
		foreach (KeyValuePair<PositionSnapshot, GameObject> _3Dwaypoint in _3Dwaypoints)
		{
			ReplayCameraPreview.SetVisible(_3Dwaypoint.Value,flag);
		}
		if (_currentLine != null)
		{
			ReplayCameraPreview.SetVisible(_currentLine.gameObject,flag);
		}
		SetCaptureHidden(ReplayMod.Director.ReplayDirector.i?.Clean==true);
	}
	public void SetCaptureHidden(bool hidden)
	{
		ReplayCameraPreview.SetCaptureHidden(hidden);
	}
	private void OnDestroy()
	{
		DatumPatch.onShiftOrigin -= UpdateLineRender;
		ReplayCameraPreview.DestroyOwned(CameraPreviewKind.LegacyTrail);
	}
}
