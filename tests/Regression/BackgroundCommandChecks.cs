using ReplayMod.Diagnostics;

public static class BackgroundCommandChecks
{
    public static void Run(Action<string,Action> test)
    {
        const string session="abc123";
        BackgroundCommand Read(string action,string extra="")=>BackgroundCommand.Parse("session="+session+"\nid=123\naction="+action+"\n"+extra,session);
        void Reject(string text)
        {try{BackgroundCommand.Parse(text,session);}catch(InvalidDataException){return;}throw new Exception("Unsafe command accepted");}
        test("background commands reject stale sessions malformed duplicates and oversized input",()=> {
            Reject("session=other\nid=1\naction=pause");Reject("session=abc123\nid=1\nid=2\naction=pause");
            Reject("session=abc123\nid=1\naction=status\nunknown=1");Reject(new string('x',2049));
            Reject("session=abc123\nid=../bad\naction=status");Reject("session=abc123\nid=1\naction=quit");
        });
        test("background numeric controls accept finite replay ranges only",()=> {
            if(Read("seek","value=123.125").Value!=123.125||Read("speed","value=0.1").Value!=0.1)throw new Exception("Numeric command changed");
            foreach(var pair in new[]{("seek","-1"),("seek","NaN"),("seek","Infinity"),("speed","0"),("speed","1.01"),("speed","1,0")})
                Reject("session=abc123\nid=1\naction="+pair.Item1+"\nvalue="+pair.Item2);
        });
        test("background open cannot address paths or non-replay files",()=> {
            if(Read("open","file=local-test.replay").File!="local-test.replay")throw new Exception("Filename changed");
            foreach(var file in new[]{"../secret.replay","..\\secret.replay","C:secret.replay","other.json","test.replay\0"})
                Reject("session=abc123\nid=1\naction=open\nfile="+file);
            Reject("session=abc123\nid=1\naction=open\nfile=ok.replay\nvalue=1");
        });
        test("background transport rejects ignored arguments and requires explicit actions",()=> {
            foreach(var action in new[]{"pause","play","status"})
            {if(Read(action).Action!=action)throw new Exception("Action changed");Reject("session=abc123\nid=1\naction="+action+"\nvalue=1");}
            Reject("session=abc123\nid=1");Reject("session=abc123\nid=1\naction=speed");
        });
    }
}
