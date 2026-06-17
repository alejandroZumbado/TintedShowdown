using System;
using System.Collections;
using TMPro;
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

    [Header("Wait Room")]
    [SerializeField] private TextMeshProUGUI joinCodeDisplay;
    [SerializeField] private Button copyCodeButton;
    [SerializeField] private TextMeshProUGUI waitStatusText;

    [Header("Join Room")]
    [SerializeField] private TMP_InputField joinCodeInput;

    [Header("Win Screen")]
    [SerializeField] private TextMeshProUGUI winnerText;
    [SerializeField] private Button backToMenuButton;

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
        ShowPanel(menuPanel);
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
    }

    public void ShowWinners(int[] winnerSlots)
    {
        winPanel.SetActive(true);
        if (winnerSlots.Length == 1)
            winnerText.text = $"¡Jugador {winnerSlots[0]} ganó!";
        else
        {
            string names = string.Join(" y ", Array.ConvertAll(winnerSlots, s => $"Jugador {s}"));
            winnerText.text = $"¡{names} ganaron!";
        }
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
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    private void ShowError(string msg)
    {
        if (errorText == null) return;
        errorText.text = msg;
        errorText.gameObject.SetActive(true);
    }
}
