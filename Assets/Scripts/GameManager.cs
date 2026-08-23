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

    // Set by SessionNetworkManager.CreateSoloRoomAsync before StartHost() — how many
    // extra bot players GameManager spawns itself alongside the real connected clients.
    // Must be reset back to 0 by any normal multiplayer room creation, or a later real
    // session would spawn leftover bots too.
    public static int BotCountToSpawn = 0;

    [Header("Scene references — assign in Inspector")]
    [SerializeField] private Image timerImage;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints; // one per possible player slot

    [SerializeField, Range(1f, 5f)] private float roundDuration = 3f;

    private const int WinScore = 10;

    // Chance (per bot, per round) that a bot skips the live in-round updates below and
    // instead reveals its real pick only in the last instant before the round is scored.
    private const float SnipeChance = 0.15f;

    // Picked without repeats per match (see PickBotNames) so "Bot 1/2/3" doesn't show up
    // on the win screen — plenty of names here so repeats across separate matches are rare.
    private static readonly string[] BotNamePool =
    {
        "Byte", "Chispa", "Rayo", "Nova", "Vector", "Pixel", "Cobre", "Neon", "Circuito", "Turbo",
        "Volt", "Kilo", "Zeta", "Omega", "Fusible", "Chip", "Rex", "Sable", "Dinamo", "Halcon",
        "Ceniza", "Titanio", "Cuarzo", "Onix", "Jade", "Cobalto", "Plasma", "Cyborg", "Robo", "Servo",
        "Motor", "Rueda", "Fenix", "Draco", "Lobo", "Puma", "Tigre", "Cometa", "Estatico", "Rafaga",
        "Bruma", "Eco", "Nimbo", "Solex", "Kraken", "Cipher", "Vortex", "Ámbar", "Grafito", "Lumen",
    };

    private static List<string> PickBotNames(int count)
    {
        var pool = new List<string>(BotNamePool);
        var picked = new List<string>(count);
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int idx = Random.Range(0, pool.Count);
            picked.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return picked;
    }

    private readonly List<ActionPlayerManager> players = new List<ActionPlayerManager>();
    private readonly List<BotController> bots = new List<BotController>();
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

        // Solo mode: fill the rest of the roster with bots — same prefab, spawned with
        // no explicit owner (defaults to the server), just marked so ActionPlayerManager
        // knows not to treat them as the local human player.
        var botNames = PickBotNames(BotCountToSpawn);
        for (int i = 0; i < BotCountToSpawn; i++)
        {
            bool hasPoint = slot < spawnPoints.Length;
            Vector3 spawnPos = hasPoint ? spawnPoints[slot].position : Vector3.zero;
            Quaternion spawnRot = hasPoint ? spawnPoints[slot].rotation : Quaternion.identity;
            var obj = Instantiate(playerPrefab, spawnPos, spawnRot);
            var apm = obj.GetComponent<ActionPlayerManager>();
            apm.MarkAsBot(i < botNames.Count ? botNames[i] : "Bot"); // before Spawn() so OnNetworkSpawn sees it
            var netObj = obj.GetComponent<NetworkObject>();
            netObj.Spawn();
            var bot = new BotController(apm);
            bots.Add(bot);
            slot++;
        }
        if (bots.Count > 0)
            foreach (var bot in bots)
                bot.Decide(players); // criteria-driven starting colors, not just random
    }

    public override void OnNetworkDespawn()
    {
        // Reset all server-side state so a future scene load starts from a clean slate
        gameRunning = false;
        playersSpawned = false;
        players.Clear();
        bots.Clear();
        BotCountToSpawn = 0;
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

        // A match with 0-1 players left can never resolve (nobody left to score off of) —
        // previously this just softlocked everyone still connected. Ending it immediately
        // and reusing the win screen gives them the same "Volver al Menú" exit it already
        // has, instead of a dedicated new panel/button for the exact same purpose.
        if (gameRunning && players.Count <= 1)
        {
            gameRunning = false;
            if (roundLoopCoroutine != null) StopCoroutine(roundLoopCoroutine);
            ShowWinnersClientRpc("Los demás jugadores se desconectaron.");
        }
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
        // Runs exactly once per match/restart (RegisterPlayer and RestartGame both guard
        // against re-entering this coroutine) — the right place to count "a match started"
        // for MatchStats, unlike StartGameClientRpc which fires twice on a "Jugar de nuevo".
        MatchStartedClientRpc(BotCountToSpawn > 0);
        yield return new WaitForSeconds(3f); // brief countdown before first round
        StartGameClientRpc();
        BeginTimerClientRpc();
        if (bots.Count > 0) StartCoroutine(RunBotsForRound(roundDuration));
        roundLoopCoroutine = StartCoroutine(RoundLoop());
    }

    private IEnumerator RoundLoop()
    {
        while (gameRunning)
        {
            yield return new WaitForSeconds(roundDuration);
            EvaluateRound();
            if (gameRunning && bots.Count > 0) StartCoroutine(RunBotsForRound(roundDuration));
        }
    }

    // Solo mode: makes bots react live through the round instead of only deciding once.
    // Most bots re-decide roughly every second, reacting to whatever colors opponents
    // currently show — same as a human re-clicking buttons before the timer runs out.
    // A random subset of bots each round instead hold their previous colors and only
    // reveal their real pick in the last instant, right before EvaluateRound scores the
    // round (a "sniped" late change).
    private IEnumerator RunBotsForRound(float duration)
    {
        var snipe = new bool[bots.Count];
        for (int i = 0; i < bots.Count; i++)
            snipe[i] = Random.value < SnipeChance;

        // Weapon and body each run on their OWN independent per-bot timer (random phase
        // + interval) instead of one shared tick for everyone — a single shared tick
        // made every bot flip both colors in the exact same instant, which read as
        // robotic/synced rather than alive.
        for (int i = 0; i < bots.Count; i++)
        {
            if (snipe[i]) continue;
            StartCoroutine(BotStatLoop(bots[i], duration, isWeapon: true));
            StartCoroutine(BotStatLoop(bots[i], duration, isWeapon: false));
        }

        yield return new WaitForSeconds(duration);
        for (int i = 0; i < bots.Count; i++)
            if (snipe[i]) bots[i].Decide(players);
    }

    // One color (weapon OR body) of one bot, re-decided on its own random cadence for
    // the rest of the round. Random starting phase (not just random interval) is what
    // actually decorrelates bots from each other and a bot's weapon from its own body —
    // without it every loop's first tick still lands at the same moment.
    private IEnumerator BotStatLoop(BotController bot, float duration, bool isWeapon)
    {
        float elapsed = Random.Range(0f, 0.9f);
        yield return new WaitForSeconds(Mathf.Min(elapsed, duration));

        while (elapsed < duration)
        {
            if (isWeapon) bot.DecideWeapon(players); else bot.DecideBody(players);

            float tick = Random.Range(0.7f, 1.3f);
            if (elapsed + tick >= duration) yield break;

            yield return new WaitForSeconds(tick);
            elapsed += tick;
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
    private void MatchStartedClientRpc(bool isSolo)
    {
        Object.FindFirstObjectByType<LobbyUIManager>()?.RecordMatchStarted(isSolo);
    }

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
