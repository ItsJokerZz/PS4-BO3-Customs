# PS4 .FF Porter — Black Ops III

Converts Black Ops III PC fastfiles (usermaps, mods, weapons, any zone) into PS4 ones. Everything it needs ships with
it: no PS4 game files, no SDK, no Python. This tree is the whole program and can be built and released on its own.

## The application

One window with nothing to fill in (`PS4 .FF Porter.exe`):

- **Add folder / Add files**, or drop files and folders anywhere on the queue. Every Black Ops III PC fastfile found
  (three folder levels deep) becomes one item, and its kind comes from the file itself: maps, mods and other zones.
  What belongs to an item is picked up with it — localized zones (`en_<map>.ff`, ...), `.xpak`/`_d.xpak` streamed data
  and `snd\*\<map>.*.sabl/.sabs` sound banks. A fastfile of another game is skipped with a line saying so.
- **Convert** converts the queue into the output folder (the button in the header; `exports` beside the application
  until changed). Each item gets `<output>\t7\<name>`.
- The panel beside the queue shows the selected item's live **port fidelity** report, or why an item failed. Problems
  show as one line with a **Details** toggle, so they never cover the grades.
- The log is hidden until **Log** is toggled or a conversion fails.

On first launch it asks for your **Black Ops III game files** - the folder holding `base.xpak` (pick the game folder
or the `zone` folder inside it; both are accepted). A map's textures and technique sets are completed from there, and
every conversion passes it as `--pc-reference`. The **Game files** button in the header shows the folder and changes
it later; without one, the porter still looks next to the input and in a Steam install as it always did.

Advanced options stay in the command line. The application remembers only that folder and where converted files go
(`%LOCALAPPDATA%\PS4 FF Porter\ui.t7.json`).

## Port fidelity

Every conversion scores how faithfully each part of the map carried over: Geometry, Textures, Materials, Lighting,
Collision, FX, Sound, Entities, Scripts and World. Each converted asset (and every inline sub-asset), streamed item,
sound and movie is a unit graded by what its converter did:

| grade | score | meaning |
|---|---|---|
| exact | 100 | carried over unchanged, or re-laid out with every value kept (byte copies, model/material/image layouts, remapped scripts checked with acts) |
| strong | 90 | kept apart from a small documented loss (FLAC re-encoded to MP3 - the only format the PS4 plays - resampled audio, technique sets compiled from PC shaders) |
| good | 75 | a stand-in that normally matches (alias names by the PS4 naming rule, rewritten PC-only builtins) |
| approximate | 50 | kept in PC form where PS4 may differ (payloads with no conversion rule yet, unknown vertex semantics) |
| weak | 20 | likely broken in game (streamed items no converted xpak knows; the PC key stays) |
| missing | 0 | not in the output (assets that could not be converted, failed banks or movies) |

A part's score is the mean of its units; the overall score weighs the parts the map has. Notes under each part say what
was approximated. The command line prints the table at the end of `convert` and writes it as `work\reports\<map>.fidelity.json`; with
`--progress` it also prints `FFPORTER_FIDELITY {json}` snapshots while it runs, which is how the application updates
the report, the progress bar and the status line live.

## How a conversion works

Every asset type of a map is converted: sound banks, xpaks (textures and meshes), images, klf and texturecombo,
materials and technique sets, models and meshes, the world (`gfx_map`, including streamed probes and skyboxes),
scripts, nav meshes and nav volumes, fx, weapons, light descriptions and anim tables. Each converter was checked
against a retail PS4 zone during development; releases need no PS4 files.

- **Technique sets** are compiled for PS4 from the map's own PC shaders (DXBC translated to PSSL, then the PS4 SDK
  compiler when one is installed), so no PS4 game zone is needed.
- **Scripts** are converted by opcode remapping and then decompiled with acts (`gscd -t ps`) as a check,
  client scripts (`.csc`) included. PC-only builtins that the PS4 cannot link are rewritten where that is safe, and
  reported.
- **Streamed data** the map only references is converted into its own xpaks from the PC game's xpaks
  (`--no-bundle-streams` turns that off).
