using System;

namespace ReplayMod.Director;

// Saved with the director project, never injected into the replay or live world.
// Point and spot lights are the realtime additional-light types supported by NO.
[Serializable]
public sealed class StudioLightSettings
{
    public const int MaximumLights=16;
    public string id=Guid.NewGuid().ToString("N");
    public string name="Studio light";
    public bool enabled=true;
    public int type; // 0 point, 1 spot. Extra directional lights can replace the sun.
    public double x,y,z;
    public float qx,qy,qz,qw=1;
    public float r=1,g=1,b=1,intensity=3,range=100;
    public float spotOuter=45,spotInner=25;
    public int beamProfile; // 0 native round, 1 ellipse, 2 rectangle, 3 window, 4 slats.
    public float beamWidth=1,beamHeight=1,beamSoftness=.2f;
    public bool useTemperature;
    public float temperature=6500;
    public int shadowMode; // 0 off, 1 hard, 2 soft; effective only if native pipeline supports it.
    public float shadowStrength=1,shadowNearPlane=.2f;

    public bool Valid()
    {
        if(id==null||id.Length!=32||name==null||name.Length>80)return false;
        foreach(char c in id)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))return false;
        foreach(char c in name)if(char.IsControl(c))return false;
        if(type<0||type>1||shadowMode<0||shadowMode>2||beamProfile<0||beamProfile>4)return false;
        return DirectorProject.Range(x,-1e8,1e8)&&DirectorProject.Range(y,-1e8,1e8)&&DirectorProject.Range(z,-1e8,1e8)&&
            DirectorProject.Finite(qx)&&DirectorProject.Finite(qy)&&DirectorProject.Finite(qz)&&DirectorProject.Finite(qw)&&
            DirectorProject.Range((double)qx*qx+(double)qy*qy+(double)qz*qz+(double)qw*qw,.9,1.1)&&
            DirectorProject.Range(r,0,1)&&DirectorProject.Range(g,0,1)&&DirectorProject.Range(b,0,1)&&
            DirectorProject.Range(intensity,0,1000)&&DirectorProject.Range(range,.1,5000)&&
            DirectorProject.Range(spotOuter,1,179)&&DirectorProject.Range(spotInner,0,spotOuter)&&
            DirectorProject.Range(beamWidth,.1,1)&&DirectorProject.Range(beamHeight,.1,1)&&DirectorProject.Range(beamSoftness,0,1)&&
            DirectorProject.Range(temperature,1000,20000)&&DirectorProject.Range(shadowStrength,0,1)&&DirectorProject.Range(shadowNearPlane,.01,10);
    }
    public StudioLightSettings Duplicate()
    {
        var copy=(StudioLightSettings)MemberwiseClone();copy.id=Guid.NewGuid().ToString("N");
        copy.name=(name??"Studio light").Length>73?(name??"Studio light").Substring(0,73)+" copy":(name??"Studio light")+" copy";
        return copy;
    }
}
