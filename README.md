# HeadTrack: the screen as a window into your city

A code mod for **Cities: Skylines II** (1.6.2) that moves the camera with your
head. The screen becomes a window: lean to the right and nearby towers slide
left against the ground, the far hills move with you, and with a higher gain
you can look around a building. The effect is a head-coupled perspective (an
off-axis "Kooima" projection), so what lies in the window plane — the ground
where the camera aims — stays put, and the mouse still picks exactly what you
see.

Head poses come from [OpenTrack](https://github.com/opentrack/opentrack)
over UDP, so any tracker OpenTrack supports works; it was built and tested with
a Tobii Eye Tracker 5 through OpenTrack's Tobii tracker, with the game under
Wine on Linux.

## Install

You need the game and the .NET 10 SDK. The mod is built against the game's own
assemblies, so it is built from source on the machine that has the game.

```sh
dotnet build src/HeadTrack -t:Deploy
```

builds the mod and copies `HeadTrack.dll`, `HeadTrack.Core.dll` (each with its
`.pdb`) and `0Harmony.dll` into `<user data>/Mods/HeadTrack/`, where the game
loads local mods. No launcher or developer mode is needed; the mod logs to
`<user data>/Logs/HeadTrack.log`.

The build looks for the game and its user data in the usual places:

| | Game folder (`Cs2GameDir`) | User data (`Cs2UserDataDir`) |
|---|---|---|
| Windows, Steam | `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II` | `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II` |
| Linux, Steam (Proton) | `~/.local/share/Steam/steamapps/common/Cities Skylines II` | `~/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II` |
| Linux, Wine | where you installed it (name it, below) | `~/.wine/drive_c/users/$USER/AppData/LocalLow/Colossal Order/Cities Skylines II` |

Anywhere else, name the folders: on the command line (`-p:Cs2GameDir=...`), as
environment variables of the same names, or once in a
`Directory.Build.local.props` next to `Directory.Build.props` (git ignores it):

```xml
<Project>
  <PropertyGroup>
    <Cs2GameDir>/path/to/Cities Skylines II</Cs2GameDir>
  </PropertyGroup>
</Project>
```

The game folder is the one with `Cities2.exe`. The user-data folder is created
by the game's first start; Deploy stops rather than create a missing one.

## Set up OpenTrack

1. **Input:** your tracker (for the Tobii Eye Tracker 5: *Tobii*).
2. **Output:** *UDP over network*. Open its settings (the button next to it)
   and set the address to **127.0.0.1** and the port to **4242**. OpenTrack's
   default address is 192.168.0.2, which the game never sees. The change
   applies at once.
3. **Filter:** none (the empty first entry). The mod smooths the poses with
   its own 1€ filter and learns the tracker's rate. OpenTrack's default,
   AccelaHamilton, adds lag of its own and turns a face found again into a
   glide the mod cannot tell from a head move (see *How it works*).
4. **Mapping:** keep the translation curves 1:1. The mod expects real
   centimetres; it ignores rotations.
5. Sit normally, facing the screen the game is on, and **centre**:
   OpenTrack's *Center* key, or the mod's own (Shift+H).

On Linux OpenTrack's hotkeys are X11 key grabs, so when it runs as a Wayland
window (the default in a Wayland session) they do nothing. Use the mod's keys,
or try `QT_QPA_PLATFORM=xcb opentrack`, which makes it an X11 window whose
grabs should see keys while another X11 window such as the game under Wine
has focus (not tested here). Under Wine the game's sockets are the host's, so
a native OpenTrack reaches it on 127.0.0.1.

The tracker should sit on the monitor that shows the game: the effect
measures your head against that screen.

## In the game

**Options → HeadTrack.** Every change applies at once and glides in over a
fraction of a second, so nothing jumps.

| Option | What it does |
|---|---|
| Head tracking | On or off. |
| Mode | *Centred* (default): your pose when centred gives the game's own view, and moving your head shifts it. *Window*: what a real window would show from where you sit. |
| Gain | How far the view moves with sideways and vertical head motion. 1 is physically right; 3 to 5 lets you look around a building. |
| Depth gain | The same for leaning towards and away from the screen; 0 ignores leaning. |
| Window depth | Where the window plane sits, as a fraction of the distance to the point the camera aims at. |
| Smoothing | More keeps the view steadier while your head is still; quick moves keep their speed. 0 turns it off. |
| Screen width, height | The picture's size on your monitor (597 × 336 mm for a 27" 16:9). |
| Viewing distance | Your eyes' distance from the screen when you sit normally. |
| Keys | Ctrl+H on/off, Shift+H centre, N hold (the view stays put while you move your head; it glides back on release). |
| When following a citizen or vehicle | The effect on the follow camera (on). |
| In photo mode | The effect on the photo-mode camera (off; screenshots then show the shifted view). |
| While a building tool is active | *Full*, *Gain at most 1* (default: the world under the cursor moves no more than a window would show) or *Off*. |
| Pose source | *OpenTrack* (centred by OpenTrack) or *Absolute* (a sender that gives the eye's position from the screen centre; the mod takes the first pose as the centre). |
| UDP port | 4242 unless OpenTrack sends elsewhere. |
| Invert axes | Flip a tracker axis. |

While you drag with a building tool (or hold a mouse button that started over
the world) the view holds still, so the world does not move under the cursor.

## How it works

Right after the game's own camera update, the mod moves the camera to where
your eye would be, scaled into the world, and gives it the off-axis frustum
through the window: the window is the screen, placed at the depth of the
point the camera aims at. Right before the next camera update it puts the
game's camera back, so the game's controllers, collisions and Cinemachine never
see the shift. Everything in between — culling, rendering, the next frame's
mouse ray — sees the head-coupled camera. The camera stays as high above
ground or water as the game keeps its own, a ray cast along the move keeps it
out of buildings, and its offset is limited softly.

HDRP's ambient occlusion is corrected for the off-axis view, and save
previews are rendered with the game's own camera. Moving your head does not
blur the picture: the game's motion blur already clamps the camera's
translation.

The poses go through a 1€ filter and are resampled to every frame. When they
stop coming (OpenTrack stopped, or a sender of your own that goes quiet while
the face is lost), the effect fades out, and it fades back in when they
return. Through OpenTrack the mod cannot see a lost face: OpenTrack sends a
pose every few milliseconds whatever its tracker reports.

- OpenTrack's Tobii tracker as built for Linux repeats the last pose it saw,
  so the view holds where your head was. When the face is found more than
  5 cm from there, the view fades out and back in on the new pose instead of
  sweeping there. With an OpenTrack filter on, the filter turns that jump into
  a glide, and the view follows the glide.
- OpenTrack's stock Tobii tracker reports a zero pose for a lost face: the
  view jumps as far towards the screen as the mod allows, for as long as the
  face is gone.

## Limitations

- The effect is off in photo mode by default, and HDRP's Panini projection
  (a photo-mode option) assumes a centred view.
- Level of detail and the punctual-light list follow the shifted camera; a save
  preview, rendered with the game's own camera, can miss a light at its edge.
- Under a still cursor the world moves with your head: that is the effect.
  Building tools and the hold key keep it still when it matters.
- Buildings are checked with one ray along the camera's move, whose result
  comes a frame late. On a fast turn in a narrow street the camera can be
  inside a wall for one frame, and the near plane can cut into a balcony or
  sign that sticks out several metres from that ray.
- Through OpenTrack a lost face is not seen (see *How it works*): the view
  holds or, with the stock Tobii tracker, jumps.
- `path = physical` in the debug config uses the physical camera's lens shift
  instead of an explicit matrix; it changes the camera's field of view for
  the game's other readers and is kept for comparison only.

## Development

```sh
dotnet test tests/HeadTrack.Core.Tests   # the signal, the geometry, the settings; no game needed
dotnet test                              # every project first, so this one needs the game
```

`src/HeadTrack.Core` holds everything that is not Unity (netstandard2.0,
tested on .NET); `src/HeadTrack` is the mod (net48, the game's assemblies
referenced from the game folder, never copied); `probes/` holds the two
throw-away mods the camera hook was found with.

A `HeadTrack.debug.cfg` next to the mod's DLL (start from
`src/HeadTrack/HeadTrack.debug.cfg.example`) can open a debug channel on
localhost: `tools/headtrack_send.py` sends test poses and commands (dump the
camera state, take screenshots, change settings, place the camera, quit the
game), and `tools/shots.py` compares screenshots. The channel takes commands
from any program on the machine, so open it only while testing. `tools/golden/` generates
the reference vectors the geometry tests check against with an independent
NumPy implementation; `tools/decompile.sh` decompiles game assemblies into a
folder outside the repository.

`tools/publish.sh Publish|NewVersion|Update` uploads a release build to
Paradox Mods with the ModPublisher the game ships, using
`src/HeadTrack/Properties/PublishConfiguration.xml` and the game's own
Paradox login (on Linux; on Windows the official modding toolchain does the
same).

## Licence

MIT; see [LICENSE](LICENSE). The mod ships Harmony's `0Harmony.dll`, under
Harmony's own MIT licence ([THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)).
