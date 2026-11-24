using ReplayMod.UI;

internal static class StudioInputChecks
{
    internal static void Run(Action<string,Action> test)
    {
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        test("studio opens with stale global IMGUI focus while replay actions remain guarded",()=>
        {
            Check(StudioInputPolicy.CanToggle(false,false,false,false,false,true),"Hidden menu keyboard control stranded studio shortcut");
            Check(StudioInputPolicy.BlocksReplayActions(false,false,false,true),"Recovery loosened ordinary replay action guards");
            Check(!StudioInputPolicy.CanToggle(false,false,false,true,false,true),"Open IMGUI typing lost its guard");
        });
        test("hidden disabled or unfocused native text entries do not strand the studio",()=>
        {
            foreach(var state in new[]{(false,true,true,true),(true,false,true,true),(true,true,false,true),(true,true,true,false)})
            {
                bool typing=StudioInputPolicy.TextEntryActive(state.Item1,state.Item2,state.Item3,state.Item4);
                Check(!typing,"Inactive or hidden text entry counted as typing");
                Check(StudioInputPolicy.CanToggle(false,false,false,false,typing,false),"Studio shortcut required native map/pause recovery");
            }
        });
        test("active native chat and studio text entry keep shortcuts suppressed",()=>
        {
            bool typing=StudioInputPolicy.TextEntryActive(true,true,true,true);
            foreach(bool visible in new[]{false,true})
            {
                Check(!StudioInputPolicy.CanToggle(false,false,false,visible,typing,false),"Visible focused field lost its typing guard");
                Check(StudioInputPolicy.BlocksReplayActions(false,false,typing,false),"Typing triggered a replay action");
            }
        });
        test("studio restores clean capture without letting key assignment fire a second action",()=>
        {
            Check(StudioInputPolicy.CanToggle(true,false,false,false,true,true),"Hidden capture input prevented studio restore");
            foreach(bool clean in new[]{false,true})
            {
                Check(!StudioInputPolicy.CanToggle(clean,true,false,false,false,false),"Shortcut assignment toggled studio");
                Check(!StudioInputPolicy.CanToggle(clean,false,true,false,false,false),"Assignment frame toggled studio a second time");
            }
        });
        test("disabled native input modules require owned events and ready native modules take over",()=>
        {
            Check(!StudioInputPolicy.UsableNativeEvents(true,false),"Empty native EventSystem suppressed owned fallback");
            Check(!StudioInputPolicy.UsableNativeEvents(false,true),"Inactive native EventSystem suppressed owned fallback");
            Check(!StudioInputPolicy.UsableNativeEvents(false,false),"Missing native input counted as usable");
            Check(StudioInputPolicy.UsableNativeEvents(true,true),"Enabled native event input was not preserved");
        });
    }
}
