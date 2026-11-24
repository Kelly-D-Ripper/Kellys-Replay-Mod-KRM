using System.Collections.Generic;
using UnityEngine;

namespace ReplayMod.Debug;

public static class DebugTools
{
	private static Unit currentSelection;

	static DebugTools()
	{
		SceneSingleton<DynamicMap>.i.onUnitSelected += delegate(Unit u)
		{
			currentSelection = u;
		};
		SceneSingleton<DynamicMap>.i.onAllDeselected += delegate
		{
			currentSelection = null;
		};
	}

	public static void DisableRbToSelectedAircraft()
	{
		if (!(currentSelection is Aircraft aircraft))
		{
			return;
		}
		aircraft.SetSimplePhysics();
		foreach (UnitPart item in aircraft.partLookup)
		{
			Collider[] componentsInChildren = item.GetComponentsInChildren<Collider>(includeInactive: true);
			Collider[] array = componentsInChildren;
			foreach (Collider collider in array)
			{
				collider.enabled = false;
			}
			Rigidbody rb = item.rb;
			if (rb != null)
			{
				rb.isKinematic = true;
				rb.detectCollisions = false;
			}
		}
	}

	public static void DetachRandomPart()
	{
		if (currentSelection == null)
		{
			currentSelection = SceneSingleton<CameraStateManager>.i.followingUnit;
		}
		if (currentSelection is Aircraft aircraft)
		{
			Transform randomPart = aircraft.GetRandomPart();
			if (randomPart.TryGetComponent<AeroPart>(out var component))
			{
				EnableRb(component);
				component.CreateRB(default(Vector3), component.transform.position);
				aircraft.DetachPart(component.id, default(Vector3), default(Vector3));
			}
		}
	}

	public static void ShowPartLookup()
	{
		if (!GameManager.GetLocalAircraft(out var localAircraft))
		{
			return;
		}
		List<UnitPart> partLookup = localAircraft.partLookup;
		foreach (UnitPart item in partLookup)
		{
			Plugin.logger.LogInfo((object)$"Part id: {item.id}, part name: {item.name}");
		}
	}

	private static void EnableRb(UnitPart part)
	{
		Collider[] componentsInChildren = part.GetComponentsInChildren<Collider>(includeInactive: true);
		Collider[] array = componentsInChildren;
		foreach (Collider collider in array)
		{
			collider.enabled = true;
		}
		Rigidbody rb = part.rb;
		if (rb != null)
		{
			rb.isKinematic = false;
			rb.detectCollisions = true;
		}
	}

	public static void ShowWeaponStations()
	{
		if (currentSelection == null)
		{
			return;
		}
		Unit unit = currentSelection;
		Plugin.logger.LogInfo((object)$"Unit: {unit}");
		foreach (WeaponStation weaponStation in unit.weaponStations)
		{
			Plugin.logger.LogInfo((object)$"number: {weaponStation.Number},  weaponName {weaponStation.WeaponInfo.weaponName}");
		}
	}
}
