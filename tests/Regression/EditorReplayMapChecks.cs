using ReplayMod.Core;

public static class EditorReplayMapChecks
{
    public static int Run()
    {
        int passed=0;
        void Check(bool result,string message){if(!result)throw new Exception(message);}
        void Test(string name,Action test){test();passed++;Console.WriteLine("PASS editor replay map "+name);}
        Test("loaded terrain wins over unsaved or changed editor mission",()=>
        {
            int queries=0;
            Check(ReplayMapIdentity.TrySelectRecordingMap(1,"Terrain2",1,"Terrain1",(type,path)=>{queries++;return true;},out var type,out var path)&&
                type==1&&path=="Terrain2"&&queries==1,"Loaded world must win over selected editor mission");
            Check(ReplayMapIdentity.TrySelectRecordingMap(2,"Assets/Maps/Custom.unity",0,null,(_,_)=>true,out type,out path)&&
                type==2&&path=="Assets/Maps/Custom.unity","Built-in custom scene identity must survive");
        });
        Test("only empty unloaded key permits an independently available editor terrain fallback",()=>
        {
            Check(ReplayMapIdentity.TrySelectRecordingMap(0,null,1,"Terrain2",(type,path)=>type==1&&path=="Terrain2",out var type,out var path)&&
                type==1&&path=="Terrain2","Explicit editor terrain should recover an empty loader key");
            Check(!ReplayMapIdentity.TrySelectRecordingMap(0,null,0,null,(_,_)=>true,out type,out path)&&type==0&&path==null,"No terrain must not guess a default map");
            Check(!ReplayMapIdentity.TrySelectRecordingMap(0,null,1,"Missing",(_,_)=>false,out _,out _),"Unavailable mission terrain must fail");
        });
        Test("missing or malformed loaded terrain never silently switches maps",()=>
        {
            Check(!ReplayMapIdentity.TrySelectRecordingMap(1,"Missing",1,"Terrain1",(_,path)=>path=="Terrain1",out _,out _),"Unavailable loaded terrain cannot use another mission's map");
            foreach(var loaded in new[]{(Type:(byte)3,Path:"Terrain2"),(Type:(byte)1,Path:""),(Type:(byte)0,Path:"Terrain2"),(Type:(byte)1,Path:"Terrain\n1")})
                Check(!ReplayMapIdentity.TrySelectRecordingMap(loaded.Type,loaded.Path,1,"Terrain1",(_,_)=>true,out _,out _),"Malformed loaded identity was replaced");
            Check(!ReplayMapIdentity.IsExplicitTerrain(1,new string('x',4097)),"Map keys must respect header bounds");
        });
        Test("load errors identify terrain and retain bounded native reason",()=>
        {
            string report=ReplayMapIdentity.LoadFailure("Mission interrupted",1,"Terrain2","Missing airbase\nMore detail");
            Check(report.Contains("Terrain2")&&report.Contains("Missing airbase More detail")&&!report.Contains('\n'),"Native load reason and terrain must remain readable");
            Check(ReplayMapIdentity.LoadFailure("Map unavailable",2,"Custom",null).Contains("required mods"),"Missing native reason must have useful instructions");
            Check(ReplayMapIdentity.LoadFailure("Mission",1,new string('p',10000),new string('r',10000)).Length<2400,"Messages must be bounded");
        });
        return passed;
    }
}
