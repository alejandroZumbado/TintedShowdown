// Run once: Tinted Showdown → Setup All
// Automates scene setup, prefab config, and build settings.

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;
using System.Linq;
using System.Reflection;

public static class TintedShowdownSetup
{
    // ── Change these constants if you rename files ──────────────────────────
    private const string PlayerPrefabPath  = "Assets/Prefabs/Player.prefab";
    private const string CanvasPrefabPath  = "Assets/Prefabs/Canvas.prefab";
    private const string MenuScenePath     = "Assets/Scenes/GameMenu.unity";
    private const string ArenaScenePath    = "Assets/Scenes/Arena.unity";

    // Circle radius for spawn points (meters from center)
    private const float SpawnRadius = 5f;

    [MenuItem("Tinted Showdown/Setup All (run once)")]
    public static void SetupAll()
    {
        if (!EditorUtility.DisplayDialog("Setup Tinted Showdown",
            "Modifica prefabs, GameMenu.unity y Arena.unity.\n\n" +
            "Ejecuta solo una vez antes de entrar a Play Mode.",
            "Continuar", "Cancelar")) return;

        RunSetupSteps();

        EditorUtility.DisplayDialog("¡Listo!",
            "Setup completado.\n\n" +
            "Pasos manuales:\n" +
            "1. Project Settings → Services → vincular Unity Dashboard\n" +
            "2. Verifica que el Player.prefab tenga Renderer asignado en ActionPlayerManager",
            "OK");
    }

    // -executeMethod entry point for batchmode/CI use — EditorUtility.DisplayDialog
    // can't be answered from batchmode (it just short-circuits SetupAll as "Cancelar"),
    // so this skips the confirmation prompt entirely. Same steps, no dialogs.
    public static void SetupAllHeadless() => RunSetupSteps();

    private static void RunSetupSteps()
    {
        SetupMobileLandscape();
        SetupAndroidPlayerSettings();
        SetupWebGLPlayerSettings();
        SetupPlayerPrefab();
        SetupCanvasPrefab();
        SetupGameMenuScene();
        SetupArenaScene();
        SetupBuildSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    // Testing utility: clears the saved player name (and anything else stored in
    // PlayerPrefs) so the NamePanel prompt shows up again on the next Play session.
    [MenuItem("Tinted Showdown/Borrar PlayerPrefs (testing)")]
    public static void ClearPlayerPrefs()
    {
        if (!EditorUtility.DisplayDialog("Borrar PlayerPrefs",
            "Esto borra todos los PlayerPrefs guardados en el Editor (incluye el nombre de jugador).\n\n¿Continuar?",
            "Borrar", "Cancelar")) return;

        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[Setup] PlayerPrefs borrados");
    }

    // ─── Mobile / landscape ───────────────────────────────────────────────────

    // Common landscape phone resolutions as of today (16:9 up to 21:9 aspect ratios).
    // Added as Game View size presets so you can preview the UI at realistic sizes
    // without needing a physical device.
    private static readonly (string Name, int Width, int Height)[] MobileLandscapeSizes =
    {
        ("Mobile FHD 16:9 (1920x1080)", 1920, 1080),
        ("Mobile 19.5:9 (2340x1080)",   2340, 1080),
        ("Mobile 20:9 (2400x1080)",     2400, 1080),
        ("Mobile 21:9 (2520x1080)",     2520, 1080),
    };

    private static void SetupMobileLandscape()
    {
        // The game has no portrait layout — lock orientation so the OS never rotates
        // into one. AutoRotation + only the two landscape flags still lets players flip
        // the device left/right, just never into portrait.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        Debug.Log("[Setup] Orientation locked to landscape (left/right autorotate only)");

        foreach (var size in MobileLandscapeSizes)
            AddGameViewSizeIfMissing(size.Name, size.Width, size.Height);
    }

    // Game View size presets aren't exposed by a public API — this uses the same
    // reflection-based approach the Unity community has relied on for years (internal
    // UnityEditor.GameViewSizes/GameViewSizeGroup/GameViewSize classes). Wrapped in a
    // try/catch so that if a future Unity version changes this internal API, the rest of
    // Setup All still runs — you'd just need to add the resolution by hand in the Game tab.
    private static void AddGameViewSizeIfMissing(string name, int width, int height)
    {
        try
        {
            var assembly = typeof(Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            object gameViewSizes = singletonType.GetProperty("instance").GetValue(null, null);

            object currentGroupType = sizesType.GetProperty("currentGroupType").GetValue(gameViewSizes, null);
            object group = sizesType.GetMethod("GetGroup").Invoke(gameViewSizes, new object[] { currentGroupType });

            var groupType = assembly.GetType("UnityEditor.GameViewSizeGroup");
            int count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
            var getGameViewSize = groupType.GetMethod("GetGameViewSize");

            for (int i = 0; i < count; i++)
            {
                object existing = getGameViewSize.Invoke(group, new object[] { i });
                // Compare against baseText (the raw name we passed in), not displayText
                // (which Unity reformats with the dimensions) — otherwise this never
                // matches on a second run and the same preset piles up every time.
                var baseText = (string)existing.GetType().GetProperty("baseText").GetValue(existing, null);
                if (baseText == name) return; // already added by a previous Setup All run
            }

            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var sizeTypeEnum = assembly.GetType("UnityEditor.GameViewSizeType");
            object fixedResolution = System.Enum.Parse(sizeTypeEnum, "FixedResolution");
            var ctor = sizeType.GetConstructor(new[] { sizeTypeEnum, typeof(int), typeof(int), typeof(string) });
            object newSize = ctor.Invoke(new object[] { fixedResolution, width, height, name });

            groupType.GetMethod("AddCustomSize").Invoke(group, new object[] { newSize });
            Debug.Log($"[Setup] Added Game View size preset: {name}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Setup] Could not add Game View size '{name}' — internal Unity API may have changed: {e.Message}");
        }
    }

    // ─── Android build settings ────────────────────────────────────────────────

    private const string DefaultAndroidPackageName = "com.tintedshowdown.game";

    private static void SetupAndroidPlayerSettings()
    {
        // Unity refuses to build for Android at all with no applicationIdentifier set.
        // This is a placeholder — package names are immutable once published to Google
        // Play, so swap it for your own domain/company before a real release build.
        //
        // GetApplicationIdentifier never actually returns empty — if nothing was ever set,
        // it synthesizes "com.DefaultCompany.<ProductName>" on the fly without persisting
        // it, so an IsNullOrEmpty check alone never catches the unset case. Checking for
        // the literal "DefaultCompany" sentinel does.
        string currentId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        if (string.IsNullOrEmpty(currentId) || currentId.Contains("DefaultCompany"))
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, DefaultAndroidPackageName);
            Debug.LogWarning($"[Setup] Android applicationIdentifier estaba vacío — seteado a placeholder '{DefaultAndroidPackageName}'. Cambialo antes de publicar.");
        }

        // Mono can't ship a 64-bit slice on Android, and Google Play has required 64-bit
        // since 2019 — IL2CPP is mandatory for a real release build.
        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            Debug.Log("[Setup] Android scripting backend: Mono → IL2CPP");
        }

