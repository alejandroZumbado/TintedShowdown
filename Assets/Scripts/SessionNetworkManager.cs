using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models; // Allocation, JoinAllocation, RelayServerEndpoint, AllocationUtils.ToRelayServerData
using UnityEngine;
using UnityEngine.SceneManagement;

// Handles UGS login, Relay room creation/joining, and scene transition to game.
// Persists across scenes (DontDestroyOnLoad).
public class SessionNetworkManager : MonoBehaviour
{
    public static SessionNetworkManager Instance { get; private set; }

    public event Action<string> OnRoomCreated;
    public event Action OnJoinedRoom;
    public event Action OnHostLeft;
    public event Action<string> OnError;
    public event Action<int, int> OnPlayerCountChanged;

    private int _maxPlayers;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[SNM] Awake");
    }

    private async Task InitServicesAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        Debug.Log($"[SNM] UGS ready. PlayerId={AuthenticationService.Instance.PlayerId}");
    }

    public async Task CreateRoomAsync(int maxPlayers)
    {
        try
        {
            await InitServicesAsync();

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[SNM] Relay allocation created. JoinCode={joinCode}");
            LogEndpoints(allocation.ServerEndpoints);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null) { OnError?.Invoke("UnityTransport no encontrado."); return; }

            // Must match PreferredConnectionType() below — UTP logs an error and silently
            // forces WebSocket mode anyway if this flag disagrees with the relay data, but
            // leaving it out of sync is undefined behavior we shouldn't rely on.
            transport.UseWebSockets = Application.platform == RuntimePlatform.WebGLPlayer;
            transport.SetRelayServerData(BuildHostRelayData(allocation));

            _maxPlayers = maxPlayers;
            GameManager.MaxPlayersTarget = maxPlayers;

            NetworkManager.Singleton.OnClientDisconnectCallback += HandleDisconnect;
            NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;

            bool started = NetworkManager.Singleton.StartHost();
            Debug.Log($"[SNM] StartHost returned: {started}");
            if (!started) { OnError?.Invoke("No se pudo iniciar el host."); return; }

            FindLobbyNetwork()?.Initialize(1, maxPlayers);
            OnRoomCreated?.Invoke(joinCode);
        }
        catch (Exception e)
        {
            // Full exception goes to the console for debugging — the player only needs
            // a short, friendly message, not a wall of stack-trace-looking text.
            Debug.LogError($"[SNM] CreateRoomAsync: {e}");
            OnError?.Invoke("No se pudo crear la sala. Probá de nuevo.");
        }
    }

    public async Task JoinRoomAsync(string joinCode)
    {
        try
        {
            await InitServicesAsync();

            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim().ToUpper());
            Debug.Log($"[SNM] Relay join succeeded for code={joinCode}");
            LogEndpoints(allocation.ServerEndpoints);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null) { OnError?.Invoke("UnityTransport no encontrado."); return; }

            transport.UseWebSockets = Application.platform == RuntimePlatform.WebGLPlayer;
            transport.SetRelayServerData(BuildClientRelayData(allocation));

            NetworkManager.Singleton.OnClientDisconnectCallback += HandleDisconnect;
            NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;

            bool started = NetworkManager.Singleton.StartClient();
            Debug.Log($"[SNM] StartClient returned: {started}");
            if (!started) { OnError?.Invoke("No se pudo iniciar el cliente."); return; }

            OnJoinedRoom?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SNM] JoinRoomAsync: {e}");
            OnError?.Invoke("Sala no encontrada o llena. Revisá el código.");
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[SNM] OnClientConnected clientId={clientId} IsServer={NetworkManager.Singleton.IsServer} count={NetworkManager.Singleton.ConnectedClients.Count}");

        if (!NetworkManager.Singleton.IsServer) return;

        int current = NetworkManager.Singleton.ConnectedClients.Count;
        FindLobbyNetwork()?.SetPlayerCount(current);
        OnPlayerCountChanged?.Invoke(current, _maxPlayers);

        if (current >= _maxPlayers)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            LoadGameScene();
        }
    }

    public void LoadGameScene()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        Debug.Log("[SNM] Loading 1v1...");
        NetworkManager.Singleton.SceneManager.LoadScene("Arena", LoadSceneMode.Single);
    }

    private void HandleDisconnect(ulong clientId)
    {
        Debug.Log($"[SNM] HandleDisconnect clientId={clientId}");

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            int current = NetworkManager.Singleton.ConnectedClients.Count;
            FindLobbyNetwork()?.SetPlayerCount(current);
            OnPlayerCountChanged?.Invoke(current, _maxPlayers);
        }
        else if (clientId == NetworkManager.ServerClientId)
        {
            Disconnect();
            OnHostLeft?.Invoke();
        }
    }

    public void Disconnect()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleDisconnect;
        NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        if (NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
        Debug.Log("[SNM] Disconnected");
    }

    // ─── Relay protocol selection ─────────────────────────────────────────────

    // Picks the connectionType string ("udp"/"dtls"/"wss") — the ONLY stable way to select
    // a Relay endpoint. Two things that look tempting instead both broke in production:
    //   - ep.Network (RelayServerEndpoint.NetworkOptions = Udp/Tcp) is the IP transport
    //     layer, not the secure/WebSocket distinction — a "Tcp" entry can BE the wss
    //     endpoint, so you can't tell wss from plain tcp by comparing this enum.
    //   - ep.Port used to reliably be 7777 (udp) / 443 (wss/dtls) — real allocations now
    //     hand out dynamic ports per endpoint, so matching by port silently falls through
    //     to "first available" (which picked a raw UDP endpoint on WebGL — browsers can't
    //     open a socket to that at all, only WebSocket — see [[tech_webgl_github_pages]]).
    private static string PreferredConnectionType()
    {
        if (Application.platform == RuntimePlatform.WebGLPlayer)
            return RelayServerEndpoint.ConnectionTypeWss; // browsers can only open WebSocket connections
        if (Application.isEditor)
            return RelayServerEndpoint.ConnectionTypeUdp; // DTLS handshake is unreliable in the Editor — see [[tech_relay_endpoint]]
        return RelayServerEndpoint.ConnectionTypeDtls;    // standalone builds: encrypted UDP
    }

    // ─── Relay data builders ──────────────────────────────────────────────────

    // AllocationUtils.ToRelayServerData (official Unity Services helper) builds the
    // RelayServerData directly from the connectionType string, including setting the
    // IsWebSocket flag — the manual NetworkEndpoint.Parse(host, port) approach used before
    // silently failed for wss endpoints anyway, since Parse expects a raw IP, not the
    // hostname (e.g. "...relay.uf.unity3d.com") wss endpoints are actually served from.
    private static RelayServerData BuildHostRelayData(Allocation a)
    {
        string connType = PreferredConnectionType();
        Debug.Log($"[SNM] Using connectionType={connType}");
        return a.ToRelayServerData(connType);
    }

    private static RelayServerData BuildClientRelayData(JoinAllocation a)
    {
        string connType = PreferredConnectionType();
        Debug.Log($"[SNM] Using connectionType={connType}");
        return a.ToRelayServerData(connType);
    }

    // ─── Internal helpers ─────────────────────────────────────────────────────

    // Finds LobbyNetwork via SpawnManager — scoped to THIS NetworkManager in MPPM
    private LobbyNetwork FindLobbyNetwork()
    {
        if (NetworkManager.Singleton?.SpawnManager == null)
        {
            Debug.LogWarning("[SNM] SpawnManager is null");
            return null;
        }
        foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
        {
            if (kvp.Value.TryGetComponent<LobbyNetwork>(out var ln)) return ln;
        }
        Debug.LogWarning("[SNM] LobbyNetwork not found in SpawnedObjects");
        return null;
    }

    private static void LogEndpoints(IReadOnlyList<RelayServerEndpoint> endpoints)
    {
        foreach (var ep in endpoints)
            Debug.Log($"[SNM]   endpoint: connectionType={ep.ConnectionType} secure={ep.Secure} {ep.Host}:{ep.Port}");
    }
}
