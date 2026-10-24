# Sphere Player

A Dalamud plugin that plays music from the original Final Fantasy I to XVI soundtracks while you play FFXIV, and switches it by what you are doing: town, field, dungeon, battle, boss. It can mute the game's own music and take over. The music comes from YouTube, shown in a small video window inside the game.

**About the name:** Sphere Player is named after the spheres of Spira, the orbs that store and replay sound in Final Fantasy X.

This is the **first version** and it is still being worked on. **Feedback is very welcome**: bugs, wrong songs, ideas. Please open an issue on this repository.

Unofficial fan project. It is not made by or connected to Square Enix. FINAL FANTASY is a trademark of Square Enix.

<a href='https://ko-fi.com/akton1' target='_blank'><img height='36' style='border:0px;height:36px;' src='https://storage.ko-fi.com/cdn/kofi3.png?v=6' border='0' alt='Buy Me a Coffee at ko-fi.com' /></a>

## Status

- **Currently being tested.**
- **Work in progress:** songs are matched to battle, boss, town, field, dungeon or other, and to places, from their titles, so some will be off: a calm piece in the battle list, a forest tune for a desert. Boss detection looks at target and duty type, so some bosses will get normal battle music. You can ban or pin songs in the song list, and tell me which ones are wrong.
- A YouTube video must be available in your region. A video taken down by its owner is skipped.

## Install

1. In the game: `/xlsettings` > **Experimental** > **Custom Plugin Repositories**.
2. Add this URL, tick it, and save:
   `https://raw.githubusercontent.com/AktOn1/SpherePlayer/main/pluginmaster.json`
3. Open `/xlplugins`, search for **Sphere Player**, install.

### First start downloads the player

The plugin itself is tiny (0.5 MB). The player is a Chromium browser without a window (CefSharp, about 160 MB to download, 360 MB unpacked), too big to ship inside a plugin. On first start the music window shows a **Download now** button; nothing is downloaded until you click it. Then the plugin downloads it from this project's GitHub releases into its own settings folder (`pluginConfigs/SpherePlayer/host`), checks its SHA-256 against the value built into the plugin, and unpacks it. The window shows the progress. If the network drops it retries twice, then shows a **Try again** button. Nothing else is downloaded, and the download happens again only when a plugin update ships a newer player. To remove everything, uninstall the plugin and delete that folder.

## The music window and YouTube

YouTube lets sites embed its player on the condition that the video is **visible** while its audio plays. So:

- The music window **is open every time the plugin loads**.
- While the window is open the video is never hidden. It has a minimum size of 200x200 (the smallest window here is 356x200) and the size is configurable up to 480 wide.
- **In a fight or boss fight** the window shrinks to that minimum. It does not hide.
- **By default the music keeps playing when you collapse or close the window**, like a browser tab you hide. If you would rather have it pause, turn off **Keep playing when the window is collapsed or closed** in the settings. Then collapsing, closing, or the game hiding its windows (cutscenes, GPose, the UI-hide key) pauses the music, and it continues by itself when the video is visible again.
- `/sphere` commands (`play`, `next`, `resume`, `playpause`, `songs` ...) always open and expand the window first.
- The plugin does not remove ads or alter the YouTube page. Clicks on the video go to YouTube's own player, so its buttons work.

## Using it

Chat command `/sphere` (alias `/sphereplayer`), also usable in macros. Without an argument it opens the music window.

| Command | Does |
|---|---|
| `settings` | Opens the settings window (mode, fade, resume, video size, opacity) |
| `songs` | Opens the song list (pin, ban, play, see tags) |
| `next` / `prev` / `playpause` / `stop` / `pause` / `resume` | Playback (`prev` plays the song before, up to 50 back) |
| `on` / `off` | Music on or off |
| `vol 40`, `vol +10`, `vol -10`, `mute` | Volume |
| `gamemusic`, `gamemusic on`, `gamemusic off` | Mute the game's own music while Sphere Player plays (also a checkbox in the window) |
| `mode omakase`, `mode random`, `mode` (toggle) | Omakase (default) or random |
| `fade 3` | Fade seconds between area and battle music |
| `ban`, `pin`, `unpin`, `now` | Act on the song that is playing |
| `help` | Lists all of this in chat |

Omakase mode (the default) picks the song that fits the place you stand in: forest, desert, seaside, city and so on. Random mode picks any song of the right kind. Music also plays on the main menu and character select, and a song for the place takes over once you enter the game. Stop ends the song and stays quiet until Next or Resume; Pause keeps the place in the song.

Timing: a new zone starts its music at once, calm music after a fight waits 4 s, battle music 0.3 s. Battle music fades in over the area music, and after the fight the area song continues where it stopped (switch off in settings).

## Privacy and what it touches

- The plugin **only reads game state** that Dalamud offers (zone, combat flag, target type, duty type) to choose the kind of music. It **sends nothing to the game servers** and does not change game data, packets or memory.
- The player talks to **YouTube** (to play the video) and to **GitHub** (first-start download). No account, no analytics, no data of mine.
- A control port listens on `127.0.0.1` only, so a helper on your own machine (the Linux media-key bridge below) can send `next`, `prev`, `playpause`, `play`, `pause`, `stop`. Programs on your machine can use it too. The port number is in `pluginConfigs/SpherePlayer/control.port`.
- YouTube is embedded through its official IFrame player under its terms: video visible, no ad blocking, no downloading.

## Media keys, clickable video

- **Hardware keys (next / previous / play-pause / stop):** the plugin polls them itself (Windows, or Wine when the key reaches the game; setting "Hardware media keys"). On Linux the desktop usually swallows these keys, so `tools/mpris-bridge/` adds a small service: the bridge connects to the control port and registers an MPRIS player "Sphere Player" on the D-Bus session bus. Then the keys, `playerctl` and desktop media widgets control the plugin. It is optional and the plugin never installs it: on Linux, Settings shows its status and a "Copy install command" button; paste that into a terminal, or run `tools/mpris-bridge/install.sh` from a checkout (systemd user service, idle until the game plugin is up). It removes itself (service and unit file) once the plugin and the game folder are gone for about 30 minutes; to remove it by hand: `systemctl --user disable --now sphere-player-mpris`. The control port accepts one command per line and sends the status as JSON lines.
- **Video clicks:** mouse moves, clicks and the wheel over the video are passed to the player page, so click-to-pause, the seek bar and YouTube's own buttons work. Links open in the system browser only for YouTube addresses. Setting "Video is clickable". YouTube's own volume button is not tied to the plugin volume; the plugin volume wins on the next song.

## Song library

2169 tracks from the original soundtracks of Final Fantasy I to XVI, from official Square Enix Music and YouTube Music playlists, all checked for embedding. "Other" tracks (cutscenes, fanfares, character scenes) are never played automatically, only from the song window, which has a game filter, a search box and pages of 40.

## Building it yourself

See [docs/DEVELOPING.md](docs/DEVELOPING.md) for building the plugin, releasing, and rebuilding the song library.

## Third-party software

The downloaded player contains [CefSharp](https://github.com/cefsharp/CefSharp) (BSD-3-Clause) and the Chromium Embedded Framework / Chromium (BSD-style, with further components under their own licences). Their licence texts are in the `licenses` folder of the downloaded player. The design of an out-of-process Chromium window for Dalamud is inspired by [Browsingway](https://github.com/Styr1x/Browsingway).

## Support

Free and MIT licensed. If it is useful to you and you want to say thanks, you can [buy me a coffee on Ko-fi](https://ko-fi.com/akton1). Totally optional.
