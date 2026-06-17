using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Host-authoritative: manages rounds, scoring, win detection, and player spawning.
// Must have a NetworkObject component on the same GameObject (scene-placed).
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

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
    private Coroutine timerCoroutine;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // Spawn one player object for every connected client
        int slot = 0;
        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            Vector3 spawnPos = slot < spawnPoints.Length ? spawnPoints[slot].position : Vector3.zero;
            var obj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
            var netObj = obj.GetComponent<NetworkObject>();
            netObj.SpawnAsPlayerObject(client.ClientId);
            slot++;
        }
    }

    // Called by ActionPlayerManager.OnNetworkSpawn on the server side
    public void RegisterPlayer(ActionPlayerManager player)
    {
        if (!IsServer) return;

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

    private IEnumerator StartGameDelayed()
    {
        gameRunning = true;
        yield return new WaitForSeconds(3f); // brief countdown before first round
        StartGameClientRpc();
        BeginTimerClientRpc();
        StartCoroutine(RoundLoop());
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
        var winnerSlots = new List<int>();

        foreach (var player in players)
        {
            int gained = 0;
            foreach (var other in players)
            {
                if (other == player) continue;
                // Score if your weapon matches the enemy's body color
                if (other.bodyColor.Value == player.weaponColor.Value)
                    gained++;
            }
            player.score.Value += gained;

            if (player.score.Value >= WinScore)
                winnerSlots.Add(player.playerSlot.Value);
        }

        if (winnerSlots.Count > 0)
        {
            gameRunning = false;
            ShowWinnersClientRpc(winnerSlots.ToArray());
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
    private void ShowWinnersClientRpc(int[] winnerSlots)
    {
        Object.FindFirstObjectByType<LobbyUIManager>()?.ShowWinners(winnerSlots);
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
