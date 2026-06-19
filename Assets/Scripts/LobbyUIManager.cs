using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Controls all UI panels: main menu, create room, waiting room, game, and win screen.
// Bridges Canvas buttons to the local player's ActionPlayerManager.
public class LobbyUIManager : MonoBehaviour
{
    public static LobbyUIManager Instance { get; private set; }

    [Header("Panels — assign all in Inspector")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject createPanel;
    [SerializeField] private GameObject waitPanel;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject namePanel;

    [Header("Name entry — shown once if no name is saved in PlayerPrefs")]
    [SerializeField] private TMP_InputField nameInput;

    private const string PlayerNameKey = "PlayerName";

    [Header("Wait Room")]
    [SerializeField] private TextMeshProUGUI joinCodeDisplay;
    [SerializeField] private Button copyCodeButton;
    [SerializeField] private TextMeshProUGUI waitStatusText;

    [Header("Join Room")]
    [SerializeField] private TMP_InputField joinCodeInput;

    [Header("Win Screen")]
    [SerializeField] private TextMeshProUGUI winnerText;
    [SerializeField] private Button backToMenuButton;
    [SerializeField] private Button playAgainButton;
    [SerializeField] private GameObject waitHostText;

    [Header("Error feedback")]
    [SerializeField] private TextMeshProUGUI errorText;

    private ActionPlayerManager localPlayer;
    private int selectedMaxPlayers = 2;
    private string _joinCode = string.Empty;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[LobbyUI] Awake, DontDestroyOnLoad set");
    }

    private void Start()
    {
        bool hasName = !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(PlayerNameKey, ""));
        ShowPanel(hasName ? menuPanel : namePanel);
        if (backToMenuButton != null) backToMenuButton.onClick.AddListener(OnBackToMenu);
        if (errorText != null) errorText.gameObject.SetActive(false);

        if (SessionNetworkManager.Instance == null)
        {
            Debug.LogError("[LobbyUI] SessionNetworkManager.Instance is null in Start!");
            return;
        }

        SessionNetworkManager.Instance.OnRoomCreated += code =>
        {
            _joinCode = code;
            joinCodeDisplay.text = $"Contraseña: {code}";
            if (copyCodeButton != null) copyCodeButton.gameObject.SetActive(true);
            ShowPanel(waitPanel);
            Debug.Log($"[LobbyUI] Room created, code={code}");
        };

        SessionNetworkManager.Instance.OnJoinedRoom += () =>
        {
            ShowPanel(waitPanel);
            Debug.Log("[LobbyUI] Joined room, showing waitPanel");
        };

        // Backup: host also receives player count via this event when OnClientConnected fires
        SessionNetworkManager.Instance.OnPlayerCountChanged += (cur, max) =>
        {
            Debug.Log($"[LobbyUI] OnPlayerCountChanged {cur}/{max}");
            UpdateWaitingRoom(cur, max);
        };

        SessionNetworkManager.Instance.OnHostLeft  += OnHostLeft;
        SessionNetworkManager.Instance.OnError     += msg =>
        {
            Debug.LogError($"[LobbyUI] Error: {msg}");
            ShowError(msg);
        };

