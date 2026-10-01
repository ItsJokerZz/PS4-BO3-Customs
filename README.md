A tool to convert BO3 customs to PS4 and a SPRX to load them on HEN consoles.

## Getting started

Everything you need is on the [Releases](../../releases) page: the tool, SPRX, and dependencies. 
You will need a copy of **BO3 installed on PC** + whatever maps you wish to port.
Ensure you have **version 1.33** of the game installed on your console (**any region**).

1. Extract the `Console.zip` from the releases and copy it to `/data/` on your console.
2. Next now place the SPRX wherever you wish, perferably in `/data/BO3-Customs/`.
3. Run the tool, drag and drop a steam map into the tool and then wait for it to finish.
4. Copy the converted map into `/data/BO3-Customs/usermaps/` however you may wish.
5. Finally then just launch the game and load the SPRX with you perfered method.

Maps can also go on a USB drive or extended storage (`/mnt/usb0-7/`, `/mnt/ext0-7/`), in `BO3-Customs/usermaps/`
at the root of the drive (a `BO3-Customs/zone/` there works too). Everything else stays in `/data/BO3-Customs/`.

## Console Layout
```text
/data/BO3-Customs/
├── BO3-Customs.sprx
├── ui_scripts/
│   ├── graphics.lua
│   ├── kbm_strings.lua
│   ├── mapselect.lua
│   ├── maptable.lua
│   ├── mouse.lua
│   └── restart.lua
├── lui/
│   └── ui/
│       └── t7/
│           └── utility/
│               └── pcutility.lua
├── zone/
├── snd/
│   ├── all/
│   │   ├── zm_levelcommon.all.sabl
│   │   └── zm_levelcommon.all.sabs
│   └── <lang>/
│       ├── zm_levelcommon.<lang>.sabl
│       └── zm_levelcommon.<lang>.sabs
├── en_zm_levelcommon.ff
├── en_zm_levelcommon.xpak
├── zm_levelcommon.ff
├── zm_levelcommon.xpak
└── usermaps/
    └── <map>/
        ├── previewimage.png
        ├── loadingimage.png
        ├── workshop.json
        ├── <map>.ff
        ├── <map>.xpak
        ├── <lang>_<map>.ff
        ├── <lang>_<map>.xpak
        ├── snd/
        │   └── <lang>/
        │       ├── <map>.<lang>.sabl
        │       └── <map>.<lang>.sabs
        └── video/
            └── <name>.mkv
```

## Building Requirements
**The SPRX**
- VS 2022
- PS4 SDK 12.00
- .NET 10.0 SDK
- Python 3


## Credits

- **ItsJokerZz** — SPRX and port development.
- **flatz** — `downgrade_elf.py` and `make_fself.py`.

## License

MIT — see [LICENSE](LICENSE).
