using System;
using System.Runtime.InteropServices;

// Isolated test only: mute this process's Windows audio session, never the device or another process.
public static class OwnAudioSession
{
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumeratorClass { }
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow,int state,out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out IDevice device);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid,int context,IntPtr activation,[MarshalAs(UnmanagedType.Interface)]out object instance);
    }
    [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(ref Guid id,int flags,out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid id,int flags,out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out ISessionEnumerator enumerator);
    }
    [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index,out ISessionControl control);
    }
    [ComImport,Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessionControl { }
    [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name,ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path,ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping,ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint id);
    }
    [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleVolume
    {
        [PreserveSig] int SetMasterVolume(float value,ref Guid context);
        [PreserveSig] int GetMasterVolume(out float value);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)]bool muted,ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)]out bool muted);
    }
    public static bool Mute()
    {
        var enumerator=(IDeviceEnumerator)new DeviceEnumeratorClass();IDevice device;
        if(enumerator.GetDefaultAudioEndpoint(0,0,out device)!=0)return false;
        Guid iid=typeof(ISessionManager2).GUID;object raw;if(device.Activate(ref iid,23,IntPtr.Zero,out raw)!=0)return false;
        var manager=(ISessionManager2)raw;ISessionEnumerator sessions;if(manager.GetSessionEnumerator(out sessions)!=0)return false;
        int count;if(sessions.GetCount(out count)!=0)return false;uint own=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        for(int i=0;i<count;i++)
        {
            ISessionControl session;if(sessions.GetSession(i,out session)!=0||session==null)continue;
            var info=session as ISessionControl2;uint pid;if(info==null||info.GetProcessId(out pid)!=0||pid!=own)continue;
            var volume=session as ISimpleVolume;if(volume==null)continue;
            Guid context=Guid.Empty;bool muted;if(volume.SetMute(true,ref context)!=0||volume.GetMute(out muted)!=0||!muted)throw new Exception("Could not verify isolated process audio mute");
            return true;
        }
        return false;
    }
}
