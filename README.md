# STS2 Map Color Mod

Give the Slay the Spire 2 map clearer room-type colors, live preview controls, and a quick on/off toggle.

## Overview

STS2 Map Color Mod improves map readability by tinting room icons based on room type.

It is designed for players who want to scan routes faster without replacing the original map layout.

## Features

- Color map room icons by type
- Tune color, transparency, and brightness in-game
- Real-time preview for each room type in the settings page
- Live apply while the map is open
- `F8` quick toggle for map tint on/off
- Restore a recommended default palette with one click

## Supported Room Types

- Enemy
- Elite
- Rest
- Shop
- Treasure
- Unknown

Boss and Ancient/Special nodes currently keep vanilla behavior to avoid hover and animation bugs.

## In-Game Settings

Open:

`Settings -> 地图配置 / Map Color`

You can:

- Enable or disable map tinting
- Enable or disable icon tinting
- Adjust each room type's color
- Adjust transparency
- Adjust brightness
- Preview the result immediately
- Restore the recommended defaults

Changes are automatically saved and automatically applied.

## Default Palette

The built-in defaults aim for quick visual recognition:

- Enemy: soft desaturated steel
- Elite: dark crimson
- Rest: flame red
- Shop: olive gold
- Treasure: rich gold-purple
- Unknown: green

These are only defaults. You can fully customize them.

## Installation

This mod requires both:

- `sts2_map_mod.dll`
- `sts2_map_mod.pck`

The installed mod folder should also contain:

- `sts2_map_mod.json`
- `map_color_config.json.txt`

Important:

- Do not put `map_color_config.json` directly into the game mod folder.
- The game may treat every `*.json` file there as a mod manifest.
- Use `map_color_config.json.txt` instead.

## Build

Requirements:

- Godot 4.5.1 for exporting the `.pck`
- .NET SDK 9+

Build the DLL:

```bash
dotnet build
```

Install into the game:

```bash
make install
```

Restart the game:

```bash
make restart-game
```

## Notes

- `F8` toggles map tinting while the map is open.
- The mod is focused on room icon presentation, not path line coloring.
- If a visual change does not seem to apply after code changes, restart the game.

## TODO

- 待实现：路线穷举和推荐功能

## Non-Commercial License

This project is released for non-commercial use only.

- Personal use is allowed
- Modification is allowed
- Sharing is allowed
- Commercial use is not allowed without prior written permission

See [LICENSE](LICENSE).
