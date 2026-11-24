using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReplayMod.Core;

public sealed class ReplayRenameResult
{
    public string Path;
    public int SidecarsMoved;
    public bool Renamed;
}

// Renames names only. Replay fingerprints and project links use replay bytes,
// so neither the recording nor its project/backup needs to be rewritten.
public static class ReplayFileRename
{
    public const int MaximumNameLength=120;
    private static readonly string[] Sidecars={".director.json",".director.json.bak",".director.json.tmp"};

    public static bool TryName(string entered,out string name,out string reason)
    {
        name=entered;reason="";
        if(string.IsNullOrWhiteSpace(name)){reason="Enter a name for the recording.";return false;}
        if(name.EndsWith(".replay",StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.Length-7);
        if(name.Length==0||name.Length>MaximumNameLength||name!=name.Trim()||name.EndsWith(".",StringComparison.Ordinal)||name=="."||name=="..")
        {reason="Use 1–120 characters, without leading/trailing spaces or a final dot.";return false;}
        foreach(char value in name)
            if(char.IsControl(value)||"<>:\"/\\|?*".IndexOf(value)>=0)
            {reason="Names cannot contain control characters or < > : \" / \\ | ? *.";return false;}
        try{new System.Text.UTF8Encoding(false,true).GetByteCount(name);}
        catch(System.Text.EncoderFallbackException){reason="Use complete Unicode characters in the recording name.";return false;}
        string device=name.Split('.')[0].TrimEnd(' ','.').ToUpperInvariant();
        if(device=="CON"||device=="PRN"||device=="AUX"||device=="NUL"||
            (device.Length==4&&(device.StartsWith("COM",StringComparison.Ordinal)||device.StartsWith("LPT",StringComparison.Ordinal))&&
             "123456789¹²³".IndexOf(device[3])>=0))
        {reason="That name is reserved by Windows. Choose another name.";return false;}
        return true;
    }

    public static ReplayRenameResult Rename(string directory,string sourcePath,string enteredName)
        =>Rename(directory,sourcePath,enteredName,File.Move);

    internal static ReplayRenameResult Rename(string directory,string sourcePath,string enteredName,Action<string,string> move)
    {
        if(!TryName(enteredName,out var name,out var reason))throw new IOException(reason);
        string root=System.IO.Path.GetFullPath(directory).TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
        string source=System.IO.Path.GetFullPath(sourcePath);
        if(!string.Equals(System.IO.Path.GetDirectoryName(source),root,StringComparison.OrdinalIgnoreCase)||
            !string.Equals(System.IO.Path.GetExtension(source),".replay",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a completed recording from this replay library.");
        if(!File.Exists(source))throw new FileNotFoundException("The selected recording no longer exists.",source);
        string target=System.IO.Path.Combine(root,name+".replay");
        if(string.Equals(source,target,StringComparison.Ordinal))return new ReplayRenameResult{Path=source};
        if(EntryExists(source+".partial"))throw new IOException("This recording has a pending writer. Wait for saving to finish.");
        bool caseOnly=string.Equals(source,target,StringComparison.OrdinalIgnoreCase);
        foreach(string suffix in new[]{""}.Concat(Sidecars).Concat(new[]{".partial"}))
            if(EntryExists(target+suffix)&&(!caseOnly||Directory.Exists(target+suffix)))
                throw new IOException("That name or one of its camera-project files already exists. Choose another name.");
        var paths=Sidecars.Select(suffix=>(Source:source+suffix,Target:target+suffix)).Where(pair=>File.Exists(pair.Source)).ToList();
        paths.Add((source,target));
        foreach(var pair in paths)
            if((File.GetAttributes(pair.Source)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Linked recording files cannot be renamed from the library.");
        var locks=new List<FileStream>();var moved=new List<(string Source,string Target)>();
        try
        {
            // Deny active readers/writers, but allow these handles to follow
            // rename operations. Lock every source before the first mutation.
            foreach(var pair in paths)locks.Add(new FileStream(pair.Source,FileMode.Open,FileAccess.Read,FileShare.Delete,1));
            try
            {
                foreach(var pair in paths)
                {
                    if(caseOnly)
                    {
                        string temp=System.IO.Path.Combine(root,".replay-rename-"+Guid.NewGuid().ToString("N"));
                        Move(pair.Source,temp);Move(temp,pair.Target);
                    }
                    else Move(pair.Source,pair.Target);
                }
            }
            catch(Exception error)
            {
                var failures=new List<string>();
                for(int index=moved.Count-1;index>=0;index--)
                {
                    var pair=moved[index];
                    try
                    {
                        if(EntryExists(pair.Source))throw new IOException("Original name is occupied");
                        move(pair.Target,pair.Source);
                    }
                    catch(Exception restore){failures.Add(pair.Target+" -> "+pair.Source+": "+restore.Message);}
                }
                if(failures.Count>0)throw new IOException("Rename stopped. Some files could not return to their original names; no files were deleted. Export logs for these locations: "+string.Join("; ",failures),error);
                throw new IOException("Rename could not finish; the original names were restored. "+error.Message,error);
            }
        }
        finally{foreach(var held in locks)held.Dispose();}
        return new ReplayRenameResult{Path=target,Renamed=true,SidecarsMoved=paths.Count-1};

        void Move(string from,string to)
        {
            if(EntryExists(to))throw new IOException("A destination appeared while renaming. Nothing will be overwritten.");
            move(from,to);moved.Add((from,to));
        }
    }

    private static bool EntryExists(string path)
    {
        if(File.Exists(path)||Directory.Exists(path))return true;
        string directory=System.IO.Path.GetDirectoryName(path),name=System.IO.Path.GetFileName(path);
        return Directory.EnumerateFileSystemEntries(directory).Any(entry=>string.Equals(System.IO.Path.GetFileName(entry),name,StringComparison.OrdinalIgnoreCase));
    }
}
