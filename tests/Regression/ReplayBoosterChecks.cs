using ReplayMod.Core;
public static class ReplayBoosterChecks
{
    public static int Run()
    {
        int passed=0;
        Test("booster starts once at its native delay and separates only at recorded attachment change",()=>
        {
            var booster=new ReplayBoosterState(.5);
            Check(booster.Apply(true,.2)==ReplayBoosterTransition.None,"pre-ignition held");
            Check(booster.Apply(true,.5)==ReplayBoosterTransition.Activate,"native ignition delay");
            Check(booster.Apply(true,30)==ReplayBoosterTransition.None,"long playback cannot cause early separation or repeated audio");
            Check(booster.Apply(false,31)==ReplayBoosterTransition.Separate,"recorded separation");
            Check(booster.Apply(false,32)==ReplayBoosterTransition.None && booster.Apply(true,33)==ReplayBoosterTransition.None,"one transition per lifetime");
        });
        Test("seek restores already burning and already separated booster visuals without duplicate ignition",()=>
        {
            var burning=new ReplayBoosterState(.5);
            Check(burning.Apply(true,10)==ReplayBoosterTransition.Activate,"seek into burning stage");
            var separated=new ReplayBoosterState(.5);
            Check(separated.Apply(false,10)==ReplayBoosterTransition.Separate,"seek past separation");
            Check(separated.Apply(true,10)==ReplayBoosterTransition.None,"no ignition flash after separation");
            var nativeAlreadyStarted=new ReplayBoosterState(.5,true);
            Check(nativeAlreadyStarted.Apply(true,10)==ReplayBoosterTransition.None,"native initialization is not repeated");
        });
        return passed;
        void Test(string name,Action action) {action();passed++;Console.WriteLine("PASS "+name);}
    }
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);}
}
