using System;
using System.Collections;
using System.Runtime.InteropServices;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Controls all UI panels: main menu, create room, waiting room, game, and win screen.
// Bridges Canvas buttons to the local player's ActionPlayerManager.
public class LobbyUIManager : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    // GUIUtility.systemCopyBuffer isn't wired to the real browser clipboard on WebGL —
    // it's a plain in-memory field there. Assets/Plugins/WebGL/ClipboardPlugin.jslib
    // bridges to navigator.clipboard.writeText, which actually reaches the OS clipboard.
    [DllImport("__Internal")]
    private static extern void CopyToClipboard(string text);
#endif

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

    [Header("Create Room — player count selector (index 0→2 players, 1→3, 2→4)")]
    [SerializeField] private Button[] maxPlayerButtons;
    [SerializeField] private Button createRoomButton;

    [Header("Join Room")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private Button joinRoomButton;

    [Header("Win Screen")]
    [SerializeField] private TextMeshProUGUI winnerText;
    [SerializeField] private Button backToMenuButton;
    [SerializeField] private Button playAgainButton;
    [SerializeField] private GameObject waitHostText;

    [Header("Error feedback")]
    [SerializeField] private GameObject errorToastRoot;
    [SerializeField] private TextMeshProUGUI errorText;

    private ActionPlayerManager localPlayer;
    private int selectedMaxPlayers = 2;
    private string _joinCode = string.Empty;
    private Coroutine _errorHideCoroutine;
    private const float ErrorToastSeconds = 3f;

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
        if (errorToastRoot != null) errorToastRoot.SetActive(false);

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
        Debug.Log($"[LobbyUI] OnCopyCodeButton joinCode={_joinCode}");
        if (string.IsNullOrEmpty(_joinCode)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("[LobbyUI] Calling CopyToClipboard (WebGL)");
        CopyToClipboard(_joinCode);
#else
        GUIUtility.systemCopyBuffer = _joinCode;
#endif
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

    public void ShowCreatePanel()
    {
        ShowPanel(createPanel);
        UpdateMaxPlayersHighlight(); // reflect the default (2) the first time the panel opens
    }

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
        UpdateMaxPlayersHighlight();
    }

    // Colors of the 2/3/4-player buttons — default matches MakeButton's base Image
    // color, Selected is a bright accent so the current pick reads clearly at any scale
    // (an Outline effect was tried first but was too subtle to notice — see setup script).
    private static readonly Color32 MaxPlayersDefaultColor  = new Color32(255, 255, 255, 200);
    private static readonly Color32 MaxPlayersSelectedColor = new Color32(255, 205, 40, 230);

    // Highlights the currently-selected player-count button — buttons are ordered 2/3/4
    // players, matching maxPlayerButtons[0..2].
    private void UpdateMaxPlayersHighlight()
    {
        if (maxPlayerButtons == null) return;
        for (int i = 0; i < maxPlayerButtons.Length; i++)
        {
            int count = i + 2;
            var image = maxPlayerButtons[i] != null ? maxPlayerButtons[i].GetComponent<Image>() : null;
            if (image != null) image.color = count == selectedMaxPlayers ? MaxPlayersSelectedColor : MaxPlayersDefaultColor;
        }
    }

    public async void OnCreateRoomButton()
    {
        Debug.Log($"[LobbyUI] OnCreateRoomButton selectedMaxPlayers={selectedMaxPlayers}");
        string original = BeginLoading(createRoomButton, "Conectando...");
        try
        {
            await SessionNetworkManager.Instance.CreateRoomAsync(selectedMaxPlayers);
        }
        catch (Exception e)
        {
            // SessionNetworkManager already catches and reports its own failures via
            // OnError — reaching here means something unexpected slipped through.
            Debug.LogError($"[LobbyUI] OnCreateRoomButton: {e}");
            ShowError("No se pudo crear la sala. Probá de nuevo.");
        }
        finally
        {
            EndLoading(createRoomButton, original);
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
        string original = BeginLoading(joinRoomButton, "Conectando...");
        try
        {
            await SessionNetworkManager.Instance.JoinRoomAsync(code);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyUI] OnJoinRoomButton: {e}");
            ShowError("Sala no encontrada o llena. Revisá el código.");
        }
        finally
        {
            EndLoading(joinRoomButton, original);
        }
    }

    // Disables the button (doubles as a double-click guard) and swaps its label to a
    // loading message — CreateRoomAsync/JoinRoomAsync take a few seconds (UGS auth +
    // Relay allocation) with no other visual feedback otherwise. Returns the label's
    // original text so EndLoading can put it back.
    private static string BeginLoading(Button button, string loadingText)
    {
        if (button == null) return null;
        button.interactable = false;
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null) return null;
        string original = label.text;
        label.text = loadingText;
        return original;
    }

    private static void EndLoading(Button button, string originalText)
    {
        if (button == null) return;
        button.interactable = true;
        if (originalText == null) return;
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = originalText;
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
        HideError();
    }

    // Toast lives outside any panel (see TintedShowdownSetup.EnsureErrorToast) so it's
    // visible regardless of which panel is active, and disappears on its own instead of
    // sitting there looking like a leftover console.log.
    private void ShowError(string msg)
    {
        if (errorText == null || errorToastRoot == null) return;
        errorText.text = msg;
        errorToastRoot.SetActive(true);

        if (_errorHideCoroutine != null) StopCoroutine(_errorHideCoroutine);
        _errorHideCoroutine = StartCoroutine(HideErrorAfterDelay());
    }

    private void HideError()
    {
        if (_errorHideCoroutine != null) StopCoroutine(_errorHideCoroutine);
        _errorHideCoroutine = null;
        if (errorToastRoot != null) errorToastRoot.SetActive(false);
    }

    private IEnumerator HideErrorAfterDelay()
    {
        yield return new WaitForSeconds(ErrorToastSeconds);
        errorToastRoot.SetActive(false);
        _errorHideCoroutine = null;
    }
}
