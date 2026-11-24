using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using ReplayMod.Core;
using UnityEngine;

public static class HangarProbe
{
    public static void Run(Player player,Action<string> log)
    {
        var world=ReplayWorldPlayback.Current;if(world==null)throw new Exception("Replay world unavailable");
        var doors=(IDictionary)typeof(ReplayWorldPlayback).GetField("doors",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
        int resolved=0,total=0;
        foreach(uint id in new uint[]{122,125})
        {
            var actor=player.FindActor(id);if(actor==null){log("HANGAR actor "+id+" unavailable");continue;}
            log("HANGAR actor "+id+" "+actor.definition?.jsonKey);
            foreach(var h in Resources.FindObjectsOfTypeAll<Hangar>().Where(h=>h!=null&&h.attachedUnit==actor))
            {
                string path;try{path=ReplayWorldPlayback.HangarPath(actor,h)??"not beneath unit";}catch(Exception e){path=e.Message;}
                log("HANGAR current "+path+" name="+h.name);
            }
            foreach(DictionaryEntry entry in doors)
                if(entry.Key.ToString().StartsWith(id+":"))
                {
                    total++;var field=entry.Value.GetType().GetField("hangar",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                    var bound=(Hangar)field.GetValue(entry.Value);if(bound!=null)resolved++;
                    log("HANGAR recorded "+entry.Key+" bound="+(bound!=null));
                }
        }
        if(total!=9||resolved!=9)throw new Exception("Carrier hangars did not all bind after initialization: "+resolved+"/"+total);
        log("HANGAR PASS all nine recorded carrier hangars bound after initialization");
    }
}
