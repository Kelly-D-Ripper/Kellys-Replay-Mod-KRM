using ReplayMod.UI;
using UnityEngine;

namespace ReplayMod;

// Keeps the original integration surface while the suite uses the game's uGUI input path.
public sealed class MainMenu : MonoBehaviour
{
    private TrailerSuiteUi suite;
    public static bool IsOpen => TrailerSuiteUi.Instance?.IsVisible == true;
    public static bool IsPointerOverUI => TrailerSuiteUi.Instance?.PointerOverUi == true || ReplayMod.Director.ReplayDirector.i?.StudioLightPointerCaptured == true;
    public static bool HasInputFocus => TrailerSuiteUi.Instance?.HasInputFocus == true || ReplayMod.Director.ReplayDirector.i?.StudioLightDragging == true;
    public static bool BlockHotkeys => HasInputFocus;
    public static bool CanToggleStudio => TrailerSuiteUi.Instance?.CanUseStudioShortcut == true;
    private void Awake() { suite = gameObject.AddComponent<TrailerSuiteUi>(); }
    public void Toggle() { suite?.Toggle(); }
    public static void OpenCameraTools() { TrailerSuiteUi.Instance?.OpenCameraTools(); }
    public void DrawEditControlMenu() { OpenCameraTools(); }
    public void DrawJointEditMenu() { OpenCameraTools(); }
}
