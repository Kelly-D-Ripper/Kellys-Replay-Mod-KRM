using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using ReplayMod.Core;

public static class DoormanMetadataProbe
{
    public static void Run(Action<string> log)
    {
        string root=AppDomain.CurrentDomain.BaseDirectory;
        string readerPath=Path.Combine(root,"ghost-probe","doorman-rc36-54125C0A2CFD.dll");
        string pluginPath=Path.Combine(root,"BepInEx","plugins","ReplayMod","Kellysreplaymod.dll");
        var doorman=Assembly.LoadFrom(readerPath);var cecil=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="Mono.Cecil");
        var definition=cecil.GetType("Mono.Cecil.AssemblyDefinition",true);var read=definition.GetMethod("ReadAssembly",new[]{typeof(string)});
        var assembly=read.Invoke(null,new object[]{pluginPath});
        try
        {
            var type=doorman.GetType("KellysJOINCHECK.EmbeddedModPreview",true);var method=type.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic);
            var card=method.Invoke(null,new[]{assembly,(object)pluginPath});if(card==null)throw new Exception("DOORMAN did not find embedded preview");
            var t=card.GetType();string Field(string name)=>(string)t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(card);
            string plugin=Field("Plugin"),name=Field("Name"),author=Field("Author"),desc=Field("Description"),image=Field("Image");
            var links=(IEnumerable)t.GetField("Links",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(card);
            bool discord=false;int count=0;foreach(var link in links){count++;var lt=link.GetType();if((string)lt.GetField("Url").GetValue(link)=="https://discord.gg/FhsqkbgtWS"&&(string)lt.GetField("Label").GetValue(link)=="Discord")discord=true;}
            if(plugin!="com.chiester3105.replayMod"||name!="Kelly's Replay Mod"||author!="Brig. Gen. Kelly D. Ripper"||!desc.Contains("chiester3105")||image!=""||!discord||count!=1)throw new Exception("Unexpected preview fields");
            var profileType=doorman.GetType("KellysJOINCHECK.ModProfile",true);var profile=Activator.CreateInstance(profileType,true);
            var contentType=doorman.GetType("KellysJOINCHECK.InstalledContent",true);var content=Array.CreateInstance(contentType,0);
            var catalog=doorman.GetType("KellysJOINCHECK.ModCatalog",true);var catalogRead=catalog.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic);
            var entries=(IEnumerable)catalogRead.Invoke(null,new object[]{content,profile,(Action<string>)(message=>log("DOORMAN catalog: "+message))});
            int found=0;foreach(var entry in entries){var et=entry.GetType();object Get(string field)=>et.GetField(field,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(entry);
                if((string)Get("Id")!=plugin)continue;found++;
                if((string)Get("Name")!="Kelly's Replay Mod (plugin)"||!(bool)Get("Enabled")||Get("Manifest")==null)throw new Exception("DOORMAN catalog failed to attach metadata to loaded plugin");
            }
            if(found!=1)throw new Exception("DOORMAN catalog found "+found+" replay entries");
            log("DOORMAN CATALOG PASS actual 1.4.0-rc.36 catalog found one enabled replay plugin with the embedded preview");
            log("DOORMAN METADATA PASS actual 1.4.0-rc.36 preview reader accepted embedded card; plugin="+plugin+" name="+name+" author="+author+" discord="+discord+" image=none");
        }
        finally{(assembly as IDisposable)?.Dispose();}
    }
}
