using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ReplayMod.Core;

// A small allowlist of compatibility/provenance data. No names, paths, Steam
// identifiers, server addresses or arbitrary configuration enter the recording.
public static class ReplayRecordingProvenance
{
    private static readonly ConcurrentDictionary<string,string> hashes=new ConcurrentDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    private static string Fingerprint(string path)
    {
        try
        {
            var info=new FileInfo(path);if(!info.Exists||info.Length>64L*1024*1024)return "unavailable";
            string key=info.FullName+"\0"+info.Length+"\0"+info.LastWriteTimeUtc.Ticks;
            return hashes.GetOrAdd(key,_=>
            {
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var sha=SHA256.Create())
                {
                    long remaining=info.Length;var buffer=new byte[65536];
                    while(remaining>0){int count=file.Read(buffer,0,(int)Math.Min(remaining,buffer.Length));if(count==0)return "unavailable";sha.TransformBlock(buffer,0,count,buffer,0);remaining-=count;}
                    if(file.Length!=info.Length||File.GetLastWriteTimeUtc(path)!=info.LastWriteTimeUtc)return "unavailable";
                    sha.TransformFinalBlock(Array.Empty<byte>(),0,0);return BitConverter.ToString(sha.Hash).Replace("-","");
                }
            });
        }
        catch{return "unavailable";}
    }
    public static string AddFingerprints(string metadata,string gameAssemblyPath,string recorderAssemblyPath)
    {
        metadata=metadata??"{}";if(!metadata.EndsWith("}",StringComparison.Ordinal))throw new InvalidDataException("Recording provenance is invalid");
        string prefix=metadata.Substring(0,metadata.Length-1).TrimEnd();string separator=prefix.EndsWith("{",StringComparison.Ordinal)?"":",";
        return prefix+separator+"\"game_assembly_file_sha256\":\""+Fingerprint(gameAssemblyPath)+"\",\"recorder_file_sha256\":\""+Fingerprint(recorderAssemblyPath)+"\"}";
    }
    private static string Escape(string value,int maximum)
    {
        value=value??"";if(value.Length>maximum){value=value.Substring(0,maximum);if(value.Length>0&&char.IsHighSurrogate(value[value.Length-1]))value=value.Substring(0,value.Length-1);}
        var text=new StringBuilder();foreach(char c in value)
        {if(c=='"'||c=='\\')text.Append('\\').Append(c);else if(c<32)text.Append("\\u").Append(((int)c).ToString("x4"));else text.Append(c);}
        return text.ToString();
    }
    public static string Build(string gameVersion,string unityVersion,string modVersion,string role,string gameAssemblyVersion,IEnumerable<string> plugins)
    {
        var text=new StringBuilder("{\"schema\":1,\"capture\":\"local_semantic_events\",\"initial_state\":\"observed_at_record_start\",\"remote_motion\":\"locally_observed_native_interpolation\",");
        text.Append("\"game_version\":\"").Append(Escape(gameVersion,128)).Append("\",\"unity_version\":\"").Append(Escape(unityVersion,128));
        text.Append("\",\"mod_version\":\"").Append(Escape(modVersion,128)).Append("\",\"role\":\"").Append(Escape(role,32));
        text.Append("\",\"game_assembly_version\":\"").Append(Escape(gameAssemblyVersion,128)).Append("\",\"plugins\":[");
        bool first=true;int included=0,total=0;
        foreach(string plugin in plugins??Enumerable.Empty<string>())
        {
            total++;if(included>=96)continue;string value=Escape(plugin,384);
            if(Encoding.UTF8.GetByteCount(text.ToString())+Encoding.UTF8.GetByteCount(value)>48*1024)continue;
            if(!first)text.Append(',');text.Append('"').Append(value).Append('"');first=false;included++;
        }
        text.Append("],\"plugins_omitted\":").Append(Math.Max(0,total-included)).Append('}');return text.ToString();
    }
}
