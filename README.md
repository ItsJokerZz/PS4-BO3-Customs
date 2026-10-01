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

## Browse and install community maps

The tool has a **Browse maps** section (the navigation rail on the left) where people who do not have the PC game
can download maps that someone else already converted and install them straight to their console — no PC copy of
the game or conversion needed.

1. Open **Browse maps**, then **Console** and fill in your console's FTP address (GoldHEN's default is port `2121`,
   user `anonymous`), the install folder (defaults to `/data/BO3-Customs/usermaps`), and the **Map list URL**.
2. **Test** the connection, then **Save**.
3. Pick a map and press **Install**. The tool downloads it, checks the checksum, unpacks it, and uploads it to
   `/data/BO3-Customs/usermaps/<map>/` over FTP, showing live progress you can cancel.

The map list and files live on Cloudflare R2 (or any static host) as a small `catalog.json` plus the archives.

### Catalog format

`catalog.json` sits next to the archives on your storage. `file` and `thumbnail` may be full URLs or relative to
the catalog URL:

```json
{
  "name": "Black Ops III community maps",
  "maps": [
    {
      "name": "zm_example",
      "title": "Example",
      "author": "Someone",
      "description": "A custom zombies map.",
      "version": "1.0",
      "size": 123456789,
      "folder": "zm_example",
      "file": "maps/zm_example.zip",
      "thumbnail": "thumbnails/zm_example.png",
      "sha256": "optional lowercase hex sha-256 of the archive"
    }
  ]
}
```

Each `file` is a zip whose contents become `usermaps/<folder>/` on the console (a single top-level folder in the
archive is unwrapped automatically). To ship a default for everyone, set `AppSettings.DefaultCatalogUrl` in
`Tool/src/FFPorter.Desktop.Shared/Models/AppSettings.cs` to your bucket's public URL, e.g.
`https://pub-xxxxxxxx.r2.dev/t7/catalog.json`.

### Split archives

Some hosts cap how large a single upload can be (the Cloudflare dashboard and Wrangler both stop at 300 MiB). For
maps bigger than that, split the zip into parts and list them under `parts` instead of `file`. The tool downloads
them in order, joins them into one archive, and checks `sha256` against the joined result:

```json
{
  "name": "zm_big",
  "title": "A Big Map",
  "size": 735753347,
  "folder": "zm_big",
  "parts": [
    "maps/zm_big.zip.001",
    "maps/zm_big.zip.002",
    "maps/zm_big.zip.003"
  ],
  "sha256": "the sha-256 of the whole zip, not of a part"
}
```

`size` and `sha256` describe the whole archive. A map with a single `file` and a map with `parts` install
identically.

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
