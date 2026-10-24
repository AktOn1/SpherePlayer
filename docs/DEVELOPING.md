# Developing Sphere Player

Notes for people who want to build the plugin or rebuild the song library. Players do not need any of this.

## Omakase mode in detail

Every area song has place tags (forest, desert, seaside, city ...), and every territory has tags too (`library/area.json`, `plugin/SpherePlayer/territories.json`). The song that fits the territory best is picked. It works per territory, not per sub-area (Costa del Sol is part of Lower La Noscea). A track named after an FFXIV place (the Kugane theme) is tied to it (`home`, from `assign-homes.py`): it is played only there, and always counts as a perfect fit there. If fewer than 6 songs fit, the pool widens to all area songs so one song does not repeat.

On top of the 19 coarse place tags, songs and zones also have finer place tags (`sub`: harbor, market, palace, tunnels ...) and one or two moods (`mood`: calm, cheerful, lively, melancholy, mysterious, tense, epic). The score is coarse tags x2, plus 2 per shared finer tag (max 4), plus the mood weight of the song (zone moods 3 and 2); a song whose moods clash with the zone loses 2. The Eorzea clock and the zone's weather (`Ambience.cs`) shift the weights by one: at night calm, mysterious and melancholy gain, lively and cheerful lose; rain and fog favour calm, melancholy and mysterious; storms favour tense and epic; clear daytime favours cheerful and calm. They only reorder songs that already fit the place, and take effect when the next song is picked, never mid-song. The labels come from `tools/label-moods.py` (the model judges from titles and place names, it cannot hear the audio, so some are off); `simulate-pools.py [--night] [--weather Rain]` mirrors the scoring.

## Song library (full original soundtracks)

The library is the original soundtrack of Final Fantasy I to XVI, 2169 tracks, all checked for embedding. Sources are official SQUARE ENIX MUSIC "Video Soundtrack" playlists (I to X, XV, XVI) and YouTube Music auto-generated ("Topic") album playlists where Square Enix has no list (XI to XIV; the XIV list is the 1.0 to 7.0 release playlist). Each track is matched from its title to battle, boss, town, field, dungeon or other, with place tags and an English title. The matching went through a second check and a correction pass (`audit-ost.py`, `arbitrate-ost.py`), and it is still work in progress: some labels are off. "Other" (cutscene, fanfare, character scenes) is never played automatically, only from the song window. The old hand-picked list is in `library/legacy/`.

Rebuild: `tools/fetch-ost.py` (track lists + embed check, playlists are listed at the top), `tools/classify-ost.py` (needs the `claude` CLI), `tools/build-ost-library.py` (writes `library/songs.json` + `area.json`). Add a game or album by adding its playlist id to `SOURCES` and running the three steps. The song window has a game filter, a search box and pages of 40.

After classify: `audit-ost.py`, `arbitrate-ost.py`, `assign-homes.py`, `retag-sparse.py` (volcano, swamp, jungle, snow, crystal), then `build-ost-library.py`. Check the zone pools with `simulate-pools.py` (mirrors the plugin scoring) and `rate-pools.py` (LLM rating per zone, 1 to 5). Some steps call the model and take minutes.

Regenerate other data: `tools/build-territories.py` (territories), `tools/resolve-area.py` (resolves `tools/area-tracklist.json` to YouTube ids, metadata only).

## Building and releasing

Needs the .NET 10 SDK and a Dalamud build (default `~/.xlcore/dalamud/Hooks/dev`, or set `DALAMUD_HOME`).

- `./build.sh` builds the plugin and the player host.
- `tests/run.sh` runs the logic tests (installer download, checksum, retries, video rules). `tests/run.sh installer` runs just the installer tests, which also work under Wine.
- Player host: `tools/pack-host.sh OWNER/REPO` zips the host, writes its SHA-256 into `host-manifest.json` (embedded in the plugin), and prints the release to create (`host-<version>` with the zip as asset). Do this only when the player changes.
- Plugin: `tools/release.sh OWNER/REPO 1.0.1 "what changed"` builds `dist/latest.zip` and rewrites `pluginmaster.json`. Upload the zip to the GitHub release `v1.0.1` (pushing the tag does this through `.github/workflows/release.yml`) and commit `pluginmaster.json`. The Settings install command for the Linux media-key helper downloads `tools/mpris-bridge/` from the tag `v<version>` and checks the SHA-256 that the build embeds, so create the tag on the commit the plugin was built from (an edited helper without a rebuild fails the check).

