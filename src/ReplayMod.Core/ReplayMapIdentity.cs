using System;

namespace ReplayMod.Core;

// Recorded terrain identity is independent of the mission's file/group/editor
// state. Never substitute the currently selected mission or an arbitrary map.
public static class ReplayMapIdentity
{
    public static bool IsExplicitTerrain(byte type,string path)
    {
        if((type!=1&&type!=2)||string.IsNullOrWhiteSpace(path)||path.Length>4096)return false;
        foreach(char value in path)if(char.IsControl(value))return false;
        return true;
    }

    public static bool TrySelectRecordingMap(byte loadedType,string loadedPath,byte missionType,string missionPath,
        Func<byte,string,bool> available,out byte type,out string path)
    {
        if(available==null)throw new ArgumentNullException(nameof(available));
        type=0;path=null;
        if(IsExplicitTerrain(loadedType,loadedPath))
        {
            if(!available(loadedType,loadedPath))return false;
            type=loadedType;path=loadedPath;return true;
        }
        // The native mission can use a default key, but a completed load has a
        // concrete CurrentMap. A fallback is allowed only for an explicit,
        // independently loadable mission terrain; no guessing from filenames.
        if(loadedType!=0||!string.IsNullOrEmpty(loadedPath))return false;
        if(IsExplicitTerrain(missionType,missionPath)&&available(missionType,missionPath))
        {type=missionType;path=missionPath;return true;}
        return false;
    }

    public static string LoadFailure(string stage,byte type,string path,string nativeReason)
    {
        string text=stage+" for replay terrain ("+type+", "+Safe(path,256)+").";
        string reason=Safe(nativeReason,2048);
        return string.IsNullOrWhiteSpace(reason)?text+" Check that the map and its required mods are installed.":text+" "+reason;
    }

    private static string Safe(string value,int maximum)
    {
        value=value??"";
        var chars=value.Substring(0,Math.Min(value.Length,maximum)).ToCharArray();
        for(int index=0;index<chars.Length;index++)if(char.IsControl(chars[index]))chars[index]=' ';
        return new string(chars);
    }
}
