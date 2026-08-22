# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read these first

Detailed reference docs live in `docs/` (written 2026-08-18 after the local checkout
at `C:\Users\User\Desktop\juegos unity\Tinted Showdown` lost its `.git`/`Assets` —
this `E:\Users\Alejandro\Opal\Tinted Showdown` copy is now the active working copy):

- `docs/PROJECT_OVERVIEW.md` — architecture, stack, 8 real production gotchas found
  in this codebase (Relay endpoint selection, WebGL decompression fallback, MPPM
  safety, RPC serialization limits, etc.), functional status, open items.
- `docs/QUICKLOBBY_PACKAGE.md` — a **separate side-project**: the networking stack
  of this game (NGO + Relay), extracted and sold independently as a Unity asset.
  Lives in the sibling folder `E:\Users\Alejandro\Opal\QuickLobby-NetcodeRelayKit`
  (own git repo, own README/manual — not part of this repo). Read this doc for
  pricing, marketplace decisions, and pending fixes before touching that folder.
- `docs/STATUS.md` — where everything currently lives on disk, and what's stashed/pending.

## Project

**Tinted Showdown** — Unity 6000.3.13f1, online 2–4 player FFA color-matching game.
Platforms: PC, Android, WebGL. Published build: https://alejandrozumbado.github.io/tinted-showdown-build/

## Game Rules

- Each player has a **body color** and a **weapon color** (8 buttons on their UI).
- Each round (~3s), you score 1 point per enemy whose body color matches your weapon color.
- First player(s) to reach **10 points** win. Multiple simultaneous winners possible.
- No ranking — just a winner announcement screen.

## Commands

There is no CLI build/lint/test workflow — this is a Unity project, driven from the Editor
(`E:\versionesUnity\6000.3.13f1\Editor\Unity.exe`) or via `-executeMethod` for headless runs.
No test framework is set up in `Assets/` (no `asmdef`, no `Tests/` folder).

- **Setup wiring** (menu, run once per fresh scene state): `Tinted Showdown → Setup All (run once)`
  → `TintedShowdownSetup.SetupAll()` in `Assets/Scripts/Editor/TintedShowdownSetup.cs`.
  Idempotent — safe to re-run after adding scene/prefab content (see philosophy note below).
  Headless equivalent (no dialogs): `-executeMethod TintedShowdownSetup.SetupAllHeadless`
- **Build WebGL** (menu): `Tinted Showdown → Build WebGL` → `WebGLBuildScript.Build()`.
  Headless:
  ```
  Unity.exe -batchmode -quit -projectPath "<repo>" -executeMethod WebGLBuildScript.Build
  ```
  Outputs to `E:\Users\Alejandro\Opal\Builds\Tinten Showdown` (separate git repo, deploys to
  GitHub Pages — not part of this repo). Full deploy flow in `docs/PROJECT_OVERVIEW.md`.

## Network Stack

| Package | Role |
|---|---|
| `com.unity.netcode.gameobjects` 2.1.1 | State sync, NetworkVariable, ClientRpc |
| `com.unity.services.relay` 1.1.1 | Relay allocation, join codes |
| `com.unity.services.authentication` 3.3.3 | Anonymous UGS login (required by Relay) |
| `com.unity.services.core` 1.12.5 | UGS base |

Architecture: **1 Host + up to 3 Clients**. Host is always player 1. No dedicated server, no matchmaking. Players join via a **6-char join code** (like Among Us).

## Scene Flow

```
GameMenu.unity  →  (all players joined)  →  server loads Arena.unity
  LobbyUIManager panels:                      GameManager.OnNetworkSpawn
  menu → create / join → wait room            spawns one Player per client
```

## Scripts (`Assets/Scripts/`)

### `SessionNetworkManager.cs` (MonoBehaviour, DontDestroyOnLoad)
- `CreateRoomAsync(int maxPlayers)` → UGS login + Relay alloc + `StartHost()`
- `JoinRoomAsync(string code)` → UGS login + Relay join + `StartClient()`
- `LoadGameScene()` → `NetworkManager.SceneManager.LoadScene("Arena", Single)` (server only)
- Fires events: `OnRoomCreated(code)`, `OnJoinedRoom`, `OnHostLeft`, `OnError(msg)`
- WebGL protocol: `"wss"` instead of `"dtls"` — handled automatically via `Application.platform`

### `ActionPlayerManager.cs` (NetworkBehaviour)
- `NetworkVariable<int> bodyColor` — Owner write
- `NetworkVariable<int> weaponColor` — Owner write
- `NetworkVariable<int> score` — Server write
- `NetworkVariable<int> playerSlot` — Server write (1–4)
- `OnNetworkSpawn`: owner picks random starting colors; server calls `GameManager.RegisterPlayer`
- `ChangeColor(int)` / `AttackColor(int)` — guard-checked `if (!IsOwner) return`

