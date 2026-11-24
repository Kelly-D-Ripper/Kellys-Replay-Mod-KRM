using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ReplayMod.Core;
using ReplayMod.Director;
using NuclearOption.Networking;

public static class MemoryRootsProbe
{
    sealed class RefComparer:IEqualityComparer<object>{public new bool Equals(object x,object y)=>ReferenceEquals(x,y);public int GetHashCode(object o)=>RuntimeHelpers.GetHashCode(o);}
    struct Item {internal object value;internal string path;internal int depth;}
    static bool SearchStruct(Type type)=>type.IsValueType&&!type.IsPrimitive&&!type.IsEnum&&
        (typeof(IAsyncStateMachine).IsAssignableFrom(type)||type.Name.Contains("Awaiter")||type.FullName.Contains("UniTask"));
    public static void Find(ReplayMod.Core.Player player,object target,Action<string> log)
    {
        var queue=new Queue<Item>();var visited=new HashSet<object>(new RefComparer());
        void Add(object value,string path,int depth)
        {
            if(value==null)return;var type=value.GetType();if((type.IsValueType&&!SearchStruct(type))||value is string||value is Type||value is MemberInfo)return;
            if(visited.Add(value))queue.Enqueue(new Item{value=value,path=path,depth=depth});
        }
        Add(player,"player",0);Add(ReplayManager.i,"replayManager",0);Add(ReplayDirector.i,"director",0);
        Add(NetworkManagerNuclearOption.i,"networkManager",0);Add(GameManager.SceneSingletons,"sceneSingletons",0);Add(GameManager._onGameStateChanged,"gameStateChanged",0);
        foreach(var pending in SeekPendingWorkProbe.LiveWork())Add(pending,"pendingWork["+pending.GetType().FullName+"]",0);
        foreach(var type in new[]{typeof(ReplayVfxPlayback),typeof(ReplayCloudParticleClock),typeof(ReplayInteriorPresentation),typeof(UnitRegistry),typeof(GameManager),typeof(Cysharp.Threading.Tasks.PlayerLoopHelper),typeof(Cysharp.Threading.Tasks.UniTaskSynchronizationContext)})
            foreach(var f in type.GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
                if(!f.IsLiteral&&!f.FieldType.IsValueType)try{Add(f.GetValue(null),type.Name+"."+f.Name,0);}catch{}
        foreach(var assembly in new[]{typeof(ReplayMod.Core.Player).Assembly,typeof(Unit).Assembly,typeof(Mirage.NetworkIdentity).Assembly})
            foreach(var type in assembly.GetTypes())
            {
                if(type.ContainsGenericParameters||type.TypeInitializer!=null)continue;
                foreach(var f in type.GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    if(!f.IsLiteral&&!f.FieldType.IsValueType)try{Add(f.GetValue(null),type.FullName+"."+f.Name,0);}catch{}
            }
        int inspected=0;var watch=System.Diagnostics.Stopwatch.StartNew();
        while(queue.Count>0&&inspected<500000&&watch.Elapsed.TotalSeconds<15)
        {
            var item=queue.Dequeue();inspected++;
            if(ReferenceEquals(item.value,target)){log("MEMORY ROOT FOUND "+item.path+" examined="+inspected);return;}
            if(item.depth>=10)continue;
            if(item.value is Delegate del){foreach(var d in del.GetInvocationList())Add(d.Target,item.path+".delegate",item.depth+1);}
            if(item.value is Array array){int n=Math.Min(array.Length,20000);for(int i=0;i<n;i++)try{Add(array.GetValue(i),item.path+"["+i+"]",item.depth+1);}catch{}continue;}
            var type=item.value.GetType();
            if(type.Namespace!=null&&(type.Namespace.StartsWith("System.Reflection")||type.Namespace.StartsWith("System.RuntimeType")))continue;
            for(var t=type;t!=null&&t!=typeof(object);t=t.BaseType)
            {
                if(t==typeof(UnityEngine.Object))break;
                foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                {
                    if((f.FieldType.IsValueType&&!SearchStruct(f.FieldType))||f.FieldType.IsPointer)continue;
                    try{Add(f.GetValue(item.value),item.path+"."+f.Name+(SearchStruct(f.FieldType)?"["+f.FieldType.Name+"]":""),item.depth+1);}catch{}
                }
            }
        }
        log("MEMORY ROOT NOT FOUND examined="+inspected+" queued="+queue.Count+" elapsed="+watch.Elapsed.TotalSeconds.ToString("F1"));
    }
}
