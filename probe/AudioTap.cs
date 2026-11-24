using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Test-only tap; it observes audio before the muted listener and leaves data unchanged.
public sealed class AudioTap:MonoBehaviour
{
    readonly object gate=new object();readonly List<float> data=new List<float>();
    int channels;const int Limit=48000*2*12;
    public int CapturedFrames {get {lock(gate)return channels>0?data.Count/channels:0;}}
    void OnAudioFilterRead(float[] buffer,int count)
    {
        lock(gate)
        {
            if(channels==0)channels=count;
            if(channels!=count)return;
            int remaining=Math.Min(buffer.Length,Limit-data.Count);
            for(int i=0;i<remaining;i++)data.Add(buffer[i]);
        }
    }
    public int Save(string name)
    {
        float[] snapshot;int ch;lock(gate){snapshot=data.ToArray();ch=channels;}
        if(ch<1||snapshot.Length==0)return 0;
        string tag=Environment.GetEnvironmentVariable("REPLAY_AUDIO_TAG");
        if(!string.IsNullOrEmpty(tag))
        {
            foreach(char c in tag)if(!char.IsLetterOrDigit(c)&&c!='-'&&c!='_')throw new ArgumentException("Invalid audio test tag");
            name=tag+"-"+name;
        }
        string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe",name);
        using(var output=new FileStream(file,string.IsNullOrEmpty(tag)?FileMode.Create:FileMode.CreateNew,FileAccess.Write))using(var writer=new BinaryWriter(output))
        {
            int sampleRate=AudioSettings.outputSampleRate,bytes=snapshot.Length*2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+bytes);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);writer.Write((short)1);writer.Write((short)ch);writer.Write(sampleRate);writer.Write(sampleRate*ch*2);writer.Write((short)(ch*2));writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(bytes);
            foreach(float sample in snapshot)writer.Write((short)(Mathf.Clamp(sample,-1,1)*32767));
        }
        return snapshot.Length;
    }
    public static void SaveClipBeginning(string tag,string name,float[] snapshot,int sampleRate,int channels)
    {
        if(string.IsNullOrEmpty(tag)||snapshot==null||snapshot.Length==0||sampleRate<=0||channels<=0)throw new ArgumentException("Invalid clip evidence");
        foreach(char c in tag)if(!char.IsLetterOrDigit(c)&&c!='-'&&c!='_')throw new ArgumentException("Invalid audio test tag");
        string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe",tag+"-"+name);
        using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write))using(var writer=new BinaryWriter(output))
        {
            int bytes=snapshot.Length*2;writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+bytes);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);writer.Write((short)1);writer.Write((short)channels);writer.Write(sampleRate);writer.Write(sampleRate*channels*2);writer.Write((short)(channels*2));writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(bytes);foreach(float sample in snapshot)writer.Write((short)(Mathf.Clamp(sample,-1,1)*32767));
        }
    }
}
