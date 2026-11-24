using BepInEx.Configuration;
using UnityEngine;

namespace ReplayMod;

public static class KeyboardShortcutExtensions
{
	public static bool GetDown(this KeyboardShortcut key) => key.IsDown();
}
