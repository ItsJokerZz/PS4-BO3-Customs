A tool to convert BO3 customs to PS4 and a SPRX to load them on HEN consoles.

## Getting started

Everything you need is on the [Releases](../../releases) page: the tool, the SPRX, and `Deps`.

1. Copy `BO3-Customs` out of `Deps` to `/data` on the console, then drop `BO3-Customs.sprx` in it.
2. Run the tool, drag and drop a steam map into the tool and then wait for it to finish.
3. Copy the converted map into `/data/BO3-Customs/usermaps/` however you may wish.
4. Finally then just launch the game and load the SPRX with you perfered method.

Maps are scanned once, when the SPRX loads, so **add them before you launch**.
They show up under the under the map selection for either mode under its own tab.

## Console Layout

```text
/data/BO3-Customs/
├── BO3-Customs.sprx
├── ui_scripts/
│   ├── mapselect.lua
│   └── maptable.lua
├── lui/
│   └── ui/t7/utility/pcutility.lua
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
