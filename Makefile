# STS2 Map Mod - build and copy to game
# Override: make copy STS2_DIR="/path/to/Slay the Spire 2"
# macOS (new): mods live in .app/Contents/MacOS/mods (see STS2-Agent build-mod.sh)

STS2_DIR ?= $(HOME)/Library/Application Support/Steam/steamapps/common/Slay the Spire 2
# Steam launch (DRM): use steam:// — AppID from Steam library appmanifest_2868840.acf ("Slay the Spire 2")
STS2_STEAM_APPID ?= 2868840
# macOS: CFBundleIdentifier — used for graceful quit (AppleScript); avoid plain killall (SIGTERM can hang Godot/Steam)
STS2_MAC_BUNDLE_ID ?= com.megacrit.SlayTheSpire2
# macOS: CFBundleExecutable — wait/kill -9 if still alive after quit
STS2_MAC_PROCESS_NAME ?= Slay the Spire 2
MOD_NAME := sts2_map_mod
# macOS: SlayTheSpire2.app/Contents/MacOS/mods/$(MOD_NAME); Windows: game root mods/$(MOD_NAME)
UNAME_S := $(shell uname -s)
ifeq ($(UNAME_S),Darwin)
MOD_DIR := $(STS2_DIR)/SlayTheSpire2.app/Contents/MacOS/mods/$(MOD_NAME)
# Game may also scan this (causes duplicate "Map Color Mod" if both have the mod):
MOD_DIR_LEGACY := $(STS2_DIR)/mods/$(MOD_NAME)
# Managed assemblies (matches sts2_map_mod.csproj Sts2DataDir for Apple Silicon build)
STS2_DATA_DIR ?= $(STS2_DIR)/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64
else
MOD_DIR := $(STS2_DIR)/mods/$(MOD_NAME)
MOD_DIR_LEGACY :=
# Windows/Linux: set to the folder that contains sts2.dll (e.g. game install data_* subdir)
STS2_DATA_DIR ?=
endif
# ILSpy CLI: dotnet tool install --global ilspycmd  (tool is net8; roll forward on net9-only machines)
ILSPYCMD_DLL := $(firstword $(wildcard $(HOME)/.dotnet/tools/.store/ilspycmd/*/ilspycmd/*/tools/net8.0/any/ilspycmd.dll))
DECOMP_OUT := decompiled/sts2
# Godot.NET.Sdk outputs to .godot/mono/temp; fallback to project bin/
GODOT_DLL := .godot/mono/temp/bin/Debug/$(MOD_NAME).dll
DLL := bin/Debug/net9.0/$(MOD_NAME).dll
DLL_SRC := $(shell [ -f "$(GODOT_DLL)" ] && echo "$(GODOT_DLL)" || echo "$(DLL)")
PCK := export/$(MOD_NAME).pck
# Source in repo (JSON). Installed name must NOT be *.json — game scans all .json as mod manifests.
CONFIG := map_color_config.json
CONFIG_INSTALLED := map_color_config.json.txt
# New game format: <ModId>.json (id, has_pck, has_dll, etc.) — see QuickRestart
MOD_JSON := $(MOD_NAME).json
RELEASE_DIR := release
RELEASE_STAGE := $(RELEASE_DIR)/$(MOD_NAME)
VERSION := $(shell sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$(MOD_JSON)" | head -1)
RELEASE_ZIP := $(RELEASE_DIR)/$(MOD_NAME)_v$(VERSION).zip
# Godot for CLI export. Auto-detect on macOS if not in PATH. Override: make export-pck GODOT=/path/to/Godot
GODOT_DARWIN := $(shell \
	command -v godot 2>/dev/null || \
	([ -x "/Applications/Godot.app/Contents/MacOS/Godot" ] && echo "/Applications/Godot.app/Contents/MacOS/Godot") || \
	([ -x "/Applications/Godot_mono.app/Contents/MacOS/Godot" ] && echo "/Applications/Godot_mono.app/Contents/MacOS/Godot") || \
	([ -x "/Applications/Godot_4.5.1.app/Contents/MacOS/Godot" ] && echo "/Applications/Godot_4.5.1.app/Contents/MacOS/Godot") || \
	echo "godot")
GODOT ?= $(if $(filter Darwin,$(UNAME_S)),$(GODOT_DARWIN),godot)

.PHONY: build copy copy-pck install export-pck clean-mod clean-duplicate launch-steam restart-game decompile-sts2 install-ilspycmd release-zip

# Install ILSpy command-line decompiler (once per machine). Then: make decompile-sts2
install-ilspycmd:
	dotnet tool install --global ilspycmd
	@echo 'If ilspycmd fails to run, add $$HOME/.dotnet/tools to PATH, or use:'
	@echo '  DOTNET_ROLL_FORWARD=LatestMajor dotnet $$(ls $$HOME/.dotnet/tools/.store/ilspycmd/*/ilspycmd/*/tools/net8.0/any/ilspycmd.dll | head -1) --help'

# Decompile sts2.dll into $(DECOMP_OUT) for local reference (read-only; folder is gitignored).
decompile-sts2:
	@if [ -z "$(ILSPYCMD_DLL)" ]; then \
		echo "ilspycmd not found. Run: make install-ilspycmd"; exit 1; \
	fi
	@if [ -z "$(STS2_DATA_DIR)" ] || [ ! -f "$(STS2_DATA_DIR)/sts2.dll" ]; then \
		echo "Missing sts2.dll. Set STS2_DATA_DIR to the folder containing it."; \
		echo "  macOS ARM default: .../SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64"; \
		echo "  Intel Mac may use: .../data_sts2_macos_x86_64 (verify in Finder)"; \
		exit 1; \
	fi
	mkdir -p "$(DECOMP_OUT)"
	rm -rf "$(DECOMP_OUT)"/*
	DOTNET_ROLL_FORWARD=LatestMajor dotnet "$(ILSPYCMD_DLL)" -p --nested-directories -o "$(DECOMP_OUT)" -r "$(STS2_DATA_DIR)" "$(STS2_DATA_DIR)/sts2.dll"
	@echo "Decompiled to $(DECOMP_OUT)/ (open MegaCrit/ etc. in your IDE)"

# Launch game through Steam only (no direct .app/.exe — matches current DRM requirement).
# macOS: open "steam://run/<appid>"; override: make launch-steam STS2_STEAM_APPID=...
launch-steam:
ifeq ($(UNAME_S),Darwin)
	open "steam://run/$(STS2_STEAM_APPID)"
else ifeq ($(UNAME_S),Linux)
	(command -v xdg-open >/dev/null && xdg-open "steam://run/$(STS2_STEAM_APPID)") || \
		(command -v steam >/dev/null && steam "steam://rungameid/$(STS2_STEAM_APPID)") || \
		{ echo "Install Steam client or set PATH so steam/xdg-open is available."; exit 1; }
else
	cmd //c start "" "steam://run/$(STS2_STEAM_APPID)"
endif

# Quit running game (best-effort) then launch via Steam.
restart-game:
ifeq ($(UNAME_S),Darwin)
	@osascript -e 'tell application id "$(STS2_MAC_BUNDLE_ID)" to quit' 2>/dev/null || true
	@n=0; while killall -0 "$(STS2_MAC_PROCESS_NAME)" 2>/dev/null && [ "$$n" -lt 15 ]; do sleep 1; n=$$((n+1)); done
	@-killall -9 "$(STS2_MAC_PROCESS_NAME)" 2>/dev/null || true
	@sleep 3
	$(MAKE) launch-steam
else ifeq ($(UNAME_S),Linux)
	@-pkill -TERM -f SlayTheSpire2 2>/dev/null || pkill -TERM -f "Slay the Spire 2" 2>/dev/null || true
	@n=0; while pgrep -f SlayTheSpire2 >/dev/null 2>&1 && [ "$$n" -lt 15 ]; do sleep 1; n=$$((n+1)); done
	@-pkill -9 -f SlayTheSpire2 2>/dev/null || pkill -9 -f "Slay the Spire 2" 2>/dev/null || true
	@sleep 3
	$(MAKE) launch-steam
else
	@-taskkill //F //IM SlayTheSpire2.exe //T 2>/dev/null || true
	@sleep 3
	$(MAKE) launch-steam
endif

build:
	dotnet build

# Clear existing files in game mod folder (so install is a clean overwrite)
clean-mod:
	@if [ -d "$(MOD_DIR)" ]; then \
		echo "Clearing $(MOD_DIR) ..."; \
		rm -rf "$(MOD_DIR)"/*; \
		echo "Done."; \
	fi

copy: build
	@mkdir -p "$(MOD_DIR)"
	cp "$(DLL_SRC)" "$(MOD_DIR)/"
	@if [ -f "$(CONFIG)" ]; then cp "$(CONFIG)" "$(MOD_DIR)/$(CONFIG_INSTALLED)"; fi
	@if [ -f "$(MOD_JSON)" ]; then cp "$(MOD_JSON)" "$(MOD_DIR)/"; fi
	@echo "Copied dll (+ $(CONFIG_INSTALLED) + $(MOD_JSON)) to $(MOD_DIR)"
	@ls -la "$(MOD_DIR)"

# Copy exported .pck and all mod files to game mod folder (run after make export-pck)
copy-pck:
	@mkdir -p "$(MOD_DIR)"
	@if [ -f "$(PCK)" ]; then cp "$(PCK)" "$(MOD_DIR)/"; fi
	@if [ -f "$(MOD_JSON)" ]; then cp "$(MOD_JSON)" "$(MOD_DIR)/"; fi
	@if [ -f "$(CONFIG)" ]; then cp "$(CONFIG)" "$(MOD_DIR)/$(CONFIG_INSTALLED)"; fi
	@cp "$(DLL_SRC)" "$(MOD_DIR)/"
	@if [ -f "$(PCK)" ]; then echo "Copied dll + $(PCK) + config to $(MOD_DIR)"; ls -la "$(MOD_DIR)"; else echo "No $(PCK) - export from Godot first."; exit 1; fi

# Full install: remove duplicate mod (macOS) → clear mod dir → build → export PCK → copy all to game
install: clean-duplicate clean-mod build export-pck
	@mkdir -p "$(MOD_DIR)"
	cp "$(DLL_SRC)" "$(MOD_DIR)/"
	@if [ -f "$(PCK)" ]; then cp "$(PCK)" "$(MOD_DIR)/"; fi
	@if [ -f "$(CONFIG)" ]; then cp "$(CONFIG)" "$(MOD_DIR)/$(CONFIG_INSTALLED)"; fi
	@if [ -f "$(MOD_JSON)" ]; then cp "$(MOD_JSON)" "$(MOD_DIR)/"; fi
	@echo "Installed to $(MOD_DIR):"
	@ls -la "$(MOD_DIR)"

# Export PCK from command line (no Godot GUI). On macOS auto-tries /Applications/Godot*.app if godot not in PATH.
export-pck:
	@mkdir -p export
	@(command -v "$(GODOT)" >/dev/null 2>&1 || [ -x "$(GODOT)" ]) || { \
		echo "Godot not found. Install Godot (4.5.1 recommended) or set GODOT= path, e.g.:"; \
		echo "  make export-pck GODOT=/Applications/Godot.app/Contents/MacOS/Godot"; exit 1; }
	$(GODOT) --path . --headless --export-pack "PCK (Mod)" "$(PCK)"
	@echo "Exported $(PCK). Run 'make copy-pck' to copy to game."

# Remove duplicate mod from legacy location (game root mods/) so only one "Map Color Mod" appears (macOS).
clean-duplicate:
	@if [ -n "$(MOD_DIR_LEGACY)" ] && [ -d "$(MOD_DIR_LEGACY)" ]; then \
		echo "Removing duplicate from $(MOD_DIR_LEGACY) ..."; \
		rm -rf "$(MOD_DIR_LEGACY)"; \
		echo "Done. Keep mod only in $(MOD_DIR)"; \
	else \
		echo "No legacy mod folder to remove, or not on macOS."; \
	fi

# Create a clean distributable zip for GitHub Releases.
release-zip:
	dotnet build -p:SkipCopyMod=true
	$(MAKE) export-pck
	@mkdir -p "$(RELEASE_STAGE)"
	@rm -rf "$(RELEASE_STAGE)"/*
	@cp "$(DLL_SRC)" "$(RELEASE_STAGE)/"
	@cp "$(PCK)" "$(RELEASE_STAGE)/"
	@if [ -f "$(MOD_JSON)" ]; then cp "$(MOD_JSON)" "$(RELEASE_STAGE)/"; fi
	@if [ -f "$(CONFIG)" ]; then cp "$(CONFIG)" "$(RELEASE_STAGE)/$(CONFIG_INSTALLED)"; fi
	@cd "$(RELEASE_DIR)" && rm -f "$(notdir $(RELEASE_ZIP))" && zip -rq "$(notdir $(RELEASE_ZIP))" "$(MOD_NAME)"
	@echo "Created $(RELEASE_ZIP)"
	@echo "Release contents:"
	@find "$(RELEASE_STAGE)" -maxdepth 1 -type f | sort
