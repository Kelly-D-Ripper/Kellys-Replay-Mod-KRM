using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NuclearOption.SavedMission;
using ReplayMod.Core;
using ReplayMod.Data;
using RuntimeHandle;
using Unity.Mathematics;
using UnityEngine;

namespace ReplayMod.Camera;

[DefaultExecutionOrder(500)]
public class CameraManager : MonoBehaviour
{
	private WaypointEditMenu _waypointEdit;

	private RuntimeTransformHandle _activeHandle;

	private CameraWaypoint _pointOnEdit;

	private bool _isDragging = false;

	private Dictionary<SplineSegment, SplineSegmentSave> _segmentsData = new Dictionary<SplineSegment, SplineSegmentSave>();

	private List<PositionSnapshot> _positions = new List<PositionSnapshot>();

	private List<CameraWaypoint> _waypoints = new List<CameraWaypoint>();

	private CameraWaypoint _start;

	private Color[] colors = new Color[3]
	{
		Color.red,
		Color.green,
		Color.blue
	};

	private bool _isMoving = false;
	private bool _editorVisible = true;
	private readonly List<SplineSegment> _orderedSegments = new List<SplineSegment>();
	private readonly ReplayMod.Director.VisibilityState<GameObject> _captureVisuals = new ReplayMod.Director.VisibilityState<GameObject>();

	public static CameraManager i { get; private set; }
	public bool IsFlying => _isMoving;

	private void Start()
	{
		if (i != null)
		{
			UnityEngine.Object.Destroy(base.gameObject);
			return;
		}
		i = this;
		UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
		_waypointEdit = base.gameObject.AddComponent<WaypointEditMenu>();
		UnityEngine.Object.DontDestroyOnLoad(_waypointEdit);
		Subscribe().Forget();
	}

	private async UniTask Subscribe()
	{
		await UniTask.WaitUntil(() => ReplayManager.i != null);
		ReplayManager replayManager = ReplayManager.i;
		replayManager.onReset = (Action)Delegate.Combine(replayManager.onReset, new Action(Restore));
	}

	private void Update()
	{
		HandleInputs();
		if (_isDragging)
		{
			_pointOnEdit.onPositionChanged();
		}
	}
	private void LateUpdate() { if(ReplayManager.i != null) MoveCamera(ReplayManager.i.GetCurrentVirtualTime()); }

	private void HandleInputs()
	{
		if(ConfigManager.ToggleCleanCapture?.Value.GetDown()==true)return;
		if (!ReplayRuntime.IsPlayback || MainMenu.HasInputFocus || SceneSingleton<CameraStateManager>.i == null || _isMoving || ReplayMod.Director.ReplayDirector.i?.Clean==true || ReplayMod.Director.ReplayDirector.i?.IsBusy==true) return;
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		if (!MainMenu.IsPointerOverUI && Input.GetMouseButtonDown(0) && SceneSingleton<CameraStateManager>.i.mainCamera != null)
		{
			Plugin.logger.LogInfo((object)"Start dragging waypoint");
			Ray ray = SceneSingleton<CameraStateManager>.i.mainCamera.ScreenPointToRay(Input.mousePosition);
			if (Physics.Raycast(ray, out var hitInfo))
			{
				Plugin.logger.LogInfo((object)hitInfo);
				if (hitInfo.transform.TryGetComponent<CameraWaypoint>(out var component))
				{
					Plugin.logger.LogInfo((object)"Exctracted waypoint from raycast hit, attaching handle");
					AttachHandleToWaypoint(component);
				}
			}
		}
		if (ConfigManager.CreateCameraWaypoint.Value.GetDown() && SceneSingleton<CameraStateManager>.i.mainCamera != null)
		{
			AddPoint();
		}
		if (ConfigManager.DeleteLastCameraWaypoint.Value.GetDown())
		{
			DeleteLastPoint();
		}
		if (ConfigManager.MirrorControlPoint.Value.GetDown())
		{
			if (_pointOnEdit == null)
			{
				return;
			}
			_pointOnEdit.Parent.MirrorControlPoint(_pointOnEdit);
		}
		if (ConfigManager.DeselectWaypoint.Value.GetDown())
		{
			Deselect();
		}
		if (!Input.GetKeyDown(KeyCode.K))
		{
		}
	}

