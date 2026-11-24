# Kelly's Replay Mod 1.0

Record Nuclear Option missions and multiplayer sessions, then arrange trailer shots with replay controls, free and tracking cameras, paths, slow motion, weather and lighting tools. Capture the finished replay with OBS or another screen recorder.

Original [ReplayMod](https://github.com/chiester3105/ReplayMod) by **chiester3105**. Continued by **Brig. Gen. Kelly D. Ripper**, with the original author's permission.
Community and support: Kelly D Rippers Mods on Discord, https://discord.gg/FhsqkbgtWS

## Install or update

Requires Nuclear Option for Windows and BepInEx 5.x (64-bit). Close the game. Copy the package's `BepInEx` folder into the game folder, so the plugin is at `BepInEx/plugins/ReplayMod/Kellysreplaymod.dll`.

Before installing, remove the previous `ReplayMod.dll` or older `Kellysreplaymod.dll` from `BepInEx/plugins`. Keep only one copy of the mod; the renamed DLL does not automatically replace the old filename. Keep your BepInEx configuration, Replays folder and camera-project sidecars. The mod is for a game client; do not install it on a headless server. Camera projects use the Newtonsoft.Json assembly already supplied with Nuclear Option.

For testers upgrading from 2.16.4.3: **1.0 is the public release name**, despite its lower version number. Replace the DLL manually; an updater may regard the old tester number as newer. New recordings use format 12, which older ReplayMod builds cannot open. This version still reads supported older recordings, but can only show data their recording version captured. There is no Ghost Flight feature in this release.

## First recording and shot

1. Press **F5** to open Kelly's Replay Mod. Open **Record**, start recording your mission, then stop and save before leaving it.
2. Open **Library**, select the recording and choose Open selected replay. The mod checks the file and required terrain and units first; if those checks pass, opening it ends the current mission and starts playback offline.
3. Use the timeline, Play/Pause and speed controls to choose a moment. In **Camera → View**, select a subject and view. Free camera uses the game's movement controls (R/F rise and descend); lower Movement speed for close shots.
4. In **Camera → Path**, compose a view and Add point here. Seek to the next desired time, compose another view and add a point. Each point stores position, aim, roll and FOV. Preview starts at the first point.
5. Press **F8** to hide all UI for capture; press it again to restore it. Use OBS to record video with game audio.

If F5/F8 were changed in an existing configuration, those bindings are retained. Find and change them in **Help → Shortcuts**. F9 saves the recent gameplay buffer when Keep recent gameplay is enabled; the buffer is optional and retains 30 seconds to 5 minutes subject to its memory limit. A save can briefly wait for an aircraft to finish initializing; if it cannot finish, the UI reports that the clip was not saved. Space toggles replay pause; comma selects slow motion; Z/C roll and X levels the camera. Game bindings and other mods can conflict, so reassign shortcuts if needed.

## Edit points confidently

Camera → Path → Path options & point times → Edit / delete points opens the point list. Clicking a point pauses at its saved time and restores its exact camera position, aim, roll and FOV. Adjust the camera, then Add point here at the same timestamp to update it. Attached points resolve relative to their subject at that moment.

Each point has its own confirmed delete button. Previous/Next points navigate longer paths in pages of 25. The advanced manual-key menu uses the same restore/delete behavior. Use Constant, Smooth or Jump to choose the transition to the next point. Changes to a point or its neighbours can change the curve; preview after editing.

## Camera and light tools

- Copilot and other supported crew seats: Camera → Cockpit. Cockpit-eye views hide crew meshes to avoid faces clipping into the camera. Availability depends on the aircraft model; copilot first-person ejection is not supported.
- Two-Aircraft Framing: select two aircraft to keep them framed. It adjusts camera distance as they separate. Existing replay files do not store selected-target history, so the second subject is chosen manually. Terrain can obscure subjects and extreme separation can exceed render distance.
- Fixed-Angle Follow: hold a world angle or rotate with the aircraft. You can lock a view composed in Free camera.
- Rails: steady camera movement in a chosen direction. Camera through terrain and objects bypasses native free-camera, studio tracking and rail collision checks when enabled. Two-aircraft framing has separate fitting behavior.
- Lights → Aircraft: independently set navigation and landing lights to Recorded/On/Off. Aircraft blackout keeps custom studio lights on.
- Lights: place, colour, aim and move custom point/spot lights. Scene controls replay weather/time. Save project retains camera and scene choices beside the replay.
- Settings: adjust UI colours, motion smoothing and playback options. High time-lapse rates are experimental and may run below the requested rate under load; they are not a promise of identical effects/audio at 480x.

## Feedback and files

Help → Support → Export logs creates a ZIP for bug reports. Include the action that failed, mod version, game version and whether the recording came from a host or client. Supply the replay and its `.director.json` sidecar when possible. The support ZIP can contain player/mod names and file paths; check it before posting publicly.

Recordings and their sidecars live under `Replays` in the game folder. Library deletion moves recordings to `Replays/Deleted recordings` for recovery. No database changes are required. Configuration is retained; camera projects store the new settings in sidecars. Existing saved `.flight` files are ignored and are not removed.

The build was checked against the locally installed game assemblies using native API checks, regression tests and an isolated Direct3D11 session. Compatibility with every other mod, aircraft or multiplayer setup is not guaranteed. See the accompanying release notes for known limitations.

## Recording reliability

The mod attempts to finalize recordings when a mission ends and checks new recordings before adding them to the library. Use Library → Recovery to recover a copy of an interrupted recording; the original partial is kept. Checksums detect damaged frames in supported newer recordings, but cannot restore gameplay data that was never captured or guarantee recovery from every failure. Keep backups of important recordings.

Known limitations: some foliage flicker and audio or visual issues may still occur depending on scene and hardware. Recordings made before format 12 did not capture missiles, rockets and bombs leaving most aircraft, so in those replays fired stores stay visible on the aircraft. Some placed objects have been reported with the wrong rotation in replay; this is under investigation. One replay-indexing crash was observed in isolated testing; the relevant code was revised and passed repeated stress tests, but its exact cause has not been confirmed. For a report, export a support log and retain the replay and camera project if available.
