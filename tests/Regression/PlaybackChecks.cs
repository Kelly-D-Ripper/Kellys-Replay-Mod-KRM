using ReplayMod.Core;

public static class PlaybackChecks
{
    public static int Run()
    {
        int passed=0;
        Check("pause and resume retain slow motion", () =>
        {
            var state=new PlaybackControlState();
            Equal(state.ClockScale,0); state.SetSpeed(.2f); state.Play(); Equal(state.ClockScale,.2f);
            state.Pause(); Equal(state.ClockScale,0); state.Play(); Equal(state.ClockScale,.2f);
            state.Toggle(); Equal(state.ClockScale,0); state.Toggle(); Equal(state.ClockScale,.2f);
        });
        Check("manual speed clamps and ignores invalid input", () =>
        {
            var state=new PlaybackControlState(); state.Play();
            state.SetSpeed(-1); Equal(state.ClockScale,.1f); state.SetSpeed(10); Equal(state.ClockScale,1);
            state.SetSpeed(.4f); state.SetSpeed(float.NaN); state.SetSpeed(float.PositiveInfinity); Equal(state.ClockScale,.4f);
        });
        Check("fast playback caps only the Unity clock", () =>
        {
            var state=new PlaybackControlState();state.Play();state.SetSpeed(480);
            Equal(state.Speed,480);Equal(state.ClockScale,1);
            long seek=state.RequestSeek(null);Equal(state.ClockScale,0);
            state.CompleteSeek(seek);Equal(state.ClockScale,1);
            state.Pause();Equal(state.ClockScale,0);state.Play();Equal(state.Speed,480);
            state.SetSpeed(900);Equal(state.Speed,480);
        });
        Check("fast replay is split into bounded timestamp intervals", () =>
        {
            double target=PlaybackControlState.FrameTarget(10,1.0/60,480);
            Require(Math.Abs(target-18)<1e-8,"480x requests eight recorded seconds at 60 fps");
            double time=10;int steps=0;
            while(time<target){double next=PlaybackControlState.FastStepTarget(time,target);Require(next-time<=1,"one-second maximum");time=next;steps++;}
            Require(steps==8,"all intervals visited in order");
            Require(PlaybackControlState.FrameTarget(10,2,480)==26,"large stalls cannot create unbounded catch-up debt");
            Require(PlaybackControlState.FrameTarget(10,double.NaN,480)==10,"invalid delta rejected");
        });
        Check("native slow motion uses persistent studio speed", () =>
        {
            var state=new PlaybackControlState();state.Play();
            Require(state.ApplyNativeTimeFactor(.05f),"native slow request accepted");
            Equal(state.Speed,.1f);Equal(state.ClockScale,.1f);
            state.Pause();state.Play();Equal(state.ClockScale,.1f);
            Require(state.ApplyNativeTimeFactor(1),"native normal request accepted");Equal(state.ClockScale,1);
        });
        Check("native speed requests preserve paused and seeking states", () =>
        {
            var state=new PlaybackControlState();
            state.ApplyNativeTimeFactor(.05f);Require(state.IsPaused,"slow key cannot resume pause");Equal(state.ClockScale,0);
            state.Play();long seek=state.RequestSeek(null);state.ApplyNativeTimeFactor(.4f);
            Require(state.IsSeeking&&!state.IsPaused,"speed request cannot end seek or alter play intent");Equal(state.ClockScale,0);
            state.CompleteSeek(seek);Equal(state.ClockScale,.4f);
            state.ApplyNativeTimeFactor(0);Require(state.IsPaused,"native zero pauses");Equal(state.ClockScale,0);
            state.ApplyNativeTimeFactor(1);Require(state.IsPaused,"positive request keeps pause");Equal(state.Speed,1);
        });
        Check("invalid native time factors leave playback state intact", () =>
        {
            var state=new PlaybackControlState();state.SetSpeed(.6f);state.Play();
            foreach(float value in new[] {float.NaN,float.PositiveInfinity,float.NegativeInfinity})
                Require(!state.ApplyNativeTimeFactor(value),"invalid native request rejected");
            Require(!state.IsPaused&&!state.IsSeeking,"invalid requests leave play intent");Equal(state.ClockScale,.6f);
            state.ApplyNativeTimeFactor(-1);Require(state.IsPaused,"negative native request pauses");Equal(state.Speed,.6f);
        });
        Check("queued scrubs coalesce and obsolete completion cannot resume", () =>
        {
            var state=new PlaybackControlState(); state.SetSpeed(.3f); state.Play();
            long first=state.RequestSeek(null), dropped=state.RequestSeek(null), final=state.RequestSeek(false);
            Require(!state.IsLatest(first) && !state.IsLatest(dropped) && state.IsLatest(final),"latest request wins");
            Require(!state.CompleteSeek(first) && !state.CompleteSeek(dropped),"old reload cannot finish latest seek");
            Require(state.IsSeeking,"busy until latest seek ends"); Equal(state.ClockScale,0);
            Require(state.CompleteSeek(final),"latest seek completes"); Require(state.IsPaused,"scrub stays paused");
            state.Play(); Equal(state.ClockScale,.3f);
        });
        Check("seek preserves play state and honors commands during reload", () =>
        {
            var state=new PlaybackControlState(); state.SetSpeed(.1f); state.Play();
            long request=state.RequestSeek(null); Equal(state.ClockScale,0);
            state.SetSpeed(.6f); state.Pause(); state.CompleteSeek(request); Equal(state.ClockScale,0);
            request=state.RequestSeek(true); Equal(state.ClockScale,0); state.CompleteSeek(request); Equal(state.ClockScale,.6f);
            request=state.RequestSeek(false); state.Play(); state.CompleteSeek(request); Equal(state.ClockScale,.6f);
        });
        Check("stop invalidates active and queued seeks", () =>
        {
            var state=new PlaybackControlState(); state.Play();
            long active=state.RequestSeek(null), queued=state.RequestSeek(true); state.CancelSeeks();
            Require(!state.IsLatest(active) && !state.IsLatest(queued),"cancel invalidates both tickets");
            Require(!state.CompleteSeek(active) && !state.CompleteSeek(queued),"stale tasks cannot restore playback");
            Require(!state.IsSeeking && state.IsPaused,"stopped state remains paused"); Equal(state.ClockScale,0);
        });
        return passed;

        void Check(string name, Action action) {action();passed++;Console.WriteLine("PASS playback "+name);}
        void Require(bool value,string message) {if(!value)throw new Exception(message);}
        void Equal(float actual,float expected) {if(Math.Abs(actual-expected)>.00001f)throw new Exception($"{actual} != {expected}");}
    }
}
