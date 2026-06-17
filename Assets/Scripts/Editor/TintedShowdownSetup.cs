// Run once: Tinted Showdown → Setup All
// Automates scene setup, prefab config, and build settings.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;

public static class TintedShowdownSetup
{
    // ── Change these constants if you rename files ──────────────────────────
    private const string ArenaSceneName    = "Arena";          // used in LoadScene()
    private const string PlayerPrefabPath  = "Assets/Prefabs/Player.prefab";
    private const string CanvasPrefabPath  = "Assets/Prefabs/Canvas.prefab";
    private const string MenuScenePath     = "Assets/Scenes/GameMenu.unity";
    private const string ArenaScenePath    = "Assets/Scenes/Arena.unity";
    private const string OldArenaScenePath = "Assets/Scenes/1v1.unity";   // renamed automatically

    // Circle radius for spawn points (meters from center)
    private const float SpawnRadius = 5f;

    [MenuItem("Tinted Showdown/Setup All (run once)")]
    public static void SetupAll()
    {
        if (!EditorUtility.DisplayDialog("Setup Tinted Showdown",
            "Modifica prefabs, GameMenu.unity y Arena.unity (antes 1v1).\n\n" +
            "Ejecuta solo una vez antes de entrar a Play Mode.",
            "Continuar", "Cancelar")) return;

        RenameArenaScene();        // 1v1.unity → Arena.unity if needed
        SetupPlayerPrefab();
        SetupCanvasPrefab();
        SetupGameMenuScene();
        SetupArenaScene();
        SetupBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("¡Listo!",
            "Setup completado.\n\n" +
            "Pasos manuales:\n" +
            "1. Project Settings → Services → vincular Unity Dashboard\n" +
            "2. Verifica que el Player.prefab tenga Renderer asignado en ActionPlayerManager",
            "OK");
    }

    // ─── Scene rename ─────────────────────────────────────────────────────────

    private static void RenameArenaScene()
    {
        bool arenaExists = System.IO.File.Exists(ArenaScenePath);
        bool oldExists   = System.IO.File.Exists(OldArenaScenePath);

        if (!arenaExists && oldExists)
        {
            string err = AssetDatabase.MoveAsset(OldArenaScenePath, ArenaScenePath);
            if (string.IsNullOrEmpty(err))
                Debug.Log("[Setup] Renamed 1v1.unity → Arena.unity");
            else
                Debug.LogError($"[Setup] Rename failed: {err}");
            AssetDatabase.Refresh();
        }
        else if (!arenaExists && !oldExists)
        {
            // Neither exists — create an empty scene
            var newScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(newScene, ArenaScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[Setup] Created Arena.unity (neither Arena nor 1v1 existed)");
        }
        else
        {
            Debug.Log("[Setup] Arena.unity already exists, skipping rename");
        }
    }

    // ─── Player.prefab ────────────────────────────────────────────────────────

    private static void SetupPlayerPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null) { Debug.LogError($"[Setup] Not found: {PlayerPrefabPath}"); return; }

        using var scope = new PrefabUtility.EditPrefabContentsScope(PlayerPrefabPath);
        var root = scope.prefabContentsRoot;

