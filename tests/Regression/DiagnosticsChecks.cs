using System.IO.Compression;
using System.Text;
using ReplayMod.Diagnostics;

public static class DiagnosticsChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("diagnostic redaction covers secrets, personal roots and endpoints",()=> {
            var scrubber=Redactor();
            string input="C:\\Users\\PrivateTester\\Games\\Nuclear Option\\BepInEx\\plugins\\ReplayMod.dll\n"+
                "c:/users/PrivateTester/Documents/log.txt\nUser PrivateTester on TestMachine\n"+
                "password = \"two secret words\"\n\"access_token\": \"secret-json\"\napi_key='secret-key'\n"+
                "Server password = unquoted secret words\nAuthorization: Bearer secret-auth-value\n"+
                "password=first-fragment,second-fragment;third-fragment\n"+
                "\"password\": \"quoted-first\\\"quoted-second\"\n"+
                "password='single-first\\'single-second'\n"+
                "https://name:secret-uri@example.test/?token=secret-query&other=1\n"+
                "IPAddress=192.168.10.22:7777\nCookie=session-data\n"+
                "IPv6=[2001:db8::1234]:443; Time=12:34:56\nAssemblyVersion=v1.0.0.0\n"+
                "ghp_012345678901234567890123456789\n";
            string output=scrubber.Scrub(input);
            foreach(string privateValue in new[]{"PrivateTester","TestMachine","two secret words","secret-json","secret-key","unquoted secret words","secret-auth-value","secret-uri","secret-query","192.168.10.22","session-data","ghp_012345","first-fragment","second-fragment","third-fragment","quoted-first","quoted-second","single-first","single-second","2001:db8"})
                Check(!output.Contains(privateValue,StringComparison.OrdinalIgnoreCase),"redacted "+privateValue);
            Check(output.Contains("<GAME>")&&output.Contains("ReplayMod.dll"),"useful normalized plugin path retained");
            Check(output.Contains("v1.0.0.0")&&output.Contains("12:34:56"),"structured versions and ordinary timestamps remain useful");
        });
        test("diagnostic tail reads shared live logs once and excludes cut credential lines",()=> WithDirectory(dir=> {
            string log=Path.Combine(dir,"live.log");string prefix="password=old-secret-"+new string('x',1024)+"\n";
            byte[] bytes=Encoding.UTF8.GetBytes(prefix+"recent first\nrecent second\n");File.WriteAllBytes(log,bytes);
            using var live=new FileStream(log,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);
            string tail=DiagnosticsBundleWriter.ReadTail(log,64,out bool truncated,out long length);
            Check(truncated&&length==bytes.Length,"bounded source metadata");
            Check(tail.Contains("recent second")&&!tail.Contains("old-secret"),"recent tail only");
            live.Position=live.Length;live.Write(Encoding.UTF8.GetBytes("appended after collection\n"));live.Flush();
            Check(!tail.Contains("appended"),"snapshot tail does not follow future writes");
            File.WriteAllText(Path.Combine(dir,"line.log"),"password="+new string('x',200));
            string cut=DiagnosticsBundleWriter.ReadTail(Path.Combine(dir,"line.log"),32,out _,out _);
            Check(!cut.Contains(new string('x',10)),"a cut-off secret key cannot leak its value");
        }));
        test("diagnostic missing and locked sources still produce a valid useful ZIP",()=> WithDirectory(dir=> {
            string locked=Path.Combine(dir,"locked.log");File.WriteAllText(locked,"locked-log-content");
            using var blocker=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            var input=Input(dir);input.Logs.Add(new DiagnosticsLogSource("locked.log",locked));input.Logs.Add(new DiagnosticsLogSource("missing.log",Path.Combine(dir,"missing.log")));
            var result=DiagnosticsBundleWriter.Write(input);Check(result.UnavailableSources==2,"partial sources counted");
            using var zip=ZipFile.OpenRead(result.Path);string readme=Read(zip,"README.txt");
            Check(readme.Contains("locked.log: unavailable")&&readme.Contains("missing.log: unavailable"),"source failures explained");
            Check(Read(zip,"runtime.txt").Contains("TestState=Paused"),"snapshot retained despite missing logs");
            Check(!Directory.EnumerateFiles(dir,"*.tmp").Any(),"completed bundle leaves no temporary file");
        }));
        test("diagnostic bundle excludes recording payload and sanitizes every exported entry",()=> WithDirectory(dir=> {
            string replay=Path.Combine(dir,"sample.replay");
            using(var file=File.Create(replay))using(var writer=new BinaryWriter(file))
            {writer.Write(12549);writer.Write((byte)5);writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)0);writer.Write("map-key");writer.Write(DateTime.UtcNow.Ticks);writer.Write(0);writer.Write(42.0);writer.Write(17L);writer.Write("RECORDING-PAYLOAD-MUST-NOT-BE-EXPORTED");}
            string log=Path.Combine(dir,"current.log");File.WriteAllText(log,"working normal line\npassword=secret-log\n");
            var input=Input(dir);input.ReplayPath=replay;input.Snapshot+="\n\"token\":\"secret-snapshot\"";input.RecentWarnings="password='secret-warning'";
            input.Logs.Add(new DiagnosticsLogSource("current.log",log));input.HashFiles.Add(new DiagnosticsLogSource("test.dll",log));
            var result=DiagnosticsBundleWriter.Write(input);using var zip=ZipFile.OpenRead(result.Path);
            Check(Read(zip,"replay-metadata.txt").Contains("DurationSeconds=42")&&Read(zip,"replay-metadata.txt").Contains("EventCount=17"),"header metadata retained");
            Check(Read(zip,"replay-metadata.txt").Contains("ReplayFormatVersion=5.0.0")&&Read(zip,"replay-metadata.txt").Contains("ReservedHeaderValue=0"),"production format bytes distinguished from reserved header value");
            foreach(var entry in zip.Entries)
            {
                string text=Read(zip,entry.FullName);
                Check(!text.Contains("secret-log")&&!text.Contains("secret-snapshot")&&!text.Contains("secret-warning")&&!text.Contains("RECORDING-PAYLOAD-MUST"),"safe exported text");
                Check(!entry.FullName.Contains("..")&&!Path.IsPathRooted(entry.FullName),"fixed portable entries");
            }
            Check(Read(zip,"assembly-hashes.txt").Contains("SHA256="),"assembly/file identity included");
            for(byte legacy=1;legacy<=4;legacy++)
            {
                string oldReplay=Path.Combine(dir,"legacy-"+legacy+".replay");
                using(var file=File.Create(oldReplay))using(var writer=new BinaryWriter(file))
                {writer.Write(12549);writer.Write(legacy);writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)0);writer.Write("legacy-map");writer.Write(DateTime.UtcNow.Ticks);writer.Write(0);writer.Write(21.0);writer.Write(8L);}
                var oldInput=Input(dir);oldInput.ReplayPath=oldReplay;var oldResult=DiagnosticsBundleWriter.Write(oldInput);using var oldZip=ZipFile.OpenRead(oldResult.Path);
                Check(Read(oldZip,"replay-metadata.txt").Contains("ReplayFormatVersion="+legacy+".0.0")&&Read(oldZip,"replay-metadata.txt").Contains("DurationSeconds=21"),"legacy header layout retained");
            }
            foreach(byte framedVersion in new byte[]{6,7,8,9,10,11})
            {
                string framedReplay=Path.Combine(dir,"framed-"+framedVersion+".replay");
                using(var file=File.Create(framedReplay))using(var writer=new BinaryWriter(file))
                {new ReplayMod.Core.ReplayRecordingHeader{Format=framedVersion,MapPath="framed-map",Duration=21,EventCount=8,Metadata="{\"schema\":1}"}.Write(writer,true);writer.Write("RECORDING-PAYLOAD-MUST-NOT-BE-EXPORTED");}
                var framedInput=Input(dir);framedInput.ReplayPath=framedReplay;var framedResult=DiagnosticsBundleWriter.Write(framedInput);using var framedZip=ZipFile.OpenRead(framedResult.Path);
                string metadata=Read(framedZip,"replay-metadata.txt");
                Check(metadata.Contains("ReplayFormatVersion="+framedVersion+".0.0")&&metadata.Contains("RecordingCommitted=True")&&metadata.Contains("EventTransport=length+CRC32")&&!metadata.Contains("RECORDING-PAYLOAD"),"framed metadata supported without payload export");
            }
        }));
        test("diagnostic UTF-8 limits and large log collection remain bounded",()=> WithDirectory(dir=> {
            string unicode=new string('x',1000)+string.Concat(Enumerable.Repeat("🚀é",3000));
            string limited=DiagnosticsBundleWriter.LimitUtf8(unicode,1024);Check(Encoding.UTF8.GetByteCount(limited)<=1024&&!limited.Contains('\uFFFD'),"Unicode byte limit with complete surrogate pairs");
            var input=Input(dir);input.Snapshot=unicode+new string('z',200000);input.RecentWarnings=new string('y',600000);
            string log=Path.Combine(dir,"large.log");File.WriteAllText(log,string.Concat(Enumerable.Repeat("line\n",DiagnosticsBundleWriter.MaximumLogBytes/2)));
            for(int i=0;i<4;i++)input.Logs.Add(new DiagnosticsLogSource("tail-"+i+".log",log));
            var result=DiagnosticsBundleWriter.Write(input);using var zip=ZipFile.OpenRead(result.Path);
            Check(zip.Entries.Sum(e=>e.Length)<=DiagnosticsBundleWriter.MaximumBundleTextBytes,"uncompressed bundle budget");
            Check(zip.GetEntry("runtime.txt").Length<=DiagnosticsBundleWriter.MaximumSnapshotBytes,"snapshot cap");
            Check(zip.GetEntry("recent-warnings.txt").Length<=DiagnosticsBundleWriter.MaximumWarningBytes,"warning cap");
            foreach(var e in zip.Entries.Where(e=>e.FullName.StartsWith("logs/")))Check(e.Length<=DiagnosticsBundleWriter.MaximumLogBytes,"per-log cap");
        }));
        test("diagnostic simultaneous writers use unique atomic archive names",()=> WithDirectory(dir=> {
            var paths=new System.Collections.Concurrent.ConcurrentBag<string>();
            Parallel.For(0,6,_=>paths.Add(DiagnosticsBundleWriter.Write(Input(dir)).Path));
            Check(paths.Distinct().Count()==6,"unique archive paths");
            foreach(var path in paths){using var zip=ZipFile.OpenRead(path);Check(zip.GetEntry("README.txt")!=null,"each archive independently valid");}
            Check(!Directory.EnumerateFiles(dir,"*.tmp").Any(),"no unfinished archives");
        }));
    }
    private static DiagnosticsRedactor Redactor()=>new DiagnosticsRedactor(new[]{new KeyValuePair<string,string>("C:\\Users\\PrivateTester\\Games\\Nuclear Option","<GAME>"),new KeyValuePair<string,string>("C:\\Users\\PrivateTester","<USER>")},"PrivateTester","TestMachine");
    private static DiagnosticsBundleInput Input(string dir)=>new DiagnosticsBundleInput {OutputDirectory=dir,Snapshot="TestState=Paused",RecentWarnings="No recent warnings",Redactor=Redactor()};
    private static string Read(ZipArchive zip,string name){using var reader=new StreamReader(zip.GetEntry(name).Open());return reader.ReadToEnd();}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void WithDirectory(Action<string> body)
    {
        string dir=Path.Combine(AppContext.BaseDirectory,"diagnostic-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try{body(dir);}finally
        {
            string full=Path.GetFullPath(dir),root=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("diagnostic-test-",StringComparison.Ordinal))throw new InvalidOperationException("Unsafe diagnostic test cleanup path");
            Directory.Delete(full,true);
        }
    }
}
