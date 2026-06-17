using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Scene-placed NetworkBehaviour in GameMenu.unity.
// Syncs lobby player count to all clients via ClientRpc + NetworkVariables.
// NetworkVariables store server-authoritative state; ClientRpc pushes UI updates immediately.
public class LobbyNetwork : NetworkBehaviour
{
    // Kept for compatibility but may point to wrong instance in MPPM Simulator mode.
    // Prefer NetworkManager.Singleton.SpawnManager for cross-player-safe lookups.
    public static LobbyNetwork Instance { get; private set; }

    // Server-authoritative values — clients read these on late-join snap
    public NetworkVariable<int> PlayerCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> MaxPlayers = new NetworkVariable<int>(
        2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        Instance = this;
        Debug.Log($"[LobbyNet] Awake on {(NetworkManager.Singleton != null ? (NetworkManager.Singleton.IsServer ? "server" : "client") : "pre-network")}");
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[LobbyNet] OnNetworkSpawn IsServer={IsServer} IsClient={IsClient} IsHost={IsHost}");

        if (!IsServer)
            StartCoroutine(SnapNextFrame()); // late-join clients: read synced values after 1 frame
    }

    // Waits one frame so NGO finishes writing initial NetworkVariable values before reading
    private IEnumerator SnapNextFrame()
    {
        yield return null;
        Debug.Log($"[LobbyNet] SnapNextFrame: PlayerCount={PlayerCount.Value} MaxPlayers={MaxPlayers.Value}");

        // FindFirstObjectByType is scoped per virtual player in MPPM Simulator mode
        var ui = Object.FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
            ui.UpdateWaitingRoom(PlayerCount.Value, MaxPlayers.Value);
        else
            Debug.LogWarning("[LobbyNet] SnapNextFrame: LobbyUIManager not found in scene");
    }

    // Sets both values atomically with a single RPC — avoids intermediate dirty state
    public void Initialize(int count, int max)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[LobbyNet] Initialize called on non-server!");
            return;
        }
        Debug.Log($"[LobbyNet] Initialize count={count} max={max}");
        PlayerCount.Value = count;
        MaxPlayers.Value = max;
        PushCountClientRpc(count, max);
    }

    // Updates player count and pushes to all clients
    public void SetPlayerCount(int count)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[LobbyNet] SetPlayerCount called on non-server!");
            return;
        }
        Debug.Log($"[LobbyNet] SetPlayerCount {count}/{MaxPlayers.Value}");
        PlayerCount.Value = count;
        PushCountClientRpc(count, MaxPlayers.Value);
    }

    // Fires on ALL clients (including host) immediately when server calls SetPlayerCount/Initialize
    [ClientRpc]
    private void PushCountClientRpc(int current, int max)
    {
        Debug.Log($"[LobbyNet] PushCountClientRpc received: {current}/{max}");

        // FindFirstObjectByType is safe in MPPM — scoped to current virtual player's scenes
        var ui = Object.FindFirstObjectByType<LobbyUIManager>();
        if (ui != null)
            ui.UpdateWaitingRoom(current, max);
        else
            Debug.LogWarning($"[LobbyNet] PushCountClientRpc: LobbyUIManager not found! current={current} max={max}");
    }
}