- Every converted zone is re-read with the PS4 game's own loader before it is written. The output folder holds only
  what the console loads (the fastfiles, xpaks, sound banks and the usermap's own files); the loader walks and the
  reports - `<zone>.port.json`, `<map>.map-port.json`, `<map>.fidelity.json` and, when a conversion fails,
  `<zone>.problems.txt` - are written under `work\`.

Official zones ship `.fd` delta patches beside their `.ff` files. The game applies them at load, so the porter applies
them first to the PC map zones and to the reference folders (`--no-fd` turns this off for the map zones).
`ffport t7 apply-fd <zone.ff> -o <patched.ff>` writes a patched fastfile. Mod tools maps have no `.fd` files.

## Command line

`ffport.exe` (published beside the application, or `src\FFPorter.Cli\bin\...`) has every command; `ffport t7 --help`
lists them:

```powershell
.\ffport.exe t7 convert 'D:\bo3\usermaps\zm_example\zm_example.ff' -o '.\exports\t7\zm_example' --force --progress
.\ffport.exe t7 info 'D:\bo3\usermaps\zm_example\zm_example.ff'
```

The application does the same work in a child copy of itself, so shipping `ffport.exe` is optional.

Working files and caches go in `work\` beside the executable (`Workspace.WorkDirectory`, or `FFPORTER_WORK`). Remove it
only with the application closed; it regenerates on the next conversion, which then takes longer. The maps themselves
stay where they are.

## Source layout

| project | what it is |
|---|---|
| `src/FFPorter.Core` | what neither game owns: the native harness host and child, tool lookups and runners, the Python parity layer, audio (libsndfile), compression, hashing, the fidelity model and protocol, the workspace |
| `src/FFPorter.Core.T7` | the engine: the port and its converters, link, streams, scripts, shaders, sound, movies, and `Data/Shipped` (`t7_ps4_image`, `t7_pc_image`, `t7_gsc`) |
| `src/FFPorter.Cli.Shared` | the option parser and the fidelity table the command line prints |
| `src/FFPorter.Cli.T7` | the `t7 ...` commands |
| `src/FFPorter.Cli` | `ffport.exe` |
| `src/FFPorter.Desktop.Shared` | the window: queue, fidelity panel, log, theme |
| `src/FFPorter.Desktop.T7` | the application: its `Edition` (the game's words, the header check, how files are grouped, which converter runs) over the engine |

`FFPorter.Core`, `FFPorter.Cli.Shared` and `FFPorter.Desktop.Shared` are the same projects in the MW Remastered porter
beside this one; keep them identical (robocopy /MIR the three folders, bin and obj excluded, when one side changes).

Shipped tool data is **inside the binaries**: each project's `Data\Shipped` folder is zipped at build time
(`PackShippedData`) and embedded in its assembly - `shader_decompiler` (cmd_Decompiler.exe with d3dcompiler_46.dll)
and libsndfile in `FFPorter.Core`, `t7_ps4_image` and `t7_pc_image` (the PS4 and PC loader segments the native checks run) and `t7_gsc` (the
PS4 opcode and builtin tables) in `FFPorter.Core.T7`. The first run that needs one unpacks it into `work\data`
(`ToolData.Extract`, once per build: a marker under `work\data\.shipped` carries the archive's size), so nothing has
to ship beside the executable. A `data\` folder next to the application still wins over the embedded copy, which is how
a newer file is dropped in without a rebuild.

**acts is not shipped; it is downloaded once.** The first time the script check needs it, the porter fetches the
latest release of [atian-cod-tools](https://github.com/ate47/atian-cod-tools) (its `acts.zip`) and keeps the part it
uses - `acts.exe`, `acts-common.dll`, `acts-bo3.dll`, the `data\` tables and the licences, 18 MB - in
`work\tools\acts`, with the release tag in `release.txt`. The application does this in the background at startup, the
command line at the start of a conversion; either way it happens once. acts is GPL-3.0, which is why its licences come
down with it and nothing of it is redistributed here.

With no internet the conversion still runs: scripts are remapped but not checked, and the report says so. To use a copy
you already have, `--acts <acts.exe>` for one run or `FFPORTER_ACTS` for every run; `--no-gsc-check` skips the check
entirely.

User-supplied files are looked up, not hard-coded:

- PC loader: shipped. `t7_pc_image` unpacks beside the other tool data and the PC zone walk maps it, so nothing
  has to be dumped from a running Black Ops III. `T7_PC_DUMP` still overrides it with a dump of your own, and a
  `t7_pc_image/BlackOps3_dump.exe` or `Executables/PC/BlackOps3_dump.exe` is still used if one is there.
- PS4 SDK (optional, for the shader compiler): `FFPORTER_PS4_SDK_BIN`, else `SCE_ORBIS_SDK_DIR`, else the newest SDK
  under Program Files (x86)\SCE\ORBIS SDKs.

## Building

Build with the SDK pinned in `global.json`:

```powershell
dotnet build 'PS4 .FF Porter.sln'
dotnet publish src/FFPorter.Desktop.T7/FFPorter.Desktop.T7.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:RuntimeFrameworkVersion=10.0.5 -p:NuGetAudit=false `
  -p:DebugType=none -p:DebugSymbols=false -o publish
```

That writes the release into `publish\`: one `PS4 .FF Porter.exe` and nothing else - the tool data, the shader
decompiler and libsndfile are inside it and unpack themselves into `work\data` on first use. The same command with
`src/FFPorter.Cli` adds `ffport.exe` beside it, which is optional.
