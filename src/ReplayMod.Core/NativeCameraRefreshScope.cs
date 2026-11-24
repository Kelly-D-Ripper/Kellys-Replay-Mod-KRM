using System;

namespace ReplayMod.Core;

// Native slider.value invokes its user-edit callback even when GetValues is only
// refreshing the panel. Temporary seek/buffer clock holds must never become a
// requested replay pause. Scope nesting also covers re-entrant UI refreshes.
public static class NativeCameraRefreshScope
{
    [ThreadStatic] private static int depth;
    public static bool IsActive => depth > 0;
    public static void Enter() => depth++;
    public static void Exit() { if(depth>0)depth--; }
}
