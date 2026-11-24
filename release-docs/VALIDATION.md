# Kelly's Replay Mod 1.0 validation

Plugin file: `Kellysreplaymod.dll`
Plugin SHA-256: `071335369FDFDF6CEC2B0131CEDD5B99AA905A0E1B76E7AAF08CE744D5227462`

This build preserves the prior plugin ID and internal assembly identity. It passed 464 regression checks and 435 native API/integration contracts, with all 163 Harmony targets resolved against the installed game. New recordings use format 12. The build maps its source path, so the DLL does not contain local folder or user names, and the source archive rebuilds to the same plugin hash.

Isolated game sessions on this build passed the public release checks (menus, playback, seeking, camera paths, lighting, copilot, framing, ship and cargo fixtures, day/night and recording while paused) and the DOORMAN metadata check, which found one enabled entry with its Discord link. The preceding build, which differed only in that link and one status message, passed a studio keyboard-input check across all menus and a mixed-weapon test: six aircraft fired every weapon station natively, and the replay matched the live mounted-store state at all 37 checkpoints, including bay drops and rail launches. The recording changes were also checked with native cargo, editor and ship recordings and the native recording edge-case batch.

Missile flight and impact presentation no longer depends on the game's own seeker logic, so a missile explodes only where and when the recording says. Recordings from earlier versions do not contain mounted-store state; fired stores remain visible in those replays.

A replay-indexing crash occurred once in an earlier isolated test; the relevant decoder was revised and passed repeat stress tests, but the exact underlying cause is not established. Foliage flicker, placed objects reported with the wrong rotation, independent multiplayer and broad visual/audio acceptance remain open. Checksums cannot guarantee recovery from every failure.
