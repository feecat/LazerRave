# osu!lazer source for LazerRave

This directory contains the reduced client snapshot from `ppy/osu`, release `2026.1005.0-lazer`. It retains `osu.Game`, the Mania ruleset, reusable client components and the beatmap editor foundation. BMS gameplay runs in the separate OpenLR2 engine.

The application entry point is `../LazerRave/lazer/`. Build from the repository root with `build.cmd`; generated outputs remain under `out/`.

See [provenance and local changes](LAZERRAVE-UPSTREAM.md), [MIT licence](LICENCE) and the [upstream repository](https://github.com/ppy/osu).
