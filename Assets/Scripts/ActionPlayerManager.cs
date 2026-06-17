using Unity.Netcode;
using UnityEngine;
using TMPro;

// Represents one player's state: body color, weapon color, score.
// Each player owns their own object; only they can change their colors.
// Score is written by the server after each round.
public class ActionPlayerManager : NetworkBehaviour
{
    public enum ColorOption { Red = 0, Blue = 1, Green = 2, Yellow = 3 }

    [Header("Visuals — assign in prefab")]
    [SerializeField] private Renderer playerRenderer;
    [SerializeField] private Renderer weapon;
    [SerializeField] private TextMeshPro scoreText;

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

    // Server assigns slot 1-4 so the win screen can show "Jugador 2 ganó"
    public NetworkVariable<int> playerSlot = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private static readonly Color[] ColorMap =
    {
        Color.red,
        Color.blue,
        Color.green,
        Color.yellow
    };

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

            // FindFirstObjectByType is MPPM-safe (scoped per virtual player)
            Object.FindFirstObjectByType<LobbyUIManager>()?.SetLocalPlayer(this);
        }

        if (IsServer)
            GameManager.Instance?.RegisterPlayer(this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            GameManager.Instance?.UnregisterPlayer(this);
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

    private void ApplyBodyColor(int index)
    {
        if (playerRenderer != null)
            playerRenderer.material.color = ColorMap[Mathf.Clamp(index, 0, 3)];
    }

    private void ApplyWeaponColor(int index)
    {
        if (weapon != null)
            weapon.material.color = ColorMap[Mathf.Clamp(index, 0, 3)];
    }

    private void UpdateScoreText(int value)
    {
        if (scoreText != null)
            scoreText.text = value.ToString();
    }
}