        // The project was set to ARMv7-only (no 64-bit slice) — Google Play rejects that.
        // ARM64 alone covers effectively every device still receiving OS updates.
        if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
        {
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            Debug.Log("[Setup] Android target architecture: ARMv7 → ARM64");
        }
    }

    // ─── WebGL build settings ───────────────────────────────────────────────────

    private const string WebGLTemplateName = "PROJECT:TintedShowdown";

    // Public: also called directly by WebGLBuildScript.Build() for CLI builds, which
    // can't go through the interactive SetupAll() dialog in batchmode.
    public static void SetupWebGLPlayerSettings()
    {
        // GitHub Pages (and most static hosts) never send a Content-Encoding: gzip/br
        // header for arbitrary files, so a Gzip/Brotli-compressed WebGL build silently
        // fails to load once hosted there — it works fine locally only because
        // `python -m http.server`-style local servers happen to set that header.
        // Decompression Fallback packs a JS-side decompressor into the loader instead,
        // so the build works regardless of what headers the host sends.
        if (!PlayerSettings.WebGL.decompressionFallback)
        {
            PlayerSettings.WebGL.decompressionFallback = true;
            Debug.Log("[Setup] WebGL Decompression Fallback: OFF → ON (necesario para hostear en GitHub Pages)");
        }

        if (PlayerSettings.WebGL.template != WebGLTemplateName)
        {
            PlayerSettings.WebGL.template = WebGLTemplateName;
            Debug.Log($"[Setup] WebGL template: {WebGLTemplateName}");
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

        var playerCamera = SetupPlayerCamera(root);

        // Round-result sounds (dealt/took damage) need a source to play from — the clips
        // themselves aren't auto-assigned, drag those into the Inspector by hand.
        var audioSource = root.GetOrAdd<AudioSource>();
        audioSource.playOnAwake = false;

        var apm = root.GetComponent<ActionPlayerManager>();
        if (apm != null)
        {
            var so = new SerializedObject(apm);
            so.FindProperty("playerCamera").objectReferenceValue = playerCamera;
            so.FindProperty("audioSource").objectReferenceValue = audioSource;
            so.FindProperty("flatMaterialTemplate").objectReferenceValue = EnsureFlatPaintMaterial();
            so.ApplyModifiedProperties();
        }
    }

    private const string FlatPaintMaterialPath = "Assets/Materials/FlatPaint.mat";

    // A real Material asset, not Shader.Find("Unlit/Color") — a shader only looked up by
    // string in code gets stripped from standalone builds when nothing else references it,
    // so Shader.Find silently returns null outside the Editor (confirmed crash: see
    // ActionPlayerManager.CreatePaintMaterials throwing ArgumentNullException in a PC
    // build). Creating this asset once gives the build pipeline a real reference to keep.
    private static Material EnsureFlatPaintMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(FlatPaintMaterialPath);
        if (existing != null) return existing;

        var shader = Shader.Find("Unlit/Color");
        if (shader == null) { Debug.LogError("[Setup] Shader 'Unlit/Color' not found"); return null; }

        var mat = new Material(shader) { name = "FlatPaint" };
        AssetDatabase.CreateAsset(mat, FlatPaintMaterialPath);
        Debug.Log("[Setup] Created Assets/Materials/FlatPaint.mat");
        return mat;
    }

    // Per-player camera, child of the player — disabled by default, ActionPlayerManager
    // enables it only for IsOwner so each client looks at their OWN player instead of
    // everyone sharing the single static scene camera.
    private static Camera SetupPlayerCamera(GameObject root)
    {
        var existing = root.transform.Find("PlayerCamera");
        bool isNew = existing == null;
        var camGO = isNew ? new GameObject("PlayerCamera") : existing.gameObject;
        camGO.transform.SetParent(root.transform, false);

        // Only set these on first creation — if you've since hand-tweaked the camera's
        // angle/offset/tag in the Inspector, re-running Setup All won't stomp it.
        if (isNew)
        {
            camGO.tag = "Untagged"; // must NOT be "MainCamera" — that tag stays on the scene's static camera
            camGO.SetActive(false);

            // Directly above the player looking straight down. A behind-and-pitched
            // camera depends on the player's yaw (different per spawn point, since each
            // one faces the arena center from a different angle) to keep the player
            // centered — a top-down camera with zero horizontal offset doesn't: the
            // player is always directly underneath it, regardless of spawn rotation.
            camGO.transform.localPosition = new Vector3(0f, 6f, 0f);
            camGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Debug.Log("[Setup] Player.prefab camera created");
        }

        var cam = camGO.GetOrAdd<Camera>();
        camGO.GetOrAdd<AudioListener>();
        return cam;
    }

    // ─── Canvas.prefab: ColorButtonProxy + rewire 8 buttons ──────────────────

    private static void SetupCanvasPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPrefabPath);
        if (prefab == null) { Debug.LogError($"[Setup] Not found: {CanvasPrefabPath}"); return; }

        using var scope = new PrefabUtility.EditPrefabContentsScope(CanvasPrefabPath);
        var root = scope.prefabContentsRoot;

        var proxy = root.GetComponent<ColorButtonProxy>() ?? root.AddComponent<ColorButtonProxy>();
        RewireColorButtons(root, proxy);

        // This CanvasScaler was still at Unity's defaults (ConstantPixelSize, 800x600) —
        // never configured. ScaleWithScreenSize + a landscape reference resolution is
        // needed so the color buttons stay correctly sized across phone aspect ratios.
        ApplyLandscapeScaler(root.GetComponent<CanvasScaler>());

        // The two color-picker grids were large (100x100 cells) and screen-centered —
        // covering the players in the middle of the arena. Dock them small into the
        // bottom corners instead, out of the way of the action.
        ApplyCornerLayout(root.transform.Find("UIChangeColor"), anchorRight: false);
        ApplyCornerLayout(root.transform.Find("UIAttackColor"), anchorRight: true);
    }

    private static void ApplyCornerLayout(Transform group, bool anchorRight)
    {
        if (group == null) return;

        var rt = group.GetComponent<RectTransform>();

        // Still at Unity's default center anchor means this has never been laid out by
        // this method. Anything else means it's already been positioned (by this script
        // previously, or by hand since) — leave it alone instead of stomping it.
        if (rt.anchorMin != new Vector2(0.5f, 0.5f)) return;

        float x = anchorRight ? 1f : 0f;
        rt.anchorMin = new Vector2(x, 0f);
        rt.anchorMax = new Vector2(x, 0f);
        rt.pivot     = new Vector2(x, 0f);
        rt.anchoredPosition = new Vector2(anchorRight ? -30f : 30f, 30f);

        var grid = group.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.cellSize = new Vector2(70f, 70f);
            grid.spacing  = new Vector2(8f, 8f);
        }

        // LayoutGroup changes made via script outside Play Mode don't always repaint the
        // Editor's cached preview immediately — force it so what you see in the Game/Scene
        // view right after running Setup All actually matches the saved data.
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    // Match by height: in landscape, height is the more consistent dimension across
    // devices (16:9 up to 21:9 phones mostly differ in width), so matching it keeps UI
    // elements a consistent size instead of shrinking on wider/narrower screens.
    private static void ApplyLandscapeScaler(CanvasScaler scaler)
    {
        if (scaler == null) return;
        // Already configured (by this script before, or by hand) — don't re-stomp it.
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) return;

        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 1f;
    }

    // Rewires the 4 body-color + 4 weapon-color buttons under `root` to call
    // ColorButtonProxy.OnBodyColor/OnWeaponColor. Body vs weapon is decided by which
    // container ("UIChangeColor" vs "UIAttackColor") the button sits under — NOT by
    // reading back the previously-wired method name, which broke on re-runs (after the
    // first run there's no "AttackColor" trace left to detect, so every later run
    // rewired both rows to OnBodyColor).
    //
    // Called on both the prefab asset AND any already-placed scene instance, because a
    // PrefabInstance can carry its own stale per-button OnClick override (e.g. a Canvas
    // placed in Arena.unity before ColorButtonProxy existed keeps a null m_Target
    // override forever unless something explicitly rewrites that instance too).
    private static void RewireColorButtons(GameObject root, ColorButtonProxy proxy)
    {
        var nameToIndex = new System.Collections.Generic.Dictionary<string, int>
        {
            { "Red", 0 }, { "Blue", 1 }, { "Green", 2 }, { "Yellow", 3 }
        };

        int rewired = 0;
        foreach (var btn in root.GetComponentsInChildren<Button>(true))
        {
            if (!nameToIndex.TryGetValue(btn.gameObject.name, out int idx)) continue;

            string method = IsUnderContainer(btn.transform, "UIAttackColor") ? "OnWeaponColor" : "OnBodyColor";

            var bso   = new SerializedObject(btn);
            var calls = bso.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");

            // Skip if already correct — keeps re-runs a true no-op for buttons that are
            // fine, instead of unconditionally rewriting (and losing any extra listener
            // you might have added by hand) every single time.
            if (calls.arraySize == 1)
            {
                var existingCall = calls.GetArrayElementAtIndex(0);
                bool targetOk = existingCall.FindPropertyRelative("m_Target").objectReferenceValue == (Object)proxy;
                bool methodOk = existingCall.FindPropertyRelative("m_MethodName").stringValue == method;
                bool argOk    = existingCall.FindPropertyRelative("m_Arguments.m_IntArgument").intValue == idx;
                if (targetOk && methodOk && argOk) continue;
            }

            calls.ClearArray();
            calls.arraySize = 1;
            var c = calls.GetArrayElementAtIndex(0);
            c.FindPropertyRelative("m_Target").objectReferenceValue             = proxy;
            c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue      = "ColorButtonProxy, Assembly-CSharp";
            c.FindPropertyRelative("m_MethodName").stringValue                  = method;
            c.FindPropertyRelative("m_Mode").intValue                           = 3; // int arg
            c.FindPropertyRelative("m_Arguments.m_IntArgument").intValue        = idx;
            c.FindPropertyRelative("m_CallState").intValue                      = 2; // RuntimeOnly
            bso.ApplyModifiedProperties();
            rewired++;
        }

        Debug.Log($"[Setup] {rewired} color buttons rewired under '{root.name}'");
    }

    private static bool IsUnderContainer(Transform t, string containerName)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name == containerName) return true;
        return false;
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

        // NetworkConfig.PlayerPrefab must stay unset. When ConnectionApproval is off (our
        // case), NGO auto-spawns NetworkConfig.PlayerPrefab for every connecting client —
        // with destroyWithScene=false, so it survives into Arena — IN ADDITION to the
        // player GameManager.OnNetworkSpawn spawns manually. That's 2 player objects per
        // client. GameManager must be the only thing that ever spawns a player.
        if (nm.NetworkConfig.PlayerPrefab != null)
        {
            nm.NetworkConfig.PlayerPrefab = null;
            EditorUtility.SetDirty(nm);
            Debug.LogWarning("[Setup] Cleared NetworkConfig.PlayerPrefab — it was causing NGO to auto-spawn a duplicate player per client.");
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

        // Create LobbyCanvas only if it's missing — preserves any manual tweaks made
        // since the last run instead of wiping the whole thing out every time.
        var lobbyCanvasGO = GameObject.Find("LobbyCanvas");
        if (lobbyCanvasGO == null)
        {
            lobbyCanvasGO = CreateLobbyCanvas();
        }
        else
        {
            Debug.Log("[Setup] LobbyCanvas already exists — leaving it as-is");
        }

        // Added after the rest of the lobby UI already existed in some projects — add it
        // on its own without touching (or requiring you to delete) an existing LobbyCanvas.
        EnsureNamePanel(lobbyCanvasGO, lobbyCanvasGO.GetComponent<LobbyUIManager>());

        // Added after the rest of the lobby UI already existed in some projects — same
        // pattern as EnsureNamePanel, only adds the toast if it's missing.
        EnsureErrorToast(lobbyCanvasGO, lobbyCanvasGO.GetComponent<LobbyUIManager>());

        // WaitPanel was the only panel with no way back — once you create/join a room
        // you were stuck there until it filled up or the host disconnected. Same
        // "Ensure" pattern as above.
        EnsureWaitPanelCancelButton(lobbyCanvasGO, lobbyCanvasGO.GetComponent<LobbyUIManager>());

        // Selection outline on the 2/3/4-player buttons + loading-state refs for
        // Crear Sala / Entrar. Always re-run (idempotent), not gated behind "only if
        // LobbyCanvas is new" like the button wiring above it.
        EnsureSelectionAndLoadingUx(lobbyCanvasGO, lobbyCanvasGO.GetComponent<LobbyUIManager>());

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Setup] GameMenu.unity saved");
    }

    private static GameObject CreateLobbyCanvas()
    {
        var canvasGO = new GameObject("LobbyCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        ApplyLandscapeScaler(canvasGO.AddComponent<CanvasScaler>());
        canvasGO.AddComponent<GraphicRaycaster>();
        var lobbyUI = canvasGO.AddComponent<LobbyUIManager>();

        var menuPanel   = MakePanel(canvasGO, "MenuPanel",   new Color32(30,  30,  30,  220));
        var createPanel = MakePanel(canvasGO, "CreatePanel", new Color32(20,  50,  80,  220));
        var waitPanel   = MakePanel(canvasGO, "WaitPanel",   new Color32(20,  70,  50,  220));
        var winPanel    = MakePanel(canvasGO, "WinPanel",    new Color32(80,  65,  10,  220));

        // ── MenuPanel ──
        MakeButton(menuPanel, "BtnShowCreate", "Crear Sala",  new Vector2(0,  95));
        var joinInput = MakeInputField(menuPanel, "JoinCodeInput", "Contraseña de sala...", new Vector2(0, 0));
        MakeButton(menuPanel, "BtnJoinConfirm", "Entrar",     new Vector2(0, -95));

        // ── CreatePanel ── (wider spread between the 3 buttons — landscape has room)
        MakeText(createPanel, "Title", "¿Cuántos jugadores?", new Vector2(0, 150), 32, Color.white);
        var btn2p = MakeButton(createPanel, "Btn2P", "2 Jugadores", new Vector2(-280, 45));
        var btn3p = MakeButton(createPanel, "Btn3P", "3 Jugadores", new Vector2(    0, 45));
        var btn4p = MakeButton(createPanel, "Btn4P", "4 Jugadores", new Vector2(  280, 45));
        MakeButton(createPanel, "BtnCreate",     "Crear Sala",      new Vector2(    0, -50));
        MakeButton(createPanel, "BtnBackCreate", "← Volver",        new Vector2(    0, -140));

        // ── WaitPanel ──
        var joinCodeDisplay = MakeText(waitPanel, "JoinCodeDisplay", "Contraseña: ------",
                                       new Vector2(0, 105), 34, Color.white);
        var copyBtn        = MakeButton(waitPanel, "BtnCopyCode", "Copiar", new Vector2(0, 20));
        var waitStatusText = MakeText(waitPanel, "WaitStatusText", "Jugadores: 0/4",
                                       new Vector2(0, -65), 38, Color.white);
        MakeText(waitPanel, "WaitHint", "Esperando jugadores...", new Vector2(0, -135), 22,
                 new Color32(200, 200, 200, 200));
        copyBtn.SetActive(false); // shown when host has a code

        // ── WinPanel ──
        var winnerText   = MakeText(winPanel, "WinnerText", "¡Jugador X ganó!", new Vector2(0, 95), 42, Color.yellow);
        var playAgainBtn = MakeButton(winPanel, "BtnPlayAgain", "Jugar de nuevo", new Vector2(0, -25));
        var waitHostText = MakeText(winPanel, "WaitHostText", "Esperando a que el host reinicie...",
                                     new Vector2(0, -25), 22, new Color32(200, 200, 200, 200));
        var backBtn      = MakeButton(winPanel, "BtnBackToMenu", "Volver al Menú", new Vector2(0, -120));

        // Wire void buttons
        WireVoidButton(copyBtn.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.OnCopyCodeButton));
        WireVoidButton(backBtn.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.OnBackToMenuPublic));
        WireVoidButton(playAgainBtn.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.OnPlayAgainButton));
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
        so.FindProperty("playAgainButton").objectReferenceValue  = playAgainBtn.GetComponent<Button>();
        so.FindProperty("waitHostText").objectReferenceValue     = waitHostText.gameObject;
        so.ApplyModifiedProperties();

        Debug.Log("[Setup] LobbyCanvas created");
        return canvasGO;
    }

    // Adds just the name-entry panel to an existing LobbyCanvas if it's missing — lets
    // this feature ship without forcing a full LobbyCanvas recreation, which would wipe
    // any manual tweaks made to the rest of the lobby UI since the last Setup All run.
    private static void EnsureNamePanel(GameObject canvasGO, LobbyUIManager lobbyUI)
    {
        if (canvasGO.transform.Find("NamePanel") != null)
        {
            Debug.Log("[Setup] NamePanel already exists — leaving it as-is");
            return;
        }

        var namePanel = MakePanel(canvasGO, "NamePanel", new Color32(40, 30, 60, 220));
        MakeText(namePanel, "NameTitle", "¿Cómo te llamas?", new Vector2(0, 90), 32, Color.white);
        var nameInput = MakeInputField(namePanel, "NameInput", "Tu nombre...", new Vector2(0, 0));
        nameInput.characterLimit = 16; // must match the FixedString32Bytes clamp in ActionPlayerManager
        var confirmBtn = MakeButton(namePanel, "BtnConfirmName", "Confirmar", new Vector2(0, -90));
        WireVoidButton(confirmBtn.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.OnConfirmNameButton));
        namePanel.SetActive(false);

        var so = new SerializedObject(lobbyUI);
        so.FindProperty("namePanel").objectReferenceValue = namePanel;
        so.FindProperty("nameInput").objectReferenceValue = nameInput;
        so.ApplyModifiedProperties();

        Debug.Log("[Setup] NamePanel added to LobbyCanvas");
    }

    // WaitPanel had no way out — every other panel has a "← Volver"/back option, but once
    // you created or joined a room you were stuck waiting until it filled up or the host
    // disconnected. Reuses OnBackToMenuPublic (Disconnect + reload GameMenu), the exact
    // same method the win screen's back button already calls — no new C# needed.
    private static void EnsureWaitPanelCancelButton(GameObject canvasGO, LobbyUIManager lobbyUI)
    {
        if (canvasGO.transform.Find("WaitPanel/BtnCancelWait") != null)
        {
            Debug.Log("[Setup] WaitPanel cancel button already exists — leaving it as-is");
            return;
        }

        var waitPanelTf = canvasGO.transform.Find("WaitPanel");
        if (waitPanelTf == null)
        {
            Debug.LogWarning("[Setup] WaitPanel not found — skipping cancel button");
            return;
        }

        var cancelBtn = MakeButton(waitPanelTf.gameObject, "BtnCancelWait", "← Cancelar", new Vector2(0, -210));
        WireVoidButton(cancelBtn.GetComponent<Button>(), lobbyUI, nameof(LobbyUIManager.OnBackToMenuPublic));

        Debug.Log("[Setup] WaitPanel cancel button added");
    }

    // Adds a selection outline to the 2/3/4-player buttons (LobbyUIManager toggles which
    // one is enabled) and wires the Crear Sala / Entrar button references used for the
    // "Conectando..." loading state. Runs unconditionally every time, same as the other
    // Ensure* methods — safe to re-run, just re-finds and re-assigns.
    private static void EnsureSelectionAndLoadingUx(GameObject canvasGO, LobbyUIManager lobbyUI)
    {
        var createPanelTf = canvasGO.transform.Find("CreatePanel");
        var menuPanelTf   = canvasGO.transform.Find("MenuPanel");
        if (createPanelTf == null || menuPanelTf == null)
        {
            Debug.LogWarning("[Setup] CreatePanel/MenuPanel not found — skipping selection/loading UX");
            return;
        }

        // .Find() only checks direct children — these buttons may be nested inside a
        // layout-group container (e.g. "Players") added by hand after the panel was
        // first generated, same reason RewireColorButtons uses GetComponentsInChildren
        // instead of a shallow Find.
        Button FindButtonDeep(string name) => createPanelTf
            .GetComponentsInChildren<Button>(true)
            .FirstOrDefault(b => b.gameObject.name == name);

        var maxPlayerButtons = new[]
        {
            FindButtonDeep("Btn2P"),
            FindButtonDeep("Btn3P"),
            FindButtonDeep("Btn4P"),
        };

        // Tried UnityEngine.UI.Outline first — it only offsets a duplicate copy of the
        // graphic in one direction (a drop-shadow, not a ring), which at this button
        // size/CanvasScaler factor was too subtle to read as "selected" at a glance.
        // A background color swap is unambiguous regardless of scale.

        var createBtn = FindButtonDeep("BtnCreate");
        var joinBtn   = menuPanelTf.GetComponentsInChildren<Button>(true)
            .FirstOrDefault(b => b.gameObject.name == "BtnJoinConfirm");

        var so = new SerializedObject(lobbyUI);
        var arrProp = so.FindProperty("maxPlayerButtons");
        arrProp.arraySize = maxPlayerButtons.Length;
        for (int i = 0; i < maxPlayerButtons.Length; i++)
            arrProp.GetArrayElementAtIndex(i).objectReferenceValue = maxPlayerButtons[i];
        so.FindProperty("createRoomButton").objectReferenceValue = createBtn;
        so.FindProperty("joinRoomButton").objectReferenceValue   = joinBtn;
        so.ApplyModifiedProperties();

        Debug.Log("[Setup] Selection outline + loading-state button refs wired");
    }

    // Top-level toast (sibling of the panels, not nested in one) so it stays visible no
    // matter which panel is active — errorText used to be a child of MenuPanel only, so
    // errors raised while NamePanel/CreatePanel were showing never rendered.
    private static void EnsureErrorToast(GameObject canvasGO, LobbyUIManager lobbyUI)
    {
        var existing = canvasGO.transform.Find("ErrorToast");
        // A previous buggy version of this method put the Image and the TextMeshProUGUI
        // on the SAME GameObject. Two Graphic components sharing one CanvasRenderer is
        // invalid — only one of them ends up actually rendering, and which one is
        // platform-dependent, which is how a small toast turned into a full-screen red
        // block on device. Detect that broken shape (no "Label" child) and rebuild it,
        // instead of trusting existence-by-name like the old check did.
        if (existing != null && existing.Find("Label") != null)
        {
            Debug.Log("[Setup] ErrorToast already exists — leaving it as-is");
            return;
        }
        if (existing != null)
        {
            Debug.LogWarning("[Setup] Found a broken ErrorToast (Image+Text sharing one GameObject) — rebuilding it");
            Object.DestroyImmediate(existing.gameObject);
        }

        // Remove the old MenuPanel-only ErrorText left over from before the toast existed —
        // it's dead weight now that errorText points at the new top-level toast.
        var legacy = canvasGO.transform.Find("MenuPanel/ErrorText");
        if (legacy != null) Object.DestroyImmediate(legacy.gameObject);

        // Root only holds the background Image + CanvasRenderer. Its active state is what
        // LobbyUIManager toggles to show/hide the whole toast (see errorToastRoot).
        var toast = new GameObject("ErrorToast");
        toast.transform.SetParent(canvasGO.transform, false);
        toast.transform.SetAsLastSibling(); // render above every panel

        var bg = toast.AddComponent<Image>();
        bg.color = new Color32(50, 20, 20, 235);
        bg.raycastTarget = false; // never blocks taps on whatever is underneath
        var rt = toast.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta        = new Vector2(720, 90);
        rt.anchoredPosition  = new Vector2(0, 60); // near the bottom edge, clear of panel content

        // Text lives on its own child GameObject ("Label") so it gets its own
        // CanvasRenderer instead of fighting the background Image for the one on toast.
        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(toast.transform, false);
        var labelRt = labelGO.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.sizeDelta = Vector2.zero;
        labelRt.anchoredPosition = Vector2.zero;

        var errorText = labelGO.AddComponent<TextMeshProUGUI>();
        errorText.fontSize  = 26;
        errorText.color     = new Color32(255, 140, 140, 255);
        errorText.alignment = TextAlignmentOptions.Center;
        errorText.margin    = new Vector4(20, 4, 20, 4);
        errorText.raycastTarget = false;

        toast.SetActive(false);

        var so = new SerializedObject(lobbyUI);
        so.FindProperty("errorToastRoot").objectReferenceValue = toast;
        so.FindProperty("errorText").objectReferenceValue = errorText;
        so.ApplyModifiedProperties();

        Debug.Log("[Setup] ErrorToast added to LobbyCanvas");
    }

    // ─── Arena.unity ──────────────────────────────────────────────────────────

    private static void SetupArenaScene()
    {
        // Prevent Unity's "Auto Generate" lighting mode from baking lightmaps/reflection
        // probes the moment this method adds/touches lit geometry and saves the scene.
        // With environmentRotationEuler still at the placeholder (0,0,0) for all 3
        // presets, any bake right now is for the wrong sun angle and gets thrown away
        // the moment that's tuned by eye — baking is a manual step for later.
        Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

        var scene = EditorSceneManager.OpenScene(ArenaScenePath, OpenSceneMode.Single);

        // Remove any Player GOs left in the scene — players must only exist at runtime
        CleanupManualPlayers();

        // Arena needs its own EventSystem — GameMenu's is destroyed on scene load
        EnsureEventSystem();

        // GameManager
        var gmGO = GameObject.Find("GameManager") ?? new GameObject("GameManager");
        gmGO.GetOrAdd<NetworkObject>();
        var gm = gmGO.GetOrAdd<GameManager>();

        // EnvironmentManager — random Day/Night/Blend skybox preset, re-rolled by
        // GameManager at the start of every round, synced to all clients. The rig
        // (EnsureEnvironmentRig) and skybox/light/fog values (EnsureEnvironmentPresets)
        // are auto-filled; only each preset's rotation is a by-eye Inspector tweak.
        var envGO = FindOrCreate("EnvironmentManager");
        envGO.GetOrAdd<NetworkObject>();
        var envMgr = envGO.GetOrAdd<EnvironmentPresetManager>();
        EnsureEnvironmentPresets(envMgr);
        EnsureEnvironmentRig(scene, envMgr);

        // Spawn points in a circle — players face the center
        var spawnRoot = FindOrCreate("SpawnPoints");
        var points    = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            string n = $"SpawnPoint{i + 1}";
            var existingSp = GameObject.Find(n);
            bool isNew = existingSp == null;
            var sp = isNew ? new GameObject(n) : existingSp;
            sp.transform.SetParent(spawnRoot.transform);

            // Only place it on first creation — if you've since dragged a spawn point
            // somewhere else by hand, re-running Setup All won't snap it back.
            if (isNew)
            {
                // Start at top (90°) and go clockwise in world space
                float angle = Mathf.PI / 2f - (2f * Mathf.PI * i) / 4f;
                float x = Mathf.Cos(angle) * SpawnRadius;
                float z = Mathf.Sin(angle) * SpawnRadius;
                sp.transform.position = new Vector3(x, 0f, z);

                // Rotate to face center
                var dir = -new Vector3(x, 0f, z).normalized;
                if (dir != Vector3.zero)
                    sp.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

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

        // Saving a scene with lit geometry auto-bakes lightmaps/reflection probes into an
        // "Arena/" companion folder even with giWorkflowMode set to OnDemand — that bake
        // is for environmentRotationEuler's still-unset (0,0,0) placeholder, so it's
        // ~27MB that becomes stale the moment that rotation is tuned by eye. This folder
        // didn't exist before EnsureEnvironmentRig, so it's always safe to clear here.
        const string arenaLightingDataFolder = "Assets/Scenes/Arena";
        if (AssetDatabase.IsValidFolder(arenaLightingDataFolder))
        {
            AssetDatabase.DeleteAsset(arenaLightingDataFolder);
            Debug.Log("[Setup] Cleared auto-generated Arena/ lighting data (stale until rotations are tuned)");
        }
    }

    // Values lifted directly from Demo Day.unity / Demo Night.unity / Demo Blend.unity
    // (RenderSettings + the "Directional Light" in each) so the in-game presets match
    // the source scenes exactly. Rotation is excluded on purpose — see EnsureEnvironmentPresets.
    private static readonly (string name, string matPath, Color lightColor, float lightIntensity,
        bool fogEnabled, Color fogColor, float fogStart, float fogEnd, float ambientIntensity)[]
        EnvironmentPresetData =
    {
        ("Day",
         "Assets/BOXOPHOBIC/Skybox Cubemap Extended/Demo/Materials/Skybox Cubemap Extended Day.mat",
         new Color(1f, 0.5917582f, 0.21226418f), 1.5f,
         true, new Color(1f, 0.43005908f, 0.24313724f), 0f, 70f, 0.95f),

        ("Night",
         "Assets/BOXOPHOBIC/Skybox Cubemap Extended/Demo/Materials/Skybox Cubemap Extended Night.mat",
         new Color(0.26379493f, 0.40115762f, 0.9811321f), 1.2f,
         true, new Color(0.7830189f, 0.21052866f, 0.5883721f), -10f, 100f, 1f),

        ("Blend",
         "Assets/BOXOPHOBIC/Skybox Cubemap Extended/Demo/Materials/Skybox Cubemap Extended Blend.mat",
         new Color(1f, 0.7654179f, 0.5990566f), 1f,
         false, new Color(0.4762816f, 0.6578769f, 0.7264151f), 0f, 300f, 1f),
    };

    private static void EnsureEnvironmentPresets(EnvironmentPresetManager mgr)
    {
        var so = new SerializedObject(mgr);
        var presetsProp = so.FindProperty("presets");
        presetsProp.arraySize = EnvironmentPresetData.Length;

        for (int i = 0; i < EnvironmentPresetData.Length; i++)
        {
            var data    = EnvironmentPresetData[i];
            var element = presetsProp.GetArrayElementAtIndex(i);
            var matProp = element.FindPropertyRelative("skyboxMaterial");

            // Only fill a preset that's never been configured — re-running Setup All
            // must not clobber a rotation you already tuned by hand.
            if (matProp.objectReferenceValue != null) continue;

            matProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(data.matPath);
            element.FindPropertyRelative("name").stringValue           = data.name;
            element.FindPropertyRelative("lightColor").colorValue      = data.lightColor;
            element.FindPropertyRelative("lightIntensity").floatValue  = data.lightIntensity;
            element.FindPropertyRelative("fogEnabled").boolValue       = data.fogEnabled;
            element.FindPropertyRelative("fogColor").colorValue        = data.fogColor;
            element.FindPropertyRelative("linearFogStart").floatValue  = data.fogStart;
            element.FindPropertyRelative("linearFogEnd").floatValue    = data.fogEnd;
            element.FindPropertyRelative("ambientIntensity").floatValue = data.ambientIntensity;
        }

        so.ApplyModifiedProperties();
        Debug.Log("[Setup] Environment presets filled (Day/Night/Blend). Pending manual step: " +
                  "set each preset's rotation by eye in the Inspector (environmentRotationEuler) " +
                  "— that one's a visual judgment call, not something to script.");
    }

    // Copies the "ENVIRONMENT" sun+skybox rig from the BOXOPHOBIC demo scene into Arena
    // and wires it to EnvironmentManager.environment. Only the demo scene's in-memory
    // copy is touched — CloseScene(removeScene: true) below unloads it without saving,
    // so the demo scene asset on disk is never modified.
    private const string EnvironmentDemoScenePath =
        "Assets/BOXOPHOBIC/Skybox Cubemap Extended/Demo/Demo Day.unity";

    private static void EnsureEnvironmentRig(Scene arenaScene, EnvironmentPresetManager mgr)
    {
        var so = new SerializedObject(mgr);
        var envProp = so.FindProperty("environment");
        if (envProp.objectReferenceValue != null)
        {
            Debug.Log("[Setup] Environment rig already assigned — leaving it as-is");
            return;
        }

        var demoScene = EditorSceneManager.OpenScene(EnvironmentDemoScenePath, OpenSceneMode.Additive);
        GameObject sourceEnv = null;
        foreach (var root in demoScene.GetRootGameObjects())
        {
            if (root.name == "ENVIRONMENT") { sourceEnv = root; break; }
        }

        if (sourceEnv == null)
        {
            Debug.LogWarning("[Setup] Could not find 'ENVIRONMENT' in the demo scene — skipping rig placement");
            EditorSceneManager.CloseScene(demoScene, true);
            return;
        }

        var envInstance = Object.Instantiate(sourceEnv);
        envInstance.name = "environment";
        SceneManager.MoveGameObjectToScene(envInstance, arenaScene);
        envInstance.transform.position = Vector3.zero; // demo scene had it offset for its own layout

        // The demo copy comes in fully static (Contribute GI included), which auto-bakes
        // ~27MB of lightmaps on save — for a rotation of (0,0,0) that's a placeholder
        // until you tune environmentRotationEuler by eye. Baking now would just be
        // thrown away the moment that rotation changes, so strip static flags until then.
        foreach (var t in envInstance.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, (StaticEditorFlags)0);

        EditorSceneManager.CloseScene(demoScene, true); // unload only — never saved, so the demo asset is untouched

        envProp.objectReferenceValue = envInstance.transform;
        so.ApplyModifiedProperties();

        Debug.Log("[Setup] Environment rig copied from the demo scene and assigned to EnvironmentManager.environment");
    }

    private static void EnsureCanvasInScene()
    {
        var existingProxy = Object.FindFirstObjectByType<ColorButtonProxy>();
        if (existingProxy != null)
        {
            // Re-wire even if already present — fixes a stale per-instance OnClick
            // override (m_Target nulled out) that a Canvas placed before
            // ColorButtonProxy existed would otherwise keep forever.
            RewireColorButtons(existingProxy.gameObject, existingProxy);
            Debug.Log("[Setup] Canvas.prefab already in Arena — re-wired buttons");
            return;
        }

        var canvasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPrefabPath);
        if (canvasPrefab == null) { Debug.LogError($"[Setup] Not found: {CanvasPrefabPath}"); return; }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(canvasPrefab);
        var proxy = instance.GetComponent<ColorButtonProxy>() ?? instance.AddComponent<ColorButtonProxy>();
        RewireColorButtons(instance, proxy);
        Debug.Log("[Setup] Canvas.prefab placed in Arena and wired");
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
        // Remove the old standalone TimerCanvas if it's still around from before the
        // timer got consolidated into the main gameplay Canvas — one Canvas is enough.
        var oldCanvas = GameObject.Find("TimerCanvas");
        if (oldCanvas != null)
        {
            Object.DestroyImmediate(oldCanvas);
            Debug.Log("[Setup] Old standalone TimerCanvas removed");
        }

        // The timer lives inside Canvas.prefab's own Canvas — no second Canvas needed
        var proxy = Object.FindFirstObjectByType<ColorButtonProxy>();
        var mainCanvas = proxy != null ? proxy.GetComponent<Canvas>() : null;
        if (mainCanvas == null)
        {
            Debug.LogError("[Setup] Canvas.prefab not found in Arena — cannot place timer");
            return null;
        }

        var existingTimer = mainCanvas.transform.Find("TimerImage");
        if (existingTimer != null)
        {
            var existingImg = existingTimer.GetComponent<Image>();

            // Type must be Filled for the Fill* settings below to do anything — on
            // Simple, Unity silently ignores fillAmount and the timer renders as a
            // static, non-shrinking circle no matter what GameManager writes to it.
            if (existingImg.type != Image.Type.Filled)
            {
                existingImg.sprite        = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
                existingImg.type          = Image.Type.Filled;
                existingImg.fillMethod    = Image.FillMethod.Radial360;
                existingImg.fillOrigin    = (int)Image.Origin360.Top;
                existingImg.fillClockwise = true;
                existingImg.fillAmount    = 1f;
                Debug.Log("[Setup] TimerImage was Type=Simple (never shrinks) — fixed to Type=Filled");
            }
            else
            {
                Debug.Log("[Setup] TimerImage already exists — leaving it as-is");
            }
            return existingImg;
        }

        var imgGO = new GameObject("TimerImage");
        imgGO.transform.SetParent(mainCanvas.transform, false);
        var img = imgGO.AddComponent<Image>();

        // Built-in circular sprite — Image.Type.Filled needs a round shape to fill,
        // otherwise the radial mask traces the (square) RectTransform instead of a circle.
        img.sprite        = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        img.type          = Image.Type.Filled;
        img.fillMethod     = Image.FillMethod.Radial360;
        img.fillOrigin     = (int)Image.Origin360.Top;
        img.fillClockwise  = true; // shrinks like a clock running out, starting at 12 o'clock
        img.fillAmount     = 1f;
        img.color          = new Color(0.2f, 0.8f, 1f, 0.85f);
        img.raycastTarget  = false; // decorative only — must never block clicks to the color buttons

        // 90×90 at top-center, 10px below the top edge
        var rt = imgGO.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(90, 90);
        rt.anchorMin        = new Vector2(0.5f, 1f);
        rt.anchorMax        = new Vector2(0.5f, 1f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -10);

        Debug.Log("[Setup] Timer placed inside the main Canvas (no separate TimerCanvas)");
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
        rt.sizeDelta        = new Vector2(340, 70);
        rt.anchoredPosition = pos;

        var txtGO = new GameObject("Label");
        txtGO.transform.SetParent(go.transform, false);
        var tmp   = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.color     = Color.black;
        tmp.fontSize  = 26;
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
        rt.sizeDelta        = new Vector2(620, 90);
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
        rt.sizeDelta        = new Vector2(420, 70);
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
        tmp.fontSize = 26;
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
        ph.fontSize  = 26;
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

    // `??` checks for a raw C# null, bypassing Unity's overloaded `==` — if GetComponent
    // returns a "fake null" (a destroyed-but-still-referenced component, which Unity's
    // `==` correctly treats as null but `??` does not), `??` wrongly skips AddComponent
    // and hands back the broken reference. An explicit `== null` check uses the overload.
    private static T GetOrAdd<T>(this GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        return existing == null ? go.AddComponent<T>() : existing;
    }
}