        if (root.GetComponent<NetworkObject>() == null)
        {
            root.AddComponent<NetworkObject>();
            Debug.Log("[Setup] NetworkObject added to Player.prefab");
        }
    }

    // ─── Canvas.prefab: ColorButtonProxy + rewire 8 buttons ──────────────────

    private static void SetupCanvasPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPrefabPath);
        if (prefab == null) { Debug.LogError($"[Setup] Not found: {CanvasPrefabPath}"); return; }

        using var scope = new PrefabUtility.EditPrefabContentsScope(CanvasPrefabPath);
        var root = scope.prefabContentsRoot;

        var proxy = root.GetComponent<ColorButtonProxy>() ?? root.AddComponent<ColorButtonProxy>();

        var nameToIndex = new System.Collections.Generic.Dictionary<string, int>
        {
            { "Red", 0 }, { "Blue", 1 }, { "Green", 2 }, { "Yellow", 3 }
        };

        int rewired = 0;
        foreach (var btn in root.GetComponentsInChildren<Button>(true))
        {
            if (!nameToIndex.TryGetValue(btn.gameObject.name, out int idx)) continue;

            var bso   = new SerializedObject(btn);
            var calls = bso.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            string old = calls.arraySize > 0
                ? calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue
                : string.Empty;

            string newMethod = old == "AttackColor" ? "OnWeaponColor" : "OnBodyColor";

            calls.ClearArray();
            calls.arraySize = 1;
            var c = calls.GetArrayElementAtIndex(0);
            c.FindPropertyRelative("m_Target").objectReferenceValue             = proxy;
            c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue      = "ColorButtonProxy, Assembly-CSharp";
            c.FindPropertyRelative("m_MethodName").stringValue                  = newMethod;
            c.FindPropertyRelative("m_Mode").intValue                           = 3; // int arg
            c.FindPropertyRelative("m_Arguments.m_IntArgument").intValue        = idx;
            c.FindPropertyRelative("m_CallState").intValue                      = 2; // RuntimeOnly
            bso.ApplyModifiedProperties();
            rewired++;
        }

        Debug.Log($"[Setup] Canvas.prefab: proxy + {rewired} buttons rewired");
    }

    // ─── GameMenu.unity ───────────────────────────────────────────────────────

    private static void SetupGameMenuScene()
    {
        var scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

        EnsureEventSystem();

        var networkRoot = FindOrCreate("NetworkRoot");
        var nm          = networkRoot.GetOrAdd<NetworkManager>();
        var utransport  = networkRoot.GetOrAdd<UnityTransport>();

        if (nm.NetworkConfig.NetworkTransport == null)
        {
            nm.NetworkConfig.NetworkTransport = utransport;
            EditorUtility.SetDirty(nm);
        }

        // Register Player prefab so NGO can spawn it on clients
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab != null)
        {
            bool already = false;
            foreach (var e in nm.NetworkConfig.Prefabs.Prefabs)
                if (e.Prefab == playerPrefab) { already = true; break; }
            if (!already)
            {
                nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
                EditorUtility.SetDirty(nm);
            }
        }

        FindOrCreate("SessionManager").GetOrAdd<SessionNetworkManager>();

        // LobbyNetwork: scene-placed NetworkObject that syncs lobby count to all clients
        var lobbyNetGO = FindOrCreate("LobbyNetwork");
        lobbyNetGO.GetOrAdd<NetworkObject>();
        lobbyNetGO.GetOrAdd<LobbyNetwork>();

        // Always recreate LobbyCanvas to reflect latest script/layout changes
        var existing = GameObject.Find("LobbyCanvas");
        if (existing != null) { Object.DestroyImmediate(existing); Debug.Log("[Setup] Old LobbyCanvas removed"); }
        CreateLobbyCanvas();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Setup] GameMenu.unity saved");
    }

    private static void CreateLobbyCanvas()
    {
        var canvasGO = new GameObject("LobbyCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        canvasGO.AddComponent<GraphicRaycaster>();
        var lobbyUI = canvasGO.AddComponent<LobbyUIManager>();

        var menuPanel   = MakePanel(canvasGO, "MenuPanel",   new Color32(30,  30,  30,  220));
        var createPanel = MakePanel(canvasGO, "CreatePanel", new Color32(20,  50,  80,  220));
        var waitPanel   = MakePanel(canvasGO, "WaitPanel",   new Color32(20,  70,  50,  220));
        var winPanel    = MakePanel(canvasGO, "WinPanel",    new Color32(80,  65,  10,  220));

        // ── MenuPanel ──
        MakeButton(menuPanel, "BtnShowCreate", "Crear Sala",  new Vector2(0,  80));
        var joinInput = MakeInputField(menuPanel, "JoinCodeInput", "Contraseña de sala...", new Vector2(0, -10));
        MakeButton(menuPanel, "BtnJoinConfirm", "Entrar",     new Vector2(0, -90));
        var errorText = MakeText(menuPanel, "ErrorText", "", new Vector2(0, -165), 22, new Color32(255, 80, 80, 255));

        // ── CreatePanel ──
        MakeText(createPanel, "Title", "¿Cuántos jugadores?", new Vector2(0, 130), 28, Color.white);
        var btn2p = MakeButton(createPanel, "Btn2P", "2 Jugadores", new Vector2(-160, 40));
        var btn3p = MakeButton(createPanel, "Btn3P", "3 Jugadores", new Vector2(    0, 40));
        var btn4p = MakeButton(createPanel, "Btn4P", "4 Jugadores", new Vector2(  160, 40));
        MakeButton(createPanel, "BtnCreate",     "Crear Sala",      new Vector2(    0, -40));
        MakeButton(createPanel, "BtnBackCreate", "← Volver",        new Vector2(    0, -110));

        // ── WaitPanel ──
        var joinCodeDisplay = MakeText(waitPanel, "JoinCodeDisplay", "Contraseña: ------",
                                       new Vector2(0, 90), 30, Color.white);
        var copyBtn        = MakeButton(waitPanel, "BtnCopyCode", "Copiar", new Vector2(0, 20));
        var waitStatusText = MakeText(waitPanel, "WaitStatusText", "Jugadores: 0/4",
                                       new Vector2(0, -55), 34, Color.white);
        MakeText(waitPanel, "WaitHint", "Esperando jugadores...", new Vector2(0, -115), 20,
                 new Color32(200, 200, 200, 200));
        copyBtn.SetActive(false); // shown when host has a code

        // ── WinPanel ──
        var winnerText = MakeText(winPanel, "WinnerText", "¡Jugador X ganó!", new Vector2(0, 80), 38, Color.yellow);
        var backBtn    = MakeButton(winPanel, "BtnBackToMenu", "Volver al Menú", new Vector2(0, -30));

        // Wire void buttons
        WireVoidButton(copyBtn.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.OnCopyCodeButton));
        WireVoidButton(backBtn.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.OnBackToMenuPublic));
        WireVoidButton(menuPanel.transform.Find("BtnShowCreate")?.GetComponent<Button>(),
                       lobbyUI, nameof(LobbyUIManager.ShowCreatePanel));
        WireVoidButton(menuPanel.transform.Find("BtnJoinConfirm")?.GetComponent<Button>(),
                       lobbyUI, nameof(LobbyUIManager.OnJoinRoomButton));
        WireVoidButton(createPanel.transform.Find("BtnCreate")?.GetComponent<Button>(),
                       lobbyUI, nameof(LobbyUIManager.OnCreateRoomButton));
        WireVoidButton(createPanel.transform.Find("BtnBackCreate")?.GetComponent<Button>(),
                       lobbyUI, nameof(LobbyUIManager.ShowMenuPanel));

        // Wire int buttons (player count selector)
        WireIntButton(btn2p.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.SelectMaxPlayers), 2);
        WireIntButton(btn3p.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.SelectMaxPlayers), 3);
        WireIntButton(btn4p.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.SelectMaxPlayers), 4);

        createPanel.SetActive(false);
        waitPanel.SetActive(false);
        winPanel.SetActive(false);

        // Assign serialized references to LobbyUIManager
        var so = new SerializedObject(lobbyUI);
        so.FindProperty("menuPanel").objectReferenceValue        = menuPanel;
        so.FindProperty("createPanel").objectReferenceValue      = createPanel;
        so.FindProperty("waitPanel").objectReferenceValue        = waitPanel;
        so.FindProperty("winPanel").objectReferenceValue         = winPanel;
        so.FindProperty("joinCodeDisplay").objectReferenceValue  = joinCodeDisplay;
        so.FindProperty("copyCodeButton").objectReferenceValue   = copyBtn.GetComponent<Button>();
        so.FindProperty("waitStatusText").objectReferenceValue   = waitStatusText;
        so.FindProperty("joinCodeInput").objectReferenceValue    = joinInput;
        so.FindProperty("winnerText").objectReferenceValue       = winnerText;
        so.FindProperty("backToMenuButton").objectReferenceValue = backBtn.GetComponent<Button>();
        so.FindProperty("errorText").objectReferenceValue        = errorText;
        so.ApplyModifiedProperties();

        Debug.Log("[Setup] LobbyCanvas created");
    }

    // ─── Arena.unity (ex 1v1) ─────────────────────────────────────────────────

    private static void SetupArenaScene()
    {
        var scene = EditorSceneManager.OpenScene(ArenaScenePath, OpenSceneMode.Single);

        // Remove any Player GOs left in the scene — players must only exist at runtime
        CleanupManualPlayers();

        // Arena needs its own EventSystem — GameMenu's is destroyed on scene load
        EnsureEventSystem();

        // GameManager
        var gmGO = GameObject.Find("GameManager") ?? new GameObject("GameManager");
        gmGO.GetOrAdd<NetworkObject>();
        var gm = gmGO.GetOrAdd<GameManager>();

        // Spawn points in a circle — players face the center
        var spawnRoot = FindOrCreate("SpawnPoints");
        var points    = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            string n  = $"SpawnPoint{i + 1}";
            var    sp = GameObject.Find(n) ?? new GameObject(n);
            sp.transform.SetParent(spawnRoot.transform);

            // Start at top (90°) and go clockwise in world space
            float angle = Mathf.PI / 2f - (2f * Mathf.PI * i) / 4f;
            float x = Mathf.Cos(angle) * SpawnRadius;
            float z = Mathf.Sin(angle) * SpawnRadius;
            sp.transform.position = new Vector3(x, 0f, z);

            // Rotate to face center
            var dir = -new Vector3(x, 0f, z).normalized;
            if (dir != Vector3.zero)
                sp.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            points[i] = sp.transform;
        }

        // Canvas.prefab (color buttons) — place if not already in scene
        EnsureCanvasInScene();

        // Timer radial fill image
        var timerImage = SetupTimerUI();

        // Assign all references to GameManager
        var so        = new SerializedObject(gm);
        var spawnProp = so.FindProperty("spawnPoints");
        spawnProp.arraySize = 4;
        for (int i = 0; i < 4; i++)
            spawnProp.GetArrayElementAtIndex(i).objectReferenceValue = points[i];

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab != null)
            so.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;

        if (timerImage != null)
            so.FindProperty("timerImage").objectReferenceValue = timerImage;

        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Setup] Arena.unity saved");
    }

    private static void EnsureCanvasInScene()
    {
        if (Object.FindFirstObjectByType<ColorButtonProxy>() != null)
        {
            Debug.Log("[Setup] Canvas.prefab already in Arena");
            return;
        }
        var canvasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPrefabPath);
        if (canvasPrefab == null) { Debug.LogError($"[Setup] Not found: {CanvasPrefabPath}"); return; }
        PrefabUtility.InstantiatePrefab(canvasPrefab);
        Debug.Log("[Setup] Canvas.prefab placed in Arena");
    }

    // Adds cleanup helper for manually placed Player objects
    private static void CleanupManualPlayers()
    {
        var found = Object.FindObjectsByType<ActionPlayerManager>(FindObjectsSortMode.None);
        int removed = 0;
        foreach (var pm in found)
        {
            Debug.Log($"[Setup] Removing manual player from scene: {pm.gameObject.name}");
            Object.DestroyImmediate(pm.gameObject);
            removed++;
        }
        if (removed > 0)
            Debug.Log($"[Setup] Removed {removed} manual player object(s) — players spawn at runtime");
    }

    private static Image SetupTimerUI()
    {
        // Always destroy and recreate so size/position stay correct after script changes
        var existing = GameObject.Find("TimerCanvas");
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
            Debug.Log("[Setup] Old TimerCanvas removed");
        }

        var canvasGO = new GameObject("TimerCanvas");
        var c = canvasGO.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;

        // ScaleWithScreenSize so the timer looks consistent across resolutions
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        var imgGO = new GameObject("TimerImage");
        imgGO.transform.SetParent(canvasGO.transform, false);
        var img    = imgGO.AddComponent<Image>();
        img.type       = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillAmount = 1f;
        img.color      = new Color(0.2f, 0.8f, 1f, 0.85f);

        // 90×90 at top-center, 10px below the top edge
        var rt = imgGO.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(90, 90);
        rt.anchorMin        = new Vector2(0.5f, 1f);
        rt.anchorMax        = new Vector2(0.5f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -10);

        Debug.Log("[Setup] TimerCanvas created");
        return img;
    }

    // ─── Build Settings ───────────────────────────────────────────────────────

    private static void SetupBuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MenuScenePath, true),   // index 0
            new EditorBuildSettingsScene(ArenaScenePath, true)   // index 1
        };
        Debug.Log("[Setup] Build Settings: GameMenu(0) + Arena(1)");
    }

    // ─── EventSystem ──────────────────────────────────────────────────────────

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<EventSystem>();
        esGO.AddComponent<StandaloneInputModule>();
        Debug.Log("[Setup] EventSystem created");
    }

    // ─── Button wiring ────────────────────────────────────────────────────────

    private static void WireVoidButton(Button btn, MonoBehaviour target, string method)
    {
        if (btn == null || target == null) return;
        var so    = new SerializedObject(btn);
        var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        calls.arraySize = 1;
        var c = calls.GetArrayElementAtIndex(0);
        c.FindPropertyRelative("m_Target").objectReferenceValue        = target;
        c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = $"{target.GetType().Name}, Assembly-CSharp";
        c.FindPropertyRelative("m_MethodName").stringValue             = method;
        c.FindPropertyRelative("m_Mode").intValue                      = 1; // void
        c.FindPropertyRelative("m_CallState").intValue                 = 2; // RuntimeOnly
        so.ApplyModifiedProperties();
    }

    private static void WireIntButton(Button btn, MonoBehaviour target, string method, int arg)
    {
        if (btn == null || target == null) return;
        var so    = new SerializedObject(btn);
        var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        calls.arraySize = 1;
        var c = calls.GetArrayElementAtIndex(0);
        c.FindPropertyRelative("m_Target").objectReferenceValue              = target;
        c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue       = $"{target.GetType().Name}, Assembly-CSharp";
        c.FindPropertyRelative("m_MethodName").stringValue                   = method;
        c.FindPropertyRelative("m_Mode").intValue                            = 3; // int
        c.FindPropertyRelative("m_Arguments.m_IntArgument").intValue         = arg;
        c.FindPropertyRelative("m_CallState").intValue                       = 2; // RuntimeOnly
        so.ApplyModifiedProperties();
    }

    // ─── UI factory helpers ───────────────────────────────────────────────────

    private static GameObject MakePanel(GameObject parent, string name, Color32 color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var img   = go.AddComponent<Image>();
        img.color = color;
        var rt    = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
    }

    private static GameObject MakeButton(GameObject parent, string name, string label, Vector2 pos)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var img = go.AddComponent<Image>();
        img.color = new Color32(255, 255, 255, 200);
        go.AddComponent<Button>();
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(280, 55);
        rt.anchoredPosition = pos;

        var txtGO = new GameObject("Label");
        txtGO.transform.SetParent(go.transform, false);
        var tmp   = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.color     = Color.black;
        tmp.fontSize  = 22;
        tmp.alignment = TextAlignmentOptions.Center;
        var trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        return go;
    }

    private static TextMeshProUGUI MakeText(GameObject parent, string name, string text,
        Vector2 pos, float size, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(500, 70);
        rt.anchoredPosition = pos;
        return tmp;
    }

    private static TMP_InputField MakeInputField(GameObject parent, string name,
        string placeholder, Vector2 pos)
    {
        var go    = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<Image>().color = new Color32(255, 255, 255, 180);
        var input = go.AddComponent<TMP_InputField>();
        var rt    = go.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(320, 55);
        rt.anchoredPosition = pos;

        // Text Area viewport (required by TMP_InputField)
        var areaGO = new GameObject("Text Area");
        areaGO.transform.SetParent(go.transform, false);
        var areaRT = areaGO.AddComponent<RectTransform>();
        areaRT.anchorMin = Vector2.zero;
        areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(10, 4);
        areaRT.offsetMax = new Vector2(-10, -4);
        input.textViewport = areaRT;

        // Text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(areaGO.transform, false);
        var tmp    = textGO.AddComponent<TextMeshProUGUI>();
        tmp.color    = Color.black;
        tmp.fontSize = 22;
        var trt    = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        input.textComponent = tmp;

        // Placeholder
        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(areaGO.transform, false);
        var ph   = phGO.AddComponent<TextMeshProUGUI>();
        ph.text      = placeholder;
        ph.color     = new Color32(130, 130, 130, 200);
        ph.fontSize  = 22;
        ph.fontStyle = FontStyles.Italic;
        var prt  = phGO.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        input.placeholder = ph;

        return input;
    }

    // ─── Generic helpers ──────────────────────────────────────────────────────

    private static GameObject FindOrCreate(string name)
        => GameObject.Find(name) ?? new GameObject(name);

    private static T GetOrAdd<T>(this GameObject go) where T : Component
        => go.GetComponent<T>() ?? go.AddComponent<T>();
}