### `GameManager.cs` (NetworkBehaviour, scene-placed in `Arena.unity`)
- `static int MaxPlayersTarget` — set by `SessionNetworkManager` before `StartHost()`
- `static GameManager Instance` — **do not read this from player/client code** (MPPM
  unsafe, see `docs/PROJECT_OVERVIEW.md` gotcha #3); this `static` is tolerated here
  because `GameManager` is server-only singleton logic, not per-player state.
- `OnNetworkSpawn` (server): spawns one `playerPrefab` per connected client at `spawnPoints`
- `RegisterPlayer` → assigns playerSlot, triggers `UpdateWaitingRoomClientRpc`, starts game when full
- `EvaluateRound()`: server scores all players, checks ≥10 → builds the winner
  announcement as a single formatted string server-side, sends via `ShowWinnersClientRpc(string message)`
  (NGO RPCs can't serialize `string[]` — see `docs/PROJECT_OVERVIEW.md` gotcha #7)
- ClientRpcs: `UpdateWaitingRoomClientRpc`, `StartGameClientRpc`, `BeginTimerClientRpc`, `ShowWinnersClientRpc(string)`

### `LobbyUIManager.cs` (MonoBehaviour, in GameMenu scene)
- `SetLocalPlayer(ActionPlayerManager)` — called by `ActionPlayerManager.OnNetworkSpawn` on owner
- `OnBodyColorButton(int)` / `OnWeaponColorButton(int)` — delegate to `localPlayer`
- `UpdateWaitingRoom(int, int)` / `ShowGamePanel()` / `ShowWinners(string message)` — called by GameManager ClientRpcs
- Panels: `menuPanel`, `createPanel`, `waitPanel`, `winPanel`

### `LobbyNetwork.cs` (NetworkBehaviour, scene-placed in `GameMenu.unity`)
- `NetworkVariable<int> PlayerCount` / `MaxPlayers` — server write, synced lobby headcount
- `Initialize(count, max)` / `SetPlayerCount(count)` — server-only, push via `PushCountClientRpc`
  rather than relying on `OnValueChanged` (see `docs/PROJECT_OVERVIEW.md` gotcha #4)
- Has a `static Instance` kept only for compatibility — do not read it from player/client code
  (same MPPM hazard as `GameManager.Instance`); use `FindFirstObjectByType<LobbyNetwork>()`

### `ColorButtonProxy.cs` (MonoBehaviour, on `Canvas.prefab` root)
- Routes body/weapon color button clicks to `LobbyUIManager` via `FindFirstObjectByType`
  (MPPM-safe indirection layer between UI buttons and the lobby singleton-ish manager)

### `EnvironmentPresetManager.cs` (NetworkBehaviour, in `Arena.unity`)
- Picks one of 3 `EnvironmentPreset` entries (Day/Night/Blend: skybox, fog, sun light,
  `environment` rig rotation) via server-rolled `NetworkVariable<int>` in `OnNetworkSpawn`,
  replicated to all clients — see `## EnvironmentManager` section below for setup status

### `DragButton.cs`
- Touch drag gesture detector. `PerformButtonAction()` is a stub — not yet integrated.

### `Assets/Scripts/Editor/TintedShowdownSetup.cs` and `WebGLBuildScript.cs`
- Editor-only automation — see `## Commands` above. `TintedShowdownSetup` is idempotent
  (adding a feature that needs new scene/prefab wiring: use the "Ensure" pattern — check
  if it exists, create if not — never gate it behind an unrelated "if X is missing" block);
  full philosophy in `docs/PROJECT_OVERVIEW.md`.

## Manual Unity Setup Required

### Unity Dashboard (do this first)
1. `dashboard.unity3d.com` → create project
2. `Edit → Project Settings → Services` → link org + project

### `GameMenu.unity`
- GO `NetworkRoot`: `NetworkManager` + `UnityTransport`
  - Enable **"Scene Management"** in NetworkManager
  - Add `Arena` to **"Registered Scene Names"**
- GO `SessionManager`: `SessionNetworkManager` script
- Canvas with `LobbyUIManager` script + 4 panels wired in Inspector

### `Player.prefab`
- Add `NetworkObject` to root GO

### `Arena.unity`
- GO `GameManager`: `NetworkObject` + `GameManager` script
  - Assign: `timerImage`, `playerPrefab` (Player.prefab), `spawnPoints` (4 empty GOs)
- 4 empty GOs positioned as spawn points, assigned to `GameManager.spawnPoints[]`

### Canvas.prefab (rewire buttons)
- Body color buttons (4): target → `LobbyUIManager`, method → `OnBodyColorButton(int)`, args 0/1/2/3
- Weapon color buttons (4): target → `LobbyUIManager`, method → `OnWeaponColorButton(int)`, args 0/1/2/3

### Build Settings
- Scene 0: `GameMenu`
- Scene 1: `Arena`

### `EnvironmentManager` (Arena.unity)
`EnvironmentPresetManager` ya está creado. Setup All ahora rellena todo automáticamente:
- Los 3 presets (Day/Night/Blend) con material de skybox + luz + fog (de las escenas demo de BOXOPHOBIC).
- El rig `environment` (copiado de la escena demo `Demo Day.unity`, GameObject `ENVIRONMENT`) y asignado al campo `environment` del Inspector — decoración sin filtrar, el usuario la reacomoda a mano en el Editor.

El preset se elige **una sola vez por sesión**, en `OnNetworkSpawn` (server-authoritative, sincronizado a los 4 jugadores vía NetworkVariable) — se mantiene fijo toda la partida, incluyendo "Jugar de nuevo", hasta que todos salgan del lobby y se cargue una escena Arena nueva.

**PENDIENTE (a ojo, no se puede scriptear):**
1. Reacomodar a mano la decoración del rig `environment` en `Arena.unity` (en curso).
2. Ajustar `environmentRotationEuler` de cada uno de los 3 presets en el Inspector — hoy están en `(0,0,0)`.

## Color Enum

`Red=0, Blue=1, Green=2, Yellow=3` — used as int everywhere in NetworkVariables.
