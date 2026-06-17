# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Tinted Showdown** — Unity 6000.3.13f1, online 2–4 player FFA color-matching game.
Platforms: PC, Android, WebGL.

## Game Rules

- Each player has a **body color** and a **weapon color** (8 buttons on their UI).
- Each round (~3s), you score 1 point per enemy whose body color matches your weapon color.
- First player(s) to reach **10 points** win. Multiple simultaneous winners possible.
- No ranking — just a winner announcement screen.

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
GameMenu.unity  →  (all players joined)  →  server loads 1v1.unity
  LobbyUIManager panels:                      GameManager.OnNetworkSpawn
  menu → create / join → wait room            spawns one Player per client
```

## Scripts (`Assets/Scripts/`)

### `SessionNetworkManager.cs` (MonoBehaviour, DontDestroyOnLoad)
- `CreateRoomAsync(int maxPlayers)` → UGS login + Relay alloc + `StartHost()`
- `JoinRoomAsync(string code)` → UGS login + Relay join + `StartClient()`
- `LoadGameScene()` → `NetworkManager.SceneManager.LoadScene("1v1", Single)` (server only)
- Fires events: `OnRoomCreated(code)`, `OnJoinedRoom`, `OnHostLeft`, `OnError(msg)`
- WebGL protocol: `"wss"` instead of `"dtls"` — handled automatically via `Application.platform`

### `ActionPlayerManager.cs` (NetworkBehaviour)
- `NetworkVariable<int> bodyColor` — Owner write
- `NetworkVariable<int> weaponColor` — Owner write
- `NetworkVariable<int> score` — Server write
- `NetworkVariable<int> playerSlot` — Server write (1–4)
- `OnNetworkSpawn`: owner picks random starting colors; server calls `GameManager.RegisterPlayer`
- `ChangeColor(int)` / `AttackColor(int)` — guard-checked `if (!IsOwner) return`

### `GameManager.cs` (NetworkBehaviour, scene-placed in `1v1.unity`)
- `static int MaxPlayersTarget` — set by `SessionNetworkManager` before `StartHost()`
- `static GameManager Instance`
- `OnNetworkSpawn` (server): spawns one `playerPrefab` per connected client at `spawnPoints`
- `RegisterPlayer` → assigns playerSlot, triggers `UpdateWaitingRoomClientRpc`, starts game when full
- `EvaluateRound()`: server scores all players, checks ≥10 → `ShowWinnersClientRpc`
- ClientRpcs: `UpdateWaitingRoomClientRpc`, `StartGameClientRpc`, `BeginTimerClientRpc`, `ShowWinnersClientRpc`

### `LobbyUIManager.cs` (MonoBehaviour, in GameMenu scene)
- `SetLocalPlayer(ActionPlayerManager)` — called by `ActionPlayerManager.OnNetworkSpawn` on owner
- `OnBodyColorButton(int)` / `OnWeaponColorButton(int)` — delegate to `localPlayer`
- `UpdateWaitingRoom(int, int)` / `ShowGamePanel()` / `ShowWinners(int[])` — called by GameManager ClientRpcs
- Panels: `menuPanel`, `createPanel`, `waitPanel`, `winPanel`

### `DragButton.cs`
- Touch drag gesture detector. `PerformButtonAction()` is a stub — not yet integrated.

## Manual Unity Setup Required

### Unity Dashboard (do this first)
1. `dashboard.unity3d.com` → create project
2. `Edit → Project Settings → Services` → link org + project

### `GameMenu.unity`
- GO `NetworkRoot`: `NetworkManager` + `UnityTransport`
  - Enable **"Scene Management"** in NetworkManager
  - Add `1v1` to **"Registered Scene Names"**
- GO `SessionManager`: `SessionNetworkManager` script
- Canvas with `LobbyUIManager` script + 4 panels wired in Inspector

### `Player.prefab`
- Add `NetworkObject` to root GO

### `1v1.unity`
- GO `GameManager`: `NetworkObject` + `GameManager` script
  - Assign: `timerImage`, `playerPrefab` (Player.prefab), `spawnPoints` (4 empty GOs)
- 4 empty GOs positioned as spawn points, assigned to `GameManager.spawnPoints[]`

### Canvas.prefab (rewire buttons)
- Body color buttons (4): target → `LobbyUIManager`, method → `OnBodyColorButton(int)`, args 0/1/2/3
- Weapon color buttons (4): target → `LobbyUIManager`, method → `OnWeaponColorButton(int)`, args 0/1/2/3

### Build Settings
- Scene 0: `GameMenu`
- Scene 1: `1v1`

## Color Enum

`Red=0, Blue=1, Green=2, Yellow=3` — used as int everywhere in NetworkVariables.
