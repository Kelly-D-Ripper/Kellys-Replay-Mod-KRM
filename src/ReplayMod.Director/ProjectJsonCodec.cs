using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace ReplayMod.Director;

// Unity's native JsonUtility omits custom nested types from this runtime-loaded
// plugin assembly. Use the game's JSON library for the whole project instead.
public static class ProjectJsonCodec
{
    public const int MaximumBytes = 2*1024*1024;
    private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        MaxDepth = 32,
        Culture = CultureInfo.InvariantCulture
    };
    public static string Write(DirectorProject project)
    {
        if(project==null)throw new ArgumentNullException(nameof(project));
        string json=JsonConvert.SerializeObject(project,Formatting.Indented,Settings);
        // Allow room for an optional UTF-8 BOM so a saved project always fits
        // the same limit enforced when it is opened again.
        if(Encoding.UTF8.GetByteCount(json)>MaximumBytes-3)throw new InvalidDataException("Project is too large to save (maximum 2 MiB)");
        return json;
    }
    public static DirectorProject Read(string json)
    {
        if(json==null)throw new ArgumentNullException(nameof(json));
        var project=new DirectorProject();
        JsonConvert.PopulateObject(json,project,Settings);
        return project;
    }
}
