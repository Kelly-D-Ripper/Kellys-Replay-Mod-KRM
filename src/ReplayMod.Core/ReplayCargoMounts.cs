using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

// Bind through native part IDs and part-local slots, not world hierarchy paths:
// complex aircraft physics can reparent whole parts during live recording.
internal sealed class ReplayCargoMounts
{
    private readonly (string key,MountedCargo mount)[] entries;
    internal int Count=>entries.Length;
    internal bool IsValid=>entries.All(entry=>entry.mount!=null);
    internal ReplayCargoMounts(Aircraft aircraft)
    {
        var result=new List<(string,MountedCargo)>();
        if(aircraft?.partLookup!=null)foreach(var part in aircraft.partLookup.Where(p=>p!=null).OrderBy(p=>p.id))
        {
            var mounts=part.GetComponentsInChildren<MountedCargo>(true).Where(m=>m!=null&&m.attachedUnit==aircraft&&m.hardpoint!=null&&m.hardpoint.part==part).ToArray();
            for(int i=0;i<mounts.Length;i++)result.Add((part.id+":"+i+":"+(mounts[i].cargo?.jsonKey??""),mounts[i]));
        }
        entries=result.ToArray();
    }
    internal CargoMountPose[] Capture()
    {
        var result=new CargoMountPose[entries.Length];
        for(int i=0;i<entries.Length;i++)
        {
            var (key,mount)=entries[i];
            if(mount==null)throw new InvalidOperationException("Cargo mount was destroyed during capture");
            result[i]=new CargoMountPose{key=key,active=mount.gameObject.activeSelf,fired=mount.fired,ammo=mount.ammo,
                position=mount.transform.localPosition,rotation=mount.transform.localRotation};
        }
        return result;
    }
    internal bool Apply(CargoMountPose[] snapshot)
    {
        if(snapshot==null||snapshot.Length!=entries.Length)return false;
        for(int i=0;i<entries.Length;i++)if(entries[i].mount==null||!string.Equals(entries[i].key,snapshot[i].key,StringComparison.Ordinal))return false;
        for(int i=0;i<entries.Length;i++)
        {
            var mount=entries[i].mount;var pose=snapshot[i];
            mount.fired=pose.fired;mount.ammo=pose.ammo;
            mount.transform.localPosition=pose.position;mount.transform.localRotation=pose.rotation;
            if(mount.gameObject.activeSelf!=pose.active)mount.gameObject.SetActive(pose.active);
        }
        return true;
    }
}
