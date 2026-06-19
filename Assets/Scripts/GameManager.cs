using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Host-authoritative: manages rounds, scoring, win detection, and player spawning.
// Must have a NetworkObject component on the same GameObject (scene-placed).
public class GameManager : NetworkBehaviour
{
    // Set by SessionNetworkManager before StartHost(), so GameManager knows when to start
    public static int MaxPlayersTarget = 2;

    [Header("Scene references — assign in Inspector")]
    [SerializeField] private Image timerImage;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints; // one per possible player slot

    [SerializeField, Range(1f, 5f)] private float roundDuration = 3f;

    private const int WinScore = 10;

    private readonly List<ActionPlayerManager> players = new List<ActionPlayerManager>();
    private bool gameRunning = false;
    private bool playersSpawned = false; // guards against spawning the roster twice
    private Coroutine timerCoroutine;
    private Coroutine roundLoopCoroutine;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        if (playersSpawned) return; // OnNetworkSpawn must only ever build the roster once
        playersSpawned = true;

        // Defensive cleanup: destroy any leftover ActionPlayerManager left manually in the
        // scene (e.g. forgot to re-run "Setup All"). Without this they get auto-spawned by
        // NGO's scene management alongside the real per-client players spawned below.
        DespawnStrayPlayers();

        // Spawn one player object for every connected client
        int slot = 0;
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            bool hasPoint = slot < spawnPoints.Length;
            Vector3 spawnPos = hasPoint ? spawnPoints[slot].position : Vector3.zero;
            Quaternion spawnRot = hasPoint ? spawnPoints[slot].rotation : Quaternion.identity;
            var obj = Instantiate(playerPrefab, spawnPos, spawnRot);
            var netObj = obj.GetComponent<NetworkObject>();
            netObj.SpawnAsPlayerObject(client.ClientId);
            slot++;
        }
    }

    public override void OnNetworkDespawn()
    {
        // Reset all server-side state so a future scene load starts from a clean slate
        gameRunning = false;
        playersSpawned = false;
        players.Clear();
        if (timerCoroutine != null) StopCoroutine(timerCoroutine);
        if (roundLoopCoroutine != null) StopCoroutine(roundLoopCoroutine);
    }

    private void DespawnStrayPlayers()
    {
        var stray = FindObjectsByType<ActionPlayerManager>(FindObjectsSortMode.None);
        foreach (var p in stray)
        {
            Debug.LogWarning($"[GameManager] Destroying stray player left in scene: {p.gameObject.name}");
            if (p.TryGetComponent<NetworkObject>(out var no) && no.IsSpawned)
                no.Despawn(true);
            else
                Destroy(p.gameObject);
        }
    }

    // Called by ActionPlayerManager.OnNetworkSpawn on the server side
    public void RegisterPlayer(ActionPlayerManager player)
    {
        if (!IsServer) return;
        if (players.Contains(player)) return; // idempotent — never register the same player twice

        players.Add(player);
        player.playerSlot.Value = players.Count; // assign slot 1–4

        // Start game when all players registered
        if (players.Count == MaxPlayersTarget && !gameRunning)
            StartCoroutine(StartGameDelayed());
    }

    public void UnregisterPlayer(ActionPlayerManager player)
    {
        if (!IsServer) return;
        players.Remove(player);
    }

    // Called by the host's "Jugar de nuevo" button — keeps the same connected roster
    // (no Relay/NetworkManager reconnection) and just resets scores and restarts rounds.
    public void RestartGame()
    {
        if (!IsServer) return;
        if (gameRunning) return; // a match is already in progress, ignore repeat clicks

        foreach (var player in players)
            player.score.Value = 0;

        StartGameClientRpc(); // hide the win screen immediately for everyone
        StartCoroutine(StartGameDelayed());
    }

    private IEnumerator StartGameDelayed()
    {
        gameRunning = true;
        yield return new WaitForSeconds(3f); // brief countdown before first round
        StartGameClientRpc();
        BeginTimerClientRpc();
        roundLoopCoroutine = StartCoroutine(RoundLoop());
    }

    private IEnumerator RoundLoop()
    {
        while (gameRunning)
        {
            yield return new WaitForSeconds(roundDuration);
            EvaluateRound();
        }
    }

    // Server computes who scored this round, checks win condition
    private void EvaluateRound()
    {
        var winnerNames = new List<string>();

        foreach (var player in players)
        {
            int gained = 0;
            bool tookDamage = false;
            foreach (var other in players)
            {
                if (other == player) continue;
                // Score if your weapon matches the enemy's body color
                if (other.bodyColor.Value == player.weaponColor.Value)
                    gained++;
                // Mirror check: did the enemy's weapon match your body color this round?
                if (other.weaponColor.Value == player.bodyColor.Value)
                    tookDamage = true;
            }
            player.score.Value += gained;
            player.PlayRoundResultClientRpc(gained > 0, tookDamage);

            if (player.score.Value >= WinScore)
            {
                string name = player.playerName.Value.ToString();
                winnerNames.Add(string.IsNullOrWhiteSpace(name) ? $"Jugador {player.playerSlot.Value}" : name);
            }
        }

        if (winnerNames.Count > 0)
        {
            gameRunning = false;
            // NGO RPCs can't serialize string[] — format the full message server-side
            // and send a single string instead.
            string message = winnerNames.Count == 1
                ? $"¡{winnerNames[0]} ganó!"
                : $"¡{string.Join(" y ", winnerNames)} ganaron!";
            ShowWinnersClientRpc(message);
        }
        else
        {
            // No winner yet — restart visual timer
            BeginTimerClientRpc();
        }
    }

    // ─── ClientRpcs (server → all clients) ───────────────────────────────────

    [ClientRpc]
    private void StartGameClientRpc()
    {
        // FindFirstObjectByType is MPPM-safe — avoids static Instance cross-player confusion
        Object.FindFirstObjectByType<LobbyUIManager>()?.ShowGamePanel();
    }

    [ClientRpc]
    private void BeginTimerClientRpc()
    {
        if (timerCoroutine != null) StopCoroutine(timerCoroutine);
        if (timerImage != null)
            timerCoroutine = StartCoroutine(RunTimer());
    }

    [ClientRpc]
    private void ShowWinnersClientRpc(string message)
    {
        Object.FindFirstObjectByType<LobbyUIManager>()?.ShowWinners(message);
    }

    // Visual countdown that runs on every client independently
    private IEnumerator RunTimer()
    {
        float elapsed = 0f;
        timerImage.fillAmount = 1f;
        while (elapsed < roundDuration)
        {
            elapsed += Time.deltaTime;
            timerImage.fillAmount = 1f - (elapsed / roundDuration);
            yield return null;
        }
        timerImage.fillAmount = 0f;
    }
}
