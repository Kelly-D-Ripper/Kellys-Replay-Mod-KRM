using System.Security.Cryptography;
using ReplayMod.Core;

public static class ReplayRenameChecks
{
    public static int Run()
    {
        int passed=0;
        void Check(bool good,string message){if(!good)throw new Exception(message);}
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS replay rename "+name);}
        void WithFiles(Action<string,string> action)
        {
            string root=Path.Combine(Path.GetTempPath(),"ReplayMod-rename-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{string source=Path.Combine(root,"Original.replay");File.WriteAllBytes(source,Enumerable.Range(0,257).Select(index=>(byte)index).ToArray());action(root,source);}
            finally{Directory.Delete(root,true);}
        }
        Test("rejects path escape, devices and invalid Windows names before touching files",()=>
        {
            foreach(string value in new[]{null,""," ","..",".","../escape","a/b","a\\b","C:drive","<tag>","a?b","a*b","a|b","a\"b","a\nb","a\0b","trailing.","trailing "," leading","CON","con.txt","NUL.replay","COM1","Lpt9.txt","COM¹","AUX",new string('x',121),"broken\ud800"})
                Check(!ReplayFileRename.TryName(value,out _,out var reason)&&!string.IsNullOrEmpty(reason),"Rejected invalid name: "+value);
            Check(ReplayFileRename.TryName("COM10",out _,out _),"Ordinary longer device-like name allowed");
        });
        Test("Unicode rename retains all recording and project bytes and timestamps",()=>WithFiles((root,source)=>
        {
            string[] suffixes={"",".director.json",".director.json.bak",".director.json.tmp"};
            for(int index=1;index<suffixes.Length;index++)File.WriteAllText(source+suffixes[index],"camera \u661f "+index);
            var hashes=suffixes.ToDictionary(s=>s,s=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source+s))));
            var times=suffixes.ToDictionary(s=>s,s=>File.GetLastWriteTimeUtc(source+s));
            var renamed=ReplayFileRename.Rename(root,source,"\u591c\u9593 caf\u00e9 \ud83d\ude80.replay");
            Check(renamed.Renamed&&renamed.SidecarsMoved==3&&Path.GetFileName(renamed.Path)=="\u591c\u9593 caf\u00e9 \ud83d\ude80.replay","Unicode name and extension retained");
            foreach(string suffix in suffixes)Check(!File.Exists(source+suffix)&&File.Exists(renamed.Path+suffix)&&
                hashes[suffix]==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(renamed.Path+suffix)))&&times[suffix]==File.GetLastWriteTimeUtc(renamed.Path+suffix),"Unchanged bytes/time: "+suffix);
        }));
        Test("stray target sidecars, pending recordings and directories prevent any rename",()=>WithFiles((root,source)=>
        {
            foreach(string suffix in new[]{"",".director.json",".director.json.bak",".director.json.tmp",".partial"})
            {
                string occupied=Path.Combine(root,"Other.replay")+suffix;File.WriteAllText(occupied,"untouched");
                try{ReplayFileRename.Rename(root,source,"Other");throw new Exception("Collision accepted");}catch(IOException){}
                Check(File.Exists(source)&&File.ReadAllText(occupied)=="untouched","No collision overwritten");File.Delete(occupied);
            }
            Directory.CreateDirectory(Path.Combine(root,"Other.replay"));
            try{ReplayFileRename.Rename(root,source,"Other");throw new Exception("Directory collision accepted");}catch(IOException){}
            File.WriteAllText(source+".partial","writer");
            try{ReplayFileRename.Rename(root,source,"New");throw new Exception("Pending writer accepted");}catch(IOException){}
            Check(File.Exists(source),"Original remains");
        }));
        Test("case-only and unchanged names preserve bytes without leaving temporary hops",()=>WithFiles((root,source)=>
        {
            File.WriteAllText(source+".director.json","project");var same=ReplayFileRename.Rename(root,source,"Original");Check(!same.Renamed,"Same name is a no-op");
            var renamed=ReplayFileRename.Rename(root,source,"original");
            Check(renamed.Renamed&&Path.GetFileName(renamed.Path)=="original.replay"&&File.ReadAllText(renamed.Path+".director.json")=="project","Case-only name applied with project");
            Check(!Directory.EnumerateFiles(root,".replay-rename-*").Any(),"No temporary hops remain");
            int moves=0;
            try{ReplayFileRename.Rename(root,renamed.Path,"ORIGINAL",(from,to)=>{if(++moves==2)throw new IOException("Case-hop failure");File.Move(from,to);});throw new Exception("Expected case-hop fault");}catch(IOException){}
            Check(File.Exists(renamed.Path)&&File.ReadAllText(renamed.Path+".director.json")=="project"&&!Directory.EnumerateFiles(root,".replay-rename-*").Any(),"Failed case-only hop restores original names and clears temporary path");
        }));
        Test("failed later move restores completed earlier sidecar moves",()=>WithFiles((root,source)=>
        {
            File.WriteAllText(source+".director.json","project");File.WriteAllText(source+".director.json.bak","backup");int moves=0;
            try{ReplayFileRename.Rename(root,source,"New",(from,to)=>{if(++moves==2)throw new IOException("Injected later failure");File.Move(from,to);});throw new Exception("Expected failed move");}catch(IOException error){Check(error.Message.Contains("original names were restored"),"Failure explains rollback");}
            Check(File.Exists(source)&&File.ReadAllText(source+".director.json")=="project"&&File.ReadAllText(source+".director.json.bak")=="backup"&&!Directory.EnumerateFiles(root,"New*").Any(),"All source names restored");
        }));
        Test("destination race never overwrites new data and restores originals",()=>WithFiles((root,source)=>
        {
            File.WriteAllText(source+".director.json","project");string target=Path.Combine(root,"New.replay");
            try{ReplayFileRename.Rename(root,source,"New",(from,to)=>{File.Move(from,to);if(to.EndsWith(".director.json",StringComparison.Ordinal))File.WriteAllText(target,"other writer");});throw new Exception("Expected target race");}catch(IOException){}
            Check(File.Exists(source)&&File.ReadAllText(source+".director.json")=="project"&&File.ReadAllText(target)=="other writer","New target and original files survive race");
        }));
        Test("active read locks and out-of-library files are rejected safely",()=>WithFiles((root,source)=>
        {
            using(var held=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            {try{ReplayFileRename.Rename(root,source,"New");throw new Exception("Active file accepted");}catch(IOException){}Check(File.Exists(source),"Active original stays named");}
            File.WriteAllText(source+".director.json","editing");
            using(var held=new FileStream(source+".director.json",FileMode.Open,FileAccess.Read,FileShare.Read))
            {try{ReplayFileRename.Rename(root,source,"New");throw new Exception("Active project accepted");}catch(IOException){}Check(File.Exists(source)&&File.ReadAllText(source+".director.json")=="editing","Active project blocks all moves");}
            string nested=Path.Combine(root,"Nested");Directory.CreateDirectory(nested);
            try{ReplayFileRename.Rename(nested,source,"Escape");throw new Exception("Outside source accepted");}catch(IOException){}
            Check(File.Exists(source),"No path escape");
        }));
        Test("rollback failure reports surviving locations and deletes no data",()=>WithFiles((root,source)=>
        {
            File.WriteAllText(source+".director.json","project");File.WriteAllText(source+".director.json.bak","backup");int moves=0;
            try{ReplayFileRename.Rename(root,source,"New",(from,to)=>{if(++moves>=2)throw new IOException("Injected lock");File.Move(from,to);});throw new Exception("Expected rollback fault");}
            catch(IOException error){Check(error.Message.Contains("no files were deleted")&&error.Message.Contains("New.replay.director.json"),"Error identifies surviving file");}
            Check(File.Exists(source)&&File.ReadAllText(Path.Combine(root,"New.replay.director.json"))=="project"&&File.ReadAllText(source+".director.json.bak")=="backup","Every original byte stream survives");
        }));
        return passed;
    }
}