        Debug.Log("[LobbyUI] Start: events subscribed");
    }

    // ─── Called by ActionPlayerManager when local player spawns ──────────────

    public void SetLocalPlayer(ActionPlayerManager player)
    {
        localPlayer = player;
    }

    // ─── Canvas button callbacks ──────────────────────────────────────────────

    public void OnBodyColorButton(int colorIndex)   => localPlayer?.ChangeColor(colorIndex);
    public void OnWeaponColorButton(int colorIndex) => localPlayer?.AttackColor(colorIndex);

    // ─── Lobby UI buttons ─────────────────────────────────────────────────────

    public void OnCopyCodeButton()
    {
        if (string.IsNullOrEmpty(_joinCode)) return;
        GUIUtility.systemCopyBuffer = _joinCode;
        StartCoroutine(CopyFeedback());
    }

    private IEnumerator CopyFeedback()
    {
        var label = copyCodeButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null) yield break;
        string original = label.text;
        label.text = "¡Copiado!";
        yield return new WaitForSeconds(1.5f);
        label.text = original;
    }

    public void ShowMenuPanel()   => ShowPanel(menuPanel);
    public void ShowCreatePanel() => ShowPanel(createPanel);

    // Called by the name panel's "Confirmar" button — only shown once, when
    // PlayerPrefs has no saved name yet.
    public void OnConfirmNameButton()
    {
        string name = nameInput != null ? nameInput.text.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Ingresa un nombre.");
            return;
        }
        // Must match the clamp in ActionPlayerManager (FixedString32Bytes only holds
        // ~28 UTF-8 bytes) so the saved name and the one actually shown in-game match.
        if (name.Length > 16) name = name.Substring(0, 16);
        PlayerPrefs.SetString(PlayerNameKey, name);
        PlayerPrefs.Save();
        ShowPanel(menuPanel);
    }

    public void SelectMaxPlayers(int count)
    {
        selectedMaxPlayers = count;
        Debug.Log($"[LobbyUI] Selected max players: {count}");
    }

    public async void OnCreateRoomButton()
    {
        try
        {
            Debug.Log($"[LobbyUI] OnCreateRoomButton selectedMaxPlayers={selectedMaxPlayers}");
            await SessionNetworkManager.Instance.CreateRoomAsync(selectedMaxPlayers);
        }
        catch (Exception e)
        {
            ShowError(e.Message);
        }
    }

    public async void OnJoinRoomButton()
    {
        string code = joinCodeInput != null ? joinCodeInput.text.Trim() : string.Empty;
        Debug.Log($"[LobbyUI] OnJoinRoomButton code='{code}'");

        if (string.IsNullOrWhiteSpace(code))
        {
            ShowError("Ingresa un código de sala.");
            return;
        }
        try
        {
            await SessionNetworkManager.Instance.JoinRoomAsync(code);
        }
        catch (Exception e)
        {
            ShowError(e.Message);
        }
    }

    // ─── Called by LobbyNetwork.PushCountClientRpc (and OnPlayerCountChanged fallback) ──

    public void UpdateWaitingRoom(int current, int max)
    {
        Debug.Log($"[LobbyUI] UpdateWaitingRoom {current}/{max} — waitStatusText={waitStatusText != null}");
        if (waitStatusText != null)
            waitStatusText.text = $"Jugadores: {current}/{max}";
    }

    // ─── Called by GameManager via ClientRpc after scene loads ───────────────

    public void ShowGamePanel()
    {
        menuPanel.SetActive(false);
        createPanel.SetActive(false);
        waitPanel.SetActive(false);
        winPanel.SetActive(false);
        namePanel.SetActive(false);
    }

    // message comes fully formatted from GameManager (e.g. "¡Ana y Luis ganaron!") —
    // NGO RPCs can't serialize string[], so the pluralization happens server-side.
    public void ShowWinners(string message)
    {
        winPanel.SetActive(true);
        winnerText.text = message;

        // Only the host can restart — same connected roster, no reconnection needed
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        if (playAgainButton != null) playAgainButton.gameObject.SetActive(isHost);
        if (waitHostText != null) waitHostText.SetActive(!isHost);
    }

    // Called by the "Jugar de nuevo" button — host only (button is hidden for guests)
    public void OnPlayAgainButton()
    {
        UnityEngine.Object.FindFirstObjectByType<GameManager>()?.RestartGame();
    }

    // ─── Internal helpers ─────────────────────────────────────────────────────

    private void OnHostLeft()
    {
        winnerText.text = "El host se desconectó.";
        winPanel.SetActive(true);
    }

    public void OnBackToMenuPublic() => OnBackToMenu();

    private void OnBackToMenu()
    {
        SessionNetworkManager.Instance.Disconnect();
        localPlayer = null;
        ShowPanel(menuPanel);
        SceneManager.LoadScene("GameMenu");
    }

    private void ShowPanel(GameObject target)
    {
        menuPanel.SetActive(target == menuPanel);
        createPanel.SetActive(target == createPanel);
        waitPanel.SetActive(target == waitPanel);
        winPanel.SetActive(target == winPanel);
        namePanel.SetActive(target == namePanel);
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    private void ShowError(string msg)
    {
        if (errorText == null) return;
        errorText.text = msg;
        errorText.gameObject.SetActive(true);
    }
}
