# Changelog

## 1.0.0

First public release of Kelly's Replay Mod, continuing the original ReplayMod by chiester3105.

### Added
- Per-replay camera projects: paths with position, aim, roll and FOV; exact point restore and individual point deletion.
- Recent-gameplay buffer, recording integrity checks, interrupted-recording recovery and recoverable bulk deletion.
- Copilot and crew-seat views, two-aircraft framing, fixed-angle follow, camera rails and camera collision controls.
- Kelly's DOORMAN can check this repository for updates.
- Aircraft light controls, custom studio lights, replay weather and time of day, and adjustable UI colours.
- Mounted missiles, rockets and bombs are recorded per aircraft part, including bay drops and rail launches (replay format 12).
- Fresh recordings capture more turret, cargo, parachute and engine state.

### Fixed
- Map buildings such as revetments, bunkers and hangars keep their real orientation in replays, including recordings made with earlier 1.0 builds.
- Missiles no longer explode early, or differently after each rewind; replayed explosions come only from the recording.
- Space plays and pauses the replay while the game's camera panel is open.
- Studio sliders no longer change when flying the camera with A/D or the arrow keys, and Space no longer repeats the last clicked button.

### Known limitations
- Recordings made before format 12 do not contain mounted-store state, so fired stores stay visible in those replays.
- Under investigation: foliage flicker and an isolated replay-indexing crash.
