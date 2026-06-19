using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using TMPro;

// Represents one player's state: body color, weapon color, score.
// Each player owns their own object; only they can change their colors.
// Score is written by the server after each round.
public class ActionPlayerManager : NetworkBehaviour
{
    public enum ColorOption { Red = 0, Blue = 1, Green = 2, Yellow = 3 }

    // Drop the parent GameObject of a body part here (e.g. "L_Leg") — every Renderer
    // found anywhere in its hierarchy (it, its children, its children's children, etc.)
    // gets painted with that list's color. Needed because parts like a leg are built out
    // of several separate cube pieces, each with its own Renderer.
    [Header("Body parts — tinted with bodyColor (chest, arms, legs, etc.)")]
    [SerializeField] private GameObject[] bodyParts;

    [Header("Weapon parts — tinted with weaponColor")]
    [SerializeField] private GameObject[] weaponParts;

    [Header("Score text")]
    [SerializeField] private TextMeshPro scoreText;

    [Header("Per-player camera — child of this prefab, disabled by default")]
    [SerializeField] private Camera playerCamera;

    [Header("Round result sounds")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip dealtDamageClip; // played when you scored on someone this round
    [SerializeField] private AudioClip tookDamageClip;  // played when someone scored on you this round

    // Owner writes; everyone reads — player controls their own colors
    public NetworkVariable<int> bodyColor = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public NetworkVariable<int> weaponColor = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // Server writes; everyone reads — server is authoritative on score
    public NetworkVariable<int> score = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Server assigns slot 1-4 — used as a fallback display name if the player never set one
    public NetworkVariable<int> playerSlot = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Owner writes once at spawn, from PlayerPrefs — read by everyone so the win screen
    // can show the name the player picked instead of "Jugador N".
    public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private static readonly Color[] ColorMap =
    {
        Color.red,
        Color.blue,
        Color.green,
        Color.yellow
    };

    // Whichever camera is currently active for the LOCAL viewer — not necessarily this
    // player's own camera, since every client billboards every player's score text
    // toward their own point of view.
    private Camera billboardCamera;

    // Flat materials swapped onto each part's renderers, cached once per renderer.
    // The character's baked texture has solid-black regions — multiplying that texture
    // by any tint color leaves it black (0 * anything = 0), so tinting the existing
    // material can never produce a clean, readable color. Replacing the material outright
    // with an unlit flat color sidesteps that entirely.
    private Material[] bodyPaintMaterials;
    private Material[] weaponPaintMaterials;

    private void Awake()
    {
        bodyPaintMaterials = CreatePaintMaterials(bodyParts);
        weaponPaintMaterials = CreatePaintMaterials(weaponParts);
    }

    private static Material[] CreatePaintMaterials(GameObject[] parts)
    {
        if (parts == null) return System.Array.Empty<Material>();

        var shader = Shader.Find("Unlit/Color");
        var materials = new System.Collections.Generic.List<Material>();
        foreach (var part in parts)
        {
            if (part == null) continue;
            foreach (var r in part.GetComponentsInChildren<Renderer>(true))
            {
                var mat = new Material(shader);
                r.material = mat;
                materials.Add(mat);
            }
        }
        return materials.ToArray();
    }

    private void LateUpdate()
    {
        if (scoreText == null) return;

        // Re-find only when the cached camera is gone/inactive — the active camera
        // rarely changes after spawn. FindFirstObjectByType is MPPM-safe (returns the
        // one active camera in this client's own world), unlike Camera.main/tag lookups.
        if (billboardCamera == null || !billboardCamera.gameObject.activeInHierarchy)
            billboardCamera = Object.FindFirstObjectByType<Camera>();

        if (billboardCamera != null)
            scoreText.transform.LookAt(billboardCamera.transform);
    }

    public override void OnNetworkSpawn()
    {
        // React to network changes and update visuals on all devices
        bodyColor.OnValueChanged += (_, v) => ApplyBodyColor(v);
        weaponColor.OnValueChanged += (_, v) => ApplyWeaponColor(v);
        score.OnValueChanged += (_, v) => UpdateScoreText(v);

        // Late-join snap: apply current values immediately
        ApplyBodyColor(bodyColor.Value);
        ApplyWeaponColor(weaponColor.Value);
        UpdateScoreText(score.Value);

        if (IsOwner)
        {
            // Random starting colors — owner writes directly to their NetworkVariables
            bodyColor.Value = Random.Range(0, 4);
            weaponColor.Value = Random.Range(0, 4);

            // The name UI in GameMenu guarantees this is set before a match can start —
            // PlayerPrefs.GetString fallback only matters if something skipped that flow.
            // FixedString32Bytes only holds ~28 UTF-8 bytes; clamp defensively since
            // accented characters take 2 bytes each and would otherwise throw.
            string savedName = PlayerPrefs.GetString("PlayerName", "Jugador");
            if (savedName.Length > 16) savedName = savedName.Substring(0, 16);
            playerName.Value = new FixedString32Bytes(savedName);

            // FindFirstObjectByType is MPPM-safe (scoped per virtual player)
            Object.FindFirstObjectByType<LobbyUIManager>()?.SetLocalPlayer(this);

            // Switch to this player's own camera — every client otherwise shares the
            // same static scene camera, so everyone sees the exact same fixed angle
            // instead of looking at their own character.
            //
            // FindObjectsByType (not GameObject.FindWithTag!) is the MPPM-safe way to do
            // this. Tag-based and other global lookups are NOT guaranteed to be scoped to
            // the current virtual player in Multiplayer Play Mode — that's exactly the
            // class of bug already documented for static fields in this project; it
            // applies here too. FindObjectsByType only returns ACTIVE objects by default,
            // and playerCamera starts disabled, so this reliably finds just this world's
            // static scene camera and nothing belonging to another virtual player.
            if (playerCamera != null)
            {
                foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    if (cam != playerCamera) cam.gameObject.SetActive(false);
                }
                playerCamera.gameObject.SetActive(true);
            }
        }

        // FindFirstObjectByType instead of a static Instance — there is exactly one
        // GameManager per Arena scene load, and this avoids any stale-reference risk
        // (same MPPM-safe convention used for LobbyUIManager).
        if (IsServer)
            Object.FindFirstObjectByType<GameManager>()?.RegisterPlayer(this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            Object.FindFirstObjectByType<GameManager>()?.UnregisterPlayer(this);
    }

    // Called by LobbyUIManager from the 4 body-color buttons
    public void ChangeColor(int colorIndex)
    {
        if (!IsOwner) return;
        bodyColor.Value = colorIndex;
    }

    // Called by LobbyUIManager from the 4 weapon-color buttons
    public void AttackColor(int colorIndex)
    {
        if (!IsOwner) return;
        weaponColor.Value = colorIndex;
    }

    // Called by GameManager.EvaluateRound for every player, every round. Broadcasting to
    // everyone and filtering by IsOwner here is simpler than wiring up ClientRpcParams to
    // target a single client.
    [ClientRpc]
    public void PlayRoundResultClientRpc(bool dealtDamage, bool tookDamage)
    {
        if (!IsOwner) return;

        // Dealing damage takes priority — if you did both this round, only the
        // "dealt damage" sound plays, never both.
        if (dealtDamage)
            PlaySound(dealtDamageClip);
        else if (tookDamage)
            PlaySound(tookDamageClip);
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private void ApplyBodyColor(int index) => ApplyColor(bodyPaintMaterials, index);

    private void ApplyWeaponColor(int index) => ApplyColor(weaponPaintMaterials, index);

    private static void ApplyColor(Material[] materials, int index)
    {
        Color color = ColorMap[Mathf.Clamp(index, 0, 3)];
        foreach (var m in materials)
            m.color = color;
    }

    private void UpdateScoreText(int value)
    {
        if (scoreText != null)
            scoreText.text = value.ToString();
    }
}
