using ReplayMod.Director;

public static class VisibilityChecks
{
    private sealed class Item {public bool Visible;public bool Alive=true;}
    public static int Run()
    {
        int passed=0;
        Test("restore keeps originally disabled objects disabled",()=>
        {
            var states=new VisibilityState<Item>();var shown=new Item{Visible=true};var hidden=new Item{Visible=false};
            Hide(states,shown,hidden);Require(!shown.Visible&&!hidden.Visible,"all hidden");Restore(states);
            Require(shown.Visible&&!hidden.Visible,"original states restored");
        });
        Test("repeated hiding does not overwrite entry state",()=>
        {
            var states=new VisibilityState<Item>();var shown=new Item{Visible=true};var hidden=new Item();
            Hide(states,shown,hidden);shown.Visible=true;hidden.Visible=true;Hide(states,shown,hidden);
            Require(!shown.Visible&&!hidden.Visible,"native re-enables suppressed");Restore(states);
            Require(shown.Visible&&!hidden.Visible,"first snapshot preserved");
        });
        Test("new objects hide and destroyed objects safely drop out",()=>
        {
            var states=new VisibilityState<Item>();var old=new Item{Visible=true};Hide(states,old);old.Alive=false;
            var added=new Item{Visible=true};var disabled=new Item();Hide(states,old,added,disabled);Restore(states);
            Require(added.Visible&&!disabled.Visible,"dynamic states restored");
        });
        Test("restoring clears previous session state",()=>
        {
            var states=new VisibilityState<Item>();var item=new Item{Visible=true};Hide(states,item);Restore(states);
            item.Visible=false;Hide(states,item);Restore(states);Require(!item.Visible,"fresh entry state used");
            Restore(states);Require(!item.Visible,"second restore is a no-op");
        });
        return passed;

        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS clean visibility "+name);}
        void Require(bool value,string message){if(!value)throw new Exception(message);}
        void Hide(VisibilityState<Item> state,params Item[] items)=>state.Hide(items,i=>i.Alive,i=>i.Visible,(i,v)=>i.Visible=v);
        void Restore(VisibilityState<Item> state)=>state.Restore(i=>i.Alive,(i,v)=> {Require(i.Alive,"destroyed object must not be written");i.Visible=v;});
    }
}