	private void AttachHandleToWaypoint(CameraWaypoint waypoint)
	{
		if (_activeHandle != null)
		{
			UnityEngine.Object.Destroy(_activeHandle.gameObject);
		}
		_activeHandle = RuntimeTransformHandle.Create(waypoint.transform, HandleType.POSITION);
		_activeHandle.space = HandleSpace.WORLD;
		_activeHandle.axes = HandleAxes.XYZ;
		_activeHandle.autoScale = true;
		_activeHandle.autoScaleFactor = 1f;
		_activeHandle.startedDraggingHandle.AddListener(OnDragStart);
		_activeHandle.endedDraggingHandle.AddListener(OnDragEnd);
		_pointOnEdit = waypoint;
	}

	private void Deselect()
	{
		_pointOnEdit = null;
		_isDragging = false;
		if (_activeHandle != null)
		{
			UnityEngine.Object.Destroy(_activeHandle.gameObject);
			_activeHandle = null;
		}
	}

	private void OnDragStart()
	{
		_isDragging = true;
	}

	private void OnDragEnd()
	{
		_isDragging = false;
	}

	public CameraWaypoint GetConnectionPoint(PositionSnapshot initial)
	{
		if (_start == null)
		{
			CameraWaypoint cameraWaypoint = (_start = CameraWaypoint.Create(initial));
			_waypoints.Add(cameraWaypoint);
			return cameraWaypoint;
		}
		return _waypoints[_waypoints.Count - 1];
	}

	public void AddSegmentEnd(CameraWaypoint point)
	{
		_waypoints.Add(point);
	}

	private void Restore()
	{
		Deselect();
		ReplayCameraPreview.DestroyOwned(CameraPreviewKind.Spline);
		_waypoints.Clear();
		_start = null;
		foreach (var segment in _orderedSegments)
		{
			segment.Restore(_segmentsData[segment]);
			segment.SetRenderActive(_editorVisible&&!_isMoving);
		}
		CaptureEditorVisibility(ReplayMod.Director.ReplayDirector.i?.Clean==true);
	}

	public void SaveData(SplineSegment segment, SplineSegmentSave data)
	{
		_segmentsData[segment] = data;
		if (!_orderedSegments.Contains(segment)) _orderedSegments.Add(segment);
		_orderedSegments.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
	}

	public void AddPoint()
	{
		double currentVirtualTime = ReplayManager.i.GetCurrentVirtualTime();
		if (_waypoints.Count <= 0 || !(currentVirtualTime <= _waypoints[_waypoints.Count - 1].Time))
		{
			SceneSingleton<CameraStateManager>.i.GetCameraPosition(out var positionRotation);
			PositionSnapshot item = new PositionSnapshot
			{
				position = positionRotation.Position,
				rotation = positionRotation.Rotation,
				time = currentVirtualTime
			};
			_positions.Add(item);
			CameraWaypoint cameraWaypoint = CameraWaypoint.Create(item.position.ToLocalPosition(), item.rotation, item.time);
			if (_start == null)
			{
				_start = cameraWaypoint;
			}
			else
			{
				SplineSegment splineSegment = new SplineSegment(_waypoints[_waypoints.Count - 1], cameraWaypoint);
			}
			_waypoints.Add(cameraWaypoint);
			ApplyEditorVisibility();
		}
	}

	private void DeleteLastPoint()
	{
		if (_waypoints.Count != 0)
		{
			CameraWaypoint cameraWaypoint = _waypoints[_waypoints.Count - 1];
			cameraWaypoint.onDelete?.Invoke();
			UnityEngine.Object.Destroy(cameraWaypoint);
			_waypoints.Remove(cameraWaypoint);
			if(_positions.Count>0)_positions.RemoveAt(_positions.Count - 1);
		}
	}

	public void RemoveSpline(SplineSegment segment)
	{
		if (_segmentsData.ContainsKey(segment))
		{
			_segmentsData.Remove(segment);
			_orderedSegments.Remove(segment);
		}
	}

	public Color GetSegmentColor()
	{
		return colors[_segmentsData.Count % colors.Length];
	}

