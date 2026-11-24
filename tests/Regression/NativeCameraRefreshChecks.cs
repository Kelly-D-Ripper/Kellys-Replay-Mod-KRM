using ReplayMod.Core;

internal static class NativeCameraRefreshChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("camera panel refresh at a temporary zero clock cannot pause buffered playback",()=>
        {
            var controls=new PlaybackControlState();controls.Play();controls.SetSpeed(.3f);
            void SliderChanged(float value)
            {
                if(NativeCameraRefreshScope.IsActive)return;
                if(value<=0)controls.Pause();else {controls.SetSpeed(value);controls.Play();}
            }
            for(int i=0;i<100;i++)
            {
                NativeCameraRefreshScope.Enter();
                try { SliderChanged(0); NativeCameraRefreshScope.Enter();try{SliderChanged(0);}finally{NativeCameraRefreshScope.Exit();}SliderChanged(0); }
                finally { NativeCameraRefreshScope.Exit(); }
                if(controls.IsPaused||Math.Abs(controls.Speed-.3f)>.0001f||NativeCameraRefreshScope.IsActive)
                    throw new Exception("refresh mutated requested playback state or leaked its scope");
            }
            SliderChanged(0);if(!controls.IsPaused)throw new Exception("deliberate user pause was suppressed");
            SliderChanged(.1f);if(controls.IsPaused||controls.Speed!=.1f)throw new Exception("deliberate speed edit was suppressed");
        });
        test("camera panel refresh exception releases callback guard and does not affect another thread",()=>
        {
            NativeCameraRefreshScope.Enter();
            try
            {
                if(Task.Run(()=>NativeCameraRefreshScope.IsActive).GetAwaiter().GetResult())throw new Exception("UI refresh guard crossed threads");
                try{NativeCameraRefreshScope.Enter();try{throw new IOException("panel failure");}finally{NativeCameraRefreshScope.Exit();}}
                catch(IOException) { }
                if(!NativeCameraRefreshScope.IsActive)throw new Exception("nested failure released outer scope");
            }
            finally { NativeCameraRefreshScope.Exit(); }
            if(NativeCameraRefreshScope.IsActive)throw new Exception("failed refresh left callbacks disabled");
        });
    }
}
