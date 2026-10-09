# LazerRave upstream source

- Repository: https://github.com/ppy/osu
- Release: `2026.1005.0-lazer`
- Commit: `9a10d935b05a3b896f4bd811d620f2023331db93`
- Imported: 2026-10-08
- Source archive: https://api.github.com/repos/ppy/osu/zipball/2026.1005.0-lazer
- Licence: MIT, see `LICENCE`.

This directory is a reduced upstream source snapshot without a nested Git repository. The Windows application in `../LazerRave/lazer/` subclasses `OsuGame`, retaining its main menu, song carousel, toolbar, settings and profile components. Mania supplies the metadata and ruleset interfaces used by the BMS adapter. OpenLR2 performs BMS gameplay in a separate native process.

## Removed modules

The following sources are archived outside the repository and are not compiled:

- Osu, Taiko and Catch ruleset projects, including their editors.
- Upstream desktop/mobile entry points, test, benchmark and tournament projects, unused solution files and IDE/CI configuration.
- Startup and outro animations, the first-run setup wizard and core test helpers.
- Beatmap submission and skin-layout editing interfaces.
- Matchmaking, ranked-play, daily-challenge and playlist-specific screens.
- Stable-install import services, tablet and pen settings, and legacy/lazer beatmap decoders.

Menu entries and setup actions referring to removed screens are disconnected or removed. The shared beatmap editor foundation and Mania editing tools were restored on 2026-10-09, including the timeline, undo/redo, setup and verification infrastructure. Editor key bindings are retained, excluding submission. No editor entry is exposed in LazerRave until BMS model conversion and safe persistence are implemented; BMS editing and write-back are not currently supported. Existing account/profile components, multiplayer-room contracts, score/replay components, ordinary input, display settings, frontend skinning and shared model serialization remain. They provide reusable infrastructure and do not imply that LazerRave account, multiplayer or replay services are implemented. Some upstream protocol models remain dependencies of the shared multiplayer client; the removed matchmaking UI is not restored by those models.

## Local integration changes

1. `BeatmapManager` provides an external working-beatmap resolver. External BMS metadata resolves before database-backed beatmaps, without falling back to imported osu! charts.
2. `SongSelect`, `SoloSongSelect` and `PanelBeatmapSet` support external folder/key filtering and read-only BMS metadata. Unsupported edit, delete and collection writes are not exposed for BMS.
3. `FooterButtonOptions` supports external beatmaps absent from Realm while hiding unsupported deletion actions.
4. `OsuGame`, `Loader` and `MainMenu` expose settings, main-menu and solo-selection factories. Loading enters the menu directly after shader compilation; confirmed exit does not play the outro.
5. `Directory.Build.props` relocates build intermediates and outputs to `out/build/upstream/`.
6. `RulesetStore` and `RealmRulesetStore` exclude osu!, taiko and catch, including stale assemblies and persisted ruleset entries. Player records are retained. The build script removes obsolete Osu/Taiko/Catch artifacts from the active frontend output and package.
7. `OsuGameBase`, importers and `Decoder` do not register osu! beatmap or replay import. Beatmap export is unsupported. Shared skin and model serialization remain where required by retained UI and storage.
8. Editor composer, verifier and setup factories are retained for future BMS integration. Shared multiplayer result screens now reside under `Screens/OnlinePlay/Match`; beat-divisor colouring and repeating-button behaviour reside in common namespaces. Chat polling helpers used by the offline API provider were extracted from the archived core test directory.
9. Mouse settings no longer expose high-precision input or its sensitivity control. Application-level `LazerRaveInputPolicy` keeps relative mouse, tablet and pen handlers disabled when loading old configuration or resetting inputs. The framework NuGet package retains its device implementations; this source reduction does not fork it.
10. The headless application check verifies the BMS adapter, supported ruleset discovery, absence of retired client screen types, disabled osu! beatmap decoding and input policy. It does not launch the full UI or native gameplay.
11. Ruleset discovery compares assembly identities instead of file locations, and bundled assemblies without a physical path are excluded from custom-ruleset file quarantine. Dependency resolution matches complete simple assembly names, preventing a neutral assembly from being returned for an unresolved satellite resource. `OsuGameBase` derives its version fingerprint from the module identifier when its assembly is bundled. The application project publishes managed assemblies as a self-contained bundle and resolves external satellite resources under `Localization/`; third-party native libraries remain external and trimming is disabled.
12. Publication derives a reduced resource assembly from the pinned `ppy.osu.Game.Resources` 2026.918.0 package (upstream commit `d481bc98f2603348ee81c15174cd72604286310d`) without modifying the package cache or development assemblies. Retired-mode, startup, official matchmaking and daily-challenge assets are removed according to the application resource policy. Fonts, localization, common UI, editor assets and ordinary multiplayer-room assets remain. The resource assembly is published under `Resources/`; application dependency metadata records the new resource paths for the .NET 10 host. The original resource assembly identity and code interfaces are retained.

The saved intro-setting key remains readable for configuration compatibility. It no longer launches an intro. Legacy editor configuration identifiers remain where needed to avoid reinterpreting existing saved values; they do not expose a BMS editor.

## Archive and runtime boundary

Removed files and the original source archive are preserved outside the active repository in workstation cleanup archives. Migration and restoration records remain in ignored local output directories. Restore individual paths only after checking for newer replacements in the active tree.

The OpenLR2 engine, BMS files, LR2 skins and player records remain separate from osu! beatmap formats. Retaining Mania does not enable osu! chart import or convert BMS into Mania gameplay. The LazerRave executable uses its own entry point and an offline API provider; official online services are not connected. Upstream resources, fonts and their licences remain dependencies of the retained interface.

The profile-specific Kudosu and daily-challenge components, mapping subscription count and supporter marker were removed together with their dedicated API models. Unused osu! beatmap package patch/replacement and playlist-close requests are also removed. Generic authentication, uploads/downloads, rooms, scores, replay and leaderboard infrastructure remains for LazerRave service integration.
