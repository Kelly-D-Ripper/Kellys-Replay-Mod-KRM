using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Data;

public static class ActorIdentityText
{
    public const int MaxCharacters=128,MaxBytes=512;
    private static readonly UTF8Encoding StrictUtf8=new UTF8Encoding(false,true);
    public static string Normalize(string value)
    {
        if(string.IsNullOrEmpty(value))return "";
        var text=new StringBuilder(Math.Min(value.Length,MaxCharacters));bool space=false;
        for(int i=0;i<value.Length&&text.Length<MaxCharacters;i++)
        {
            char c=value[i];
            if(char.IsWhiteSpace(c)||char.IsControl(c)||char.GetUnicodeCategory(c)==UnicodeCategory.Format||c=='<'||c=='>')
            {space=text.Length>0;continue;}
            if(space&&text.Length<MaxCharacters){text.Append(' ');space=false;}
            if(char.IsHighSurrogate(c))
            {
                if(i+1<value.Length&&char.IsLowSurrogate(value[i+1]))
                {if(text.Length+2>MaxCharacters)break;text.Append(c);text.Append(value[++i]);}
                else text.Append('\uFFFD');
            }
            else if(char.IsLowSurrogate(c))text.Append('\uFFFD');
            else if(text.Length<MaxCharacters)text.Append(c);
        }
        return text.ToString().TrimEnd();
    }
    public static bool Valid(string value)
    {
        if(value==null||value.Length>MaxCharacters||!StringComparer.Ordinal.Equals(value,Normalize(value)))return false;
        try{return StrictUtf8.GetByteCount(value)<=MaxBytes;}catch(EncoderFallbackException){return false;}
    }
    public static string Caption(ActorIdentitySnapshot value)
        =>value?.CaptionText??"";
}

public sealed class ActorIdentitySnapshot
{
    public readonly string PlayerName,Callsign,CaptionText;
    public readonly bool HumanControlled;
    public ActorIdentitySnapshot(string playerName,string callsign,bool humanControlled)
    {
        HumanControlled=humanControlled;PlayerName=humanControlled?ActorIdentityText.Normalize(playerName):"";Callsign=ActorIdentityText.Normalize(callsign);
        CaptionText=PlayerName.Length==0?Callsign:Callsign.Length==0||StringComparer.Ordinal.Equals(PlayerName,Callsign)?PlayerName:PlayerName+" · "+Callsign;
    }
    public static ActorIdentitySnapshot From(ActorIdentityEvent value)=>new ActorIdentitySnapshot(value.playerName,value.callsign,value.humanControlled);
    public bool Same(ActorIdentitySnapshot other)=>other!=null&&HumanControlled==other.HumanControlled&&
        StringComparer.Ordinal.Equals(PlayerName,other.PlayerName)&&StringComparer.Ordinal.Equals(Callsign,other.Callsign);
}

// Exact spawn-time ownership prevents a reused actor ID inheriting another
// player's caption, even if queried during a replacement/failed-spawn boundary.
public sealed class ActorIdentityCache
{
    private sealed class Entry {internal double birth,time;internal ActorIdentitySnapshot value;}
    private readonly Dictionary<uint,Entry> identities=new Dictionary<uint,Entry>();
    public void Apply(ActorIdentityEvent value,double birth)
    {
        if(!value.HasValidPayload()||double.IsNaN(birth)||double.IsInfinity(birth)||value.Time<birth)return;
        if(identities.TryGetValue(value.unitId,out var old)&&old.birth==birth&&old.time>value.Time)return;
        identities[value.unitId]=new Entry {birth=birth,time=value.Time,value=ActorIdentitySnapshot.From(value)};
    }
    public ActorIdentitySnapshot Get(uint id,double birth)=>identities.TryGetValue(id,out var value)&&value.birth==birth?value.value:null;
    public void Forget(uint id)=>identities.Remove(id);
    public void Clear()=>identities.Clear();
}
