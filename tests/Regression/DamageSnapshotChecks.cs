using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class DamageSnapshotChecks
{
    public static int Run()
    {
        int passed=0;
        Test("absolute damaged HP restores without live impact or historical fragments",()=>
        {
            var helper=new ReplayPartHealthPresentation();var part=new UnitPart();
            foreach(var value in new[]{100f,0f,-123456789f,float.MinValue,float.MaxValue})
            {Require(helper.TryRestoreSnapshot(part,Snapshot(value))&&part.hitPoints==value,"finite recorded HP was clamped or resimulated");}
        });
        Test("real and tiny recorded impacts retain their existing action path",()=>
        {
            var helper=new ReplayPartHealthPresentation();var renderer=Render();var part=Part(renderer);
            foreach(int slot in Enumerable.Range(0,4))
            foreach(float value in new[]{float.Epsilon,.01f,90f})
            {
                var e=Snapshot(10);switch(slot){case 0:e.pierce=value;break;case 1:e.blast=value;break;case 2:e.fire=value;break;default:e.impact=value;break;}
                Require(!helper.TryRestoreSnapshot(part,e)&&part.hitPoints==100&&renderer.Reads==0&&renderer.Writes==0,"nonzero damage must not be silently converted to snapshot");
            }
            var negativeZero=Snapshot(9);negativeZero.pierce=-0f;
            Require(helper.TryRestoreSnapshot(part,negativeZero)&&part.hitPoints==9,"IEEE negative zero is still zero damage");
        });
        Test("nonfinite HP or damage fails clearly before health or rendering mutation",()=>
        {
            foreach(int slot in Enumerable.Range(0,5))
            foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {
                var helper=new ReplayPartHealthPresentation();var renderer=Render();var part=Part(renderer);var e=Snapshot(10);
                switch(slot){case 0:e.hitPoints=value;break;case 1:e.pierce=value;break;case 2:e.blast=value;break;case 3:e.fire=value;break;default:e.impact=value;break;}
                bool failed=false;try{helper.TryRestoreSnapshot(part,e);}catch(InvalidDataException error){failed=error.Message.Contains("actor 7")&&error.Message.Contains("part 2");}
                Require(failed&&part.hitPoints==100&&renderer.Reads==0&&renderer.Writes==0,"invalid damage was applied or silently sanitized");
            }
        });
        Test("optional absent material bindings do not prevent absolute HP restoration",()=>
        {
            var helper=new ReplayPartHealthPresentation();
            foreach(var material in new DamageMaterial[]{null,new DamageMaterial {renderers=null},new DamageMaterial(),new DamageMaterial {renderers=new List<Renderer>{null}}})
            {var part=new UnitPart {damageMaterial=material};Require(helper.TryRestoreSnapshot(part,Snapshot(-20))&&part.hitPoints==-20,"optional binding required");}
            Require(helper.TryRestoreSnapshot(null,Snapshot(10)),"already removed part is a safe no-op snapshot");
        });
        Test("renderer and existing material property blocks retain unrelated values",()=>
        {
            int hp=Shader.PropertyToID("_HitPoints"),tint=Shader.PropertyToID("_TestTint"),fade=Shader.PropertyToID("_TestFade");
            var renderer=Render(3);renderer.Wide[tint]=.42f;renderer.Slots[0]=new Dictionary<int,float>{{fade,.8f}};renderer.Slots[2]=new Dictionary<int,float>{{tint,.7f},{hp,100}};
            new ReplayPartHealthPresentation().TryRestoreSnapshot(Part(renderer),Snapshot(17));
            Require(renderer.Wide[tint]==.42f&&renderer.Wide[hp]==17&&renderer.Slots[0][fade]==.8f&&renderer.Slots[0][hp]==17&&renderer.Slots[2][tint]==.7f&&renderer.Slots[2][hp]==17,"existing shader inputs were lost");
            Require(!renderer.Slots.ContainsKey(1)&&renderer.Shared.All(material=>material.Floats.Count==0),"snapshot created unnecessary slot override or mutated material");
        });
        Test("one unavailable renderer does not block HP or later renderer restoration",()=>
        {
            var readFailure=Render();readFailure.ThrowRead=true;var writeFailure=Render();writeFailure.ThrowWrite=true;var healthy=Render();
            var part=Part(readFailure,writeFailure,healthy);var helper=new ReplayPartHealthPresentation();
            Require(helper.TryRestoreSnapshot(part,Snapshot(-7))&&part.hitPoints==-7&&healthy.Wide[Shader.PropertyToID("_HitPoints")]==-7,"optional visual failure stopped the actor state");
            readFailure.ThrowRead=false;writeFailure.ThrowWrite=false;helper.TryRestoreSnapshot(part,Snapshot(60));
            Require(readFailure.Wide[Shader.PropertyToID("_HitPoints")]==60&&writeFailure.Wide[Shader.PropertyToID("_HitPoints")]==60,"later healthy binding did not recover");
        });
        Test("later real damage refreshes snapshot shader overrides without hiding new HP",()=>
        {
            int hp=Shader.PropertyToID("_HitPoints"),tint=Shader.PropertyToID("_TestTint");
            var renderer=Render(2);renderer.Wide[tint]=.42f;renderer.Slots[1]=new Dictionary<int,float>{{tint,.8f}};
            var helper=new ReplayPartHealthPresentation();var part=Part(renderer);helper.TryRestoreSnapshot(part,Snapshot(80));
            var hit=Snapshot(25);hit.blast=55;
            Require(!helper.TryRestoreSnapshot(part,hit)&&part.hitPoints==80&&renderer.Wide[hp]==80,"nonzero classification must not apply live damage");
            part.hitPoints=25; // Boundary: native ApplyDamage has now applied the recorded hit.
            helper.RefreshDamageShader(part,hit);
            Require(part.hitPoints==25&&renderer.Wide[hp]==25&&renderer.Slots[1][hp]==25&&renderer.Wide[tint]==.42f&&renderer.Slots[1][tint]==.8f,"old HP override masked the later real hit");
        });
        Test("property block cache is reused and values do not leak across parts",()=>
        {
            var helper=new ReplayPartHealthPresentation();var first=Render();first.Wide[Shader.PropertyToID("_OldOnly")]=.9f;
            int before=MaterialPropertyBlock.Created;helper.TryRestoreSnapshot(Part(first),Snapshot(1));
            var next=Render();var part=Part(next);
            for(int n=0;n<2000;n++)helper.TryRestoreSnapshot(part,Snapshot(n));
            Require(MaterialPropertyBlock.Created==before+1&&next.Wide.Count==1&&part.hitPoints==1999,"per-renderer allocation or cross-part property leakage");
        });
        Test("existing damage codec snapshots restore without rewriting live impacts",()=>
        {
            var helper=new ReplayPartHealthPresentation();var original=Snapshot(-200);var encoded=RollingReplayBuffer.Encode(original);
            var decoded=(PartDamageEvent)RollingReplayBuffer.Decode(encoded);var part=new UnitPart();
            Require(helper.TryRestoreSnapshot(part,decoded)&&part.hitPoints==-200&&encoded.SequenceEqual(RollingReplayBuffer.Encode(decoded)),"existing encoded snapshot changed or did not restore");
            ReplayEventFactory.Return(decoded);
            var impact=Snapshot(10);impact.blast=90;encoded=RollingReplayBuffer.Encode(impact);
            Require(!helper.TryRestoreSnapshot(part,impact)&&part.hitPoints==-200&&encoded.SequenceEqual(RollingReplayBuffer.Encode(impact)),"live impact was applied, mutated or converted");
        });
        return passed;
        void Test(string title,Action action){action();passed++;Console.WriteLine("PASS damage snapshot "+title);}
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        PartDamageEvent Snapshot(float hp)=>new PartDamageEvent {Time=0,unitId=7,partId=2,hitPoints=hp};
        UnitPart Part(params Renderer[] renderers)=>new UnitPart {damageMaterial=new DamageMaterial {renderers=renderers.ToList()}};
        Renderer Render(int materialCount=1){var renderer=new Renderer();for(int n=0;n<materialCount;n++)renderer.Shared.Add(new Material());return renderer;}
    }
}