	private void MoveCamera(double time)
	{
		if (!_isMoving || _waypoints.Count < 2 || SceneSingleton<CameraStateManager>.i == null || SceneSingleton<CameraStateManager>.i.mainCamera == null)
		{
			return;
		}
		int lo = 0, hi = _orderedSegments.Count;
		if(_orderedSegments.Count>0 && time<_orderedSegments[0].StartTime)
		{var first=_segmentsData[_orderedSegments[0]].p0;SceneSingleton<CameraStateManager>.i.SetCameraPosition(first.position,first.rotation);return;}
		while (lo < hi) { int mid = lo + (hi - lo) / 2; if (_orderedSegments[mid].StartTime <= time) lo = mid + 1; else hi = mid; }
		if (lo > 0 && _orderedSegments[lo - 1].TryInterpolate(time, out var position, out var rotation))
			SceneSingleton<CameraStateManager>.i.SetCameraPosition(new PositionRotation { Position = position.ToGlobalPosition(), Rotation = rotation });

		if (time > _waypoints[_waypoints.Count - 1].Time)
		{
			StopCamFlight();
		}
	}

	public double GetStartingTime()
	{
		if (_waypoints.Count > 0)
		{
			return _waypoints[0].Time;
		}
		return 0.0;
	}

	public void ToggleFlight()
	{
		if (!_isMoving)
		{
			StartCamFlight().Forget();
		}
		else
		{
			StopCamFlight();
		}
	}

	public async UniTask StartCamFlight()
	{
		if (_waypoints.Count < 2 || !ReplayRuntime.IsPlayback) return;
		ReplayMod.Director.ReplayDirector.i?.StopRail();
		_isMoving = true;
		Deselect();
		foreach (var segment in _segmentsData.Keys) segment.SetRenderActive(false);
		await ReplayManager.i.TimelineJump(GetStartingTime());
		foreach (SplineSegment segment in _segmentsData.Keys)
		{
			segment.SetRenderActive(render: false);
		}
		ReplayManager.i.ResumePlayback();
	}

	public void StopCamFlight()
	{
		_isMoving = false;
		ApplyEditorVisibility();
		CaptureEditorVisibility(ReplayMod.Director.ReplayDirector.i?.Clean==true);
	}

	public void ClearPath()
	{
		StopCamFlight(); Deselect();
		foreach (var segment in new List<SplineSegment>(_segmentsData.Keys)) segment.Delete();
		foreach (var waypoint in _waypoints) if (waypoint != null) UnityEngine.Object.Destroy(waypoint.gameObject);
		ReplayCameraPreview.DestroyOwned(CameraPreviewKind.Spline);
		_waypoints.Clear(); _positions.Clear(); _segmentsData.Clear(); _orderedSegments.Clear(); _start = null;
	}

	public List<CameraWaypoint> GetWaypoints()
	{
		return _waypoints;
	}

	public bool IsEditing()
	{
		return _pointOnEdit != null;
	}

	public double GetWaypointTime()
	{
		if (_pointOnEdit != null)
		{
			return _pointOnEdit.Time;
		}
		return 0.0;
	}

	public void SetNewTime(double time)
	{
		if (_pointOnEdit != null)
		{
			int num = _waypoints.IndexOf(_pointOnEdit);
			double time2;
			if (0 < num && num < _waypoints.Count - 1)
			{
				time2 = math.clamp(time, _waypoints[num - 1].Time, _waypoints[num + 1].Time);
			}
			else if (num == 0)
			{
				double b = ((_waypoints.Count <= 1) ? ReplayManager.i.GetDuration() : _waypoints[num + 1].Time);
				time2 = math.clamp(time, 0.0, b);
			}
			else
			{
				double time3 = _waypoints[num - 1].Time;
				double duration = ReplayManager.i.GetDuration();
				time2 = math.clamp(time, time3, duration);
			}
			if (!double.IsNaN(time2) && !double.IsInfinity(time2))
			{
				_pointOnEdit.Time = time2;
				_pointOnEdit.onPositionChanged?.Invoke();
			}
		}
	}

	public bool IsControlPointSelected()
	{
		return _pointOnEdit != null && _pointOnEdit.controlPoint;
	}

