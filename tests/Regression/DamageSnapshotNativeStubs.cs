// External Unity/game boundaries for production ReplayPartHealthPresentation.
// These stubs deliberately fail native damage/material-instancing calls.
namespace UnityEngine
{
    public static class Shader
    {
        private static readonly Dictionary<string,int> ids=new();
        public static int PropertyToID(string name)
        {if(!ids.TryGetValue(name,out int id))ids[name]=id=ids.Count+1;return id;}
    }
    public sealed class Material
    {
        public readonly Dictionary<int,float> Floats=new();
        public void SetFloat(int key,float value)=>throw new Exception("Snapshot must not mutate or instantiate materials");
    }
    public sealed class MaterialPropertyBlock
    {
        public static int Created;
        internal readonly Dictionary<int,float> Floats=new();
        public MaterialPropertyBlock(){Created++;}
        public bool isEmpty=>Floats.Count==0;
        public void Clear()=>Floats.Clear();
        public void SetFloat(int key,float value)=>Floats[key]=value;
        public float GetFloat(int key)=>Floats.TryGetValue(key,out var value)?value:0;
    }
    public sealed class Renderer
    {
        public readonly Dictionary<int,float> Wide=new();
        public readonly Dictionary<int,Dictionary<int,float>> Slots=new();
        public readonly List<Material> Shared=new();
        public bool ThrowRead,ThrowWrite;
        public int Reads,Writes;
        public Material material=>throw new Exception("Snapshot must not instantiate material");
        public Material[] materials=>throw new Exception("Snapshot must not instantiate materials");
        public void GetSharedMaterials(List<Material> target){target.Clear();target.AddRange(Shared);}
        public void GetPropertyBlock(MaterialPropertyBlock block){Read(block,Wide);}
        public void GetPropertyBlock(MaterialPropertyBlock block,int index){Read(block,Slots.TryGetValue(index,out var values)?values:null);}
        private void Read(MaterialPropertyBlock block,Dictionary<int,float> source)
        {
            Reads++;if(ThrowRead)throw new Exception("native renderer read unavailable");block.Clear();
            if(source!=null)foreach(var pair in source)block.Floats[pair.Key]=pair.Value;
        }
        public void SetPropertyBlock(MaterialPropertyBlock block){Write(block,Wide);}
        public void SetPropertyBlock(MaterialPropertyBlock block,int index)
        {
            if(!Slots.TryGetValue(index,out var values))Slots[index]=values=new();Write(block,values);
        }
        private void Write(MaterialPropertyBlock block,Dictionary<int,float> target)
        {
            Writes++;if(ThrowWrite)throw new Exception("native renderer write unavailable");target.Clear();foreach(var pair in block.Floats)target[pair.Key]=pair.Value;
        }
    }
}
public sealed class DamageMaterial {public List<UnityEngine.Renderer> renderers=new();}
public sealed class UnitPart
{
    public float hitPoints=100;
    public DamageMaterial damageMaterial;
    public void ApplyDamage(float pierce,float blast,float fire,float impact)=>throw new Exception("Snapshot must not call live ApplyDamage");
    public void InvokeDamage(float pierce,float blast,float fire,float impact)=>throw new Exception("Snapshot must not invoke damage callbacks");
    public void SpawnFragments()=>throw new Exception("Snapshot must not spawn historic fragments");
}
