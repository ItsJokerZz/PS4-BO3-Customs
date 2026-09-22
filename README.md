A fastfile porter and an `.sprx` SPRX for running Black Ops III custom maps on console.

- `SPRX/` - the SPRX source and its build scripts
- `Tool/` - the fastfile porter that converts PC maps for the console

## Requirements

### Console

- A jailbroken/HEN PS4 or PS5.
- BO3 installed on computer.
- The files mentioned below.

### Building the SPRX

- Visual Studio 2022 with the PlayStation 4 SDK **12.000** installed (ORBIS platform, Clang toolset)
- Python 3, for `downgrade_elf.py` and `make_fself.py` (both in `SPRX/`)

### Converting maps

- The PlayStation 4 SDK **12.000** as well. The porter compiles a map's technique sets for the
  console with `orbis-wave-psslc.exe`, `libSceGnm.dll` and `libSceGnmx.dll` out of the SDK's
  `host_tools/bin`, found through `SCE_ORBIS_SDK_DIR` - or `FFPORTER_PS4_SDK_BIN` if you keep those
  files somewhere else. Everything else it needs, the DXBC decompiler and the GNF image writer,
  ships with the tool.
<br>
- Without the SDK a map still converts, but its shaders cannot be built: technique sets can then
  only be reused from retail PS4 zones handed to the porter as donors, and anything the donors do
  not cover is dropped. `--no-shader-compile` is that mode on purpose.

## Installing

1. Download/build the sprx, and download [this](https://archive.org/download/bo-3-customs/BO3-Customs.zip).
2. Create a folder under `/data` named `BO3-Customs`.
3. Place the contents of the downloaded files in there.
4. Put your converted maps in the `/usermaps` folder.
5. Launch the game and load the SPRX.

## Using it

Open the tool, drag and drop the folder, and just wait. Once put the files on the console as mentioned.

The SPRX loads maps from `/data/BO3-Customs/usermaps` and puts them in the game's own map
selects: **STANDARD / CUSTOM** tabs in the Zombies map select, and a **CUSTOMS** category under
Multiplayer > CHANGE MAP. Each map gets a real map table entry, so its name, description, preview
picture, loading screen and intro movie work the way a retail map's do out the box.

Maps are scanned once, when the SPRX loads, so **add maps before you launch the game**.

## Files on the console

```text
/data/BO3-Customs/
├── BO3-Customs.sprx
├── ui_scripts/
│   ├── mapselect.lua
│   └── maptable.lua
├── zone/
│   ├── en_zm_levelcommon.ff
│   ├── en_zm_levelcommon.xpak
│   ├── zm_levelcommon.ff
│   ├── zm_levelcommon.xpak
│   └── snd/
│       ├── all/
│       │   ├── zm_levelcommon.all.sabl
│       │   └── zm_levelcommon.all.sabs
│       └── en/
│           ├── zm_levelcommon.en.sabl
│           └── zm_levelcommon.en.sabs
└── usermaps/
    └── <map>/
        ├── <map>.ff
        ├── <map>.fd
        ├── <map>.xpak
        ├── previewimage.png
        ├── loadingimage.png
        ├── workshop.json
        ├── snd/
        │   └── <lang>/
        │       ├── <map>.<lang>.sabl
        │       └── <map>.<lang>.sabs
        └── video/
            └── <name>.mkv
```

## Credits

- **ItsJokerZz** — SPRX and port development.
- **flatz** — `downgrade_elf.py` and `make_fself.py`.

## License

MIT — see [LICENSE](LICENSE).
