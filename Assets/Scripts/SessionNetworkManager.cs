using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models; // Allocation, JoinAllocation, RelayServerEndpoint
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
            Debug.LogError($"[SNM] CreateRoomAsync: {e}");
            OnError?.Invoke($"Error al crear sala: {e.Message}");
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
            OnError?.Invoke($"Código inválido o sala llena: {e.Message}");
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

    // Selects the right endpoint by port — avoids relying on NetworkOptions enum values
    // which differ between relay package versions.
    // Unity Relay standard ports: UDP = 7777, DTLS/WSS = 443, WS = 80
    private static (NetworkEndpoint endpoint, bool isSecure) PickEndpoint(IReadOnlyList<RelayServerEndpoint> endpoints)
    {
        // Log all options so we can diagnose if the wrong one is chosen
        foreach (var ep in endpoints)
            Debug.Log($"[SNM]   available: Network={ep.Network} {ep.Host}:{ep.Port}");

        RelayServerEndpoint chosen = null;
        bool isSecure;

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            // WebGL must use WebSockets (WSS, port 443)
            foreach (var ep in endpoints) if (ep.Port == 443) { chosen = ep; break; }
            isSecure = true;
        }
        else if (Application.isEditor)
        {
            // Editor: use plain UDP (port 7777) — DTLS handshake is unreliable in the Editor
            foreach (var ep in endpoints) if (ep.Port == 7777) { chosen = ep; break; }
            isSecure = false;
        }
        else
        {
            // Standalone builds: use DTLS (port 443, encrypted)
            foreach (var ep in endpoints) if (ep.Port == 443) { chosen = ep; break; }
            isSecure = true;
        }

        if (chosen == null)
        {
            Debug.LogWarning("[SNM] Preferred endpoint port not found, using first available");
            chosen   = endpoints[0];
            isSecure = false;
        }

        Debug.Log($"[SNM] Selected: {chosen.Network} {chosen.Host}:{chosen.Port} secure={isSecure}");
        return (NetworkEndpoint.Parse(chosen.Host, (ushort)chosen.Port), isSecure);
    }

    // ─── Relay data builders ──────────────────────────────────────────────────

    private static RelayServerData BuildHostRelayData(Allocation a)
    {
        var (endpoint, isSecure) = PickEndpoint(a.ServerEndpoints);
        var id   = RelayAllocationId.FromByteArray(a.AllocationIdBytes);
        var conn = RelayConnectionData.FromByteArray(a.ConnectionData);
        var key  = RelayHMACKey.FromByteArray(a.Key);
        // Host passes its own ConnectionData twice (no separate host connection data for host)
        return new RelayServerData(ref endpoint, 0, ref id, ref conn, ref conn, ref key, isSecure);
    }

    private static RelayServerData BuildClientRelayData(JoinAllocation a)
    {
        var (endpoint, isSecure) = PickEndpoint(a.ServerEndpoints);
        var id   = RelayAllocationId.FromByteArray(a.AllocationIdBytes);
        var conn = RelayConnectionData.FromByteArray(a.ConnectionData);
        var host = RelayConnectionData.FromByteArray(a.HostConnectionData);
        var key  = RelayHMACKey.FromByteArray(a.Key);
        return new RelayServerData(ref endpoint, 0, ref id, ref conn, ref host, ref key, isSecure);
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
            Debug.Log($"[SNM]   endpoint: {ep.Network} {ep.Host}:{ep.Port}");
    }
}