	public bool IsJointPointSelected()
	{
		return _pointOnEdit != null && !_pointOnEdit.controlPoint;
	}

	public void SetEditorVisible(bool visible)
	{
		_editorVisible=visible;
		if(!visible)Deselect();
		ApplyEditorVisibility();
	}
	private void ApplyEditorVisibility()
	{
		foreach(var segment in _orderedSegments)segment.SetRenderActive(_editorVisible && !_isMoving);
		foreach(var point in _waypoints)if(point!=null)point.SetRenderActive(_editorVisible && !_isMoving);
	}
	public void CaptureEditorVisibility(bool hidden)
	{
		ReplayCameraPreview.SetCaptureHidden(hidden);
		if(hidden)
		{
			_isDragging=false;
			_captureVisuals.Hide(CaptureVisualObjects(),obj=>obj!=null,obj=>obj.activeSelf,(obj,visible)=>obj.SetActive(visible));
		}
		else
		{
			_captureVisuals.Restore(obj=>obj!=null,(obj,visible)=>obj.SetActive(visible));
			if(_isMoving||!_editorVisible)ApplyEditorVisibility();
		}
	}
	private IEnumerable<GameObject> CaptureVisualObjects()
	{
		// Spheres/lines have their own exact ownership registry. Keeping them out
		// of this original-state cache preserves editor changes made while hidden.
		// Create gives this editor its own handle GameObject. Never deactivate
		// arbitrary RuntimeTransformHandle hosts: other mods can attach one to a
		// building or vehicle itself.
		if(_activeHandle!=null)yield return _activeHandle.gameObject;
	}
	public List<ReplayMod.Director.SplineRecord> ExportPath()
	{
		var result=new List<ReplayMod.Director.SplineRecord>();
		foreach(var segment in _orderedSegments) {var p=_segmentsData[segment];result.Add(new ReplayMod.Director.SplineRecord {a=ToRecord(p.p0),b=ToRecord(p.p1),c=ToRecord(p.p2),d=ToRecord(p.p3)});}
		return result;
	}
	private static ReplayMod.Director.PoseRecord ToRecord(PositionSnapshot p) => new ReplayMod.Director.PoseRecord {time=p.time,x=p.position.x,y=p.position.y,z=p.position.z,qx=p.rotation.x,qy=p.rotation.y,qz=p.rotation.z,qw=p.rotation.w};
	private static PositionSnapshot ToSnapshot(ReplayMod.Director.PoseRecord p) => new PositionSnapshot {time=p.time,position=new GlobalPosition(p.x,p.y,p.z),rotation=new Quaternion(p.qx,p.qy,p.qz,p.qw)};
	public ReplayMod.Director.PoseRecord ExportFirstPoint()
	{if(_waypoints.Count==0)return null;var p=_waypoints[0];return ToRecord(new PositionSnapshot {time=p.Time,position=p.transform.position.ToGlobalPosition(),rotation=p.transform.rotation});}
	public void ImportPath(List<ReplayMod.Director.SplineRecord> path,ReplayMod.Director.PoseRecord firstPoint=null)
	{
		ClearPath();
		if(path.Count==0 && firstPoint!=null)GetConnectionPoint(ToSnapshot(firstPoint));
		foreach(var p in path)
		{
			var start=GetConnectionPoint(ToSnapshot(p.a));var end=CameraWaypoint.Create(ToSnapshot(p.d));
			var segment=new SplineSegment(start,end);_waypoints.Add(end);
			var save=new SplineSegmentSave {p0=ToSnapshot(p.a),p1=ToSnapshot(p.b),p2=ToSnapshot(p.c),p3=ToSnapshot(p.d)};
			segment.ApplyControlPoints(save);SaveData(segment,save);
		}
		ApplyEditorVisibility();CaptureEditorVisibility(ReplayMod.Director.ReplayDirector.i?.Clean==true);
	}
	public void DrivePath(bool enabled) { _isMoving=enabled && _orderedSegments.Count>0;ApplyEditorVisibility();CaptureEditorVisibility(ReplayMod.Director.ReplayDirector.i?.Clean==true); }
}
