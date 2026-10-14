# Over The Hill: Expanded Lobbies Mod

A MelonLoader mod for **Over the Hill** (by Funselektor) that expands multiplayer lobby capacities beyond the default 4-player limit up to 16 players.

---

## Features
- **Expanded Player Limits**: Adds selectable lobby player limits (`2`, `3`, `4`, `6`, `8 (Unstable)`, `10 (Unstable)`, `12 (Unstable)`, `16 (Unstable)`) directly in the in-game *Create Server* menu.
- **Safety Tags**: Lobby sizes of 8 and higher are clearly designated with an `(Unstable)` tag in the UI to notify the host and joining players that game network arrays were originally budgeted for 4–8 players.
- **Lobby Spawn Point Guard**: Dynamically modulo-wraps lobby vehicle spawn points to prevent out-of-bounds exceptions when player count exceeds available physical spawn markers.
- **Network Exception Guards**: Implements Harmony finalizer guards on `OverlandSessionPlayers.AddPlayer`, `RemovePlayer`, and `RPC_ConnectedPlayer` to prevent unhandled `IndexOutOfRangeException` or `AssertException` disconnects when large parties join.

---

## Installation

### Prerequisites
1. [MelonLoader](https://github.com/LavaGang/MelonLoader) v0.7.3+ (automated or manual installer).
2. Run the game once with MelonLoader installed so it generates the `Il2CppAssemblies/` folder.

### Mod Setup
1. Download the latest `OverTheHillExpandedLobbies.dll` from the [Releases](https://github.com/Danie1Winn/over-the-hill-multiplayer-mod/releases) page.
2. Place `OverTheHillExpandedLobbies.dll` into your game's `Mods` folder:
   ```
   <SteamLibrary>\steamapps\common\over the hill\Mods\OverTheHillExpandedLobbies.dll
   ```
3. Launch the game through Steam as normal.

---

## Building from Source

### Prerequisites
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- Installed game with MelonLoader run at least once.

### Build Steps
```bash
# Clone the repository
git clone https://github.com/Danie1Winn/over-the-hill-multiplayer-mod.git
cd over-the-hill-multiplayer-mod

# Build Release binary (automatically locates Steam/MelonLoader assemblies)
dotnet build -c Release
```

The compiled binary will be placed in `src/OverTheHillExpandedLobbies/bin/Release/net6.0/OverTheHillExpandedLobbies.dll` and copied directly into your game's `Mods/` folder if detected.

---

## License
This project is licensed under the [MIT License](LICENSE).
