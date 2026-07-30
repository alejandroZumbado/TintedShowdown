using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

// Picks one of 3 skybox/lighting presets (Day / Night / Blend, matching the BOXOPHOBIC
// "Skybox Cubemap Extended" demo scenes) when Arena loads, and applies the SAME one on
// every client. It's shared environment dressing, not a per-player setting — the host
// rolls the index once and NetworkVariable replication keeps everyone in sync.
public class EnvironmentPresetManager : NetworkBehaviour
{
    [Tooltip("Sun + skybox rig, placed manually in the scene — rotated per preset.")]
    [SerializeField] private Transform environment;

    [SerializeField] private EnvironmentPreset[] presets = new EnvironmentPreset[3];

    private readonly NetworkVariable<int> _presetIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        _presetIndex.OnValueChanged += (_, newIndex) => Apply(newIndex);

        if (IsServer) RandomizePreset();

        Apply(_presetIndex.Value);
    }

    // Called by GameManager at the start of every round — server-authoritative, so all
    // 4 players always see the same preset (NetworkVariable replication handles the sync,
    // same as the initial pick in OnNetworkSpawn).
    public void RandomizePreset()
    {
        if (!IsServer) return;
        _presetIndex.Value = Random.Range(0, presets.Length);
    }

    private void Apply(int index)
    {
        if (index < 0 || index >= presets.Length) return;
        var p = presets[index];

        RenderSettings.skybox           = p.skyboxMaterial;
        RenderSettings.fog              = p.fogEnabled;
        RenderSettings.fogMode          = FogMode.Linear;
        RenderSettings.fogColor         = p.fogColor;
        RenderSettings.fogStartDistance = p.linearFogStart;
        RenderSettings.fogEndDistance   = p.linearFogEnd;
        RenderSettings.ambientMode      = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = p.ambientIntensity;
        DynamicGI.UpdateEnvironment(); // re-derives ambient probe from the new skybox

        if (environment == null) return;
        environment.localRotation = Quaternion.Euler(p.environmentRotationEuler);

        var sun = environment.GetComponentInChildren<Light>();
        if (sun != null)
        {
            sun.color     = p.lightColor;
            sun.intensity = p.lightIntensity;
        }
    }
}

[System.Serializable]
public class EnvironmentPreset
{
    public string name;
    public Material skyboxMaterial;

    [Tooltip("Set by eye once 'environment' is placed — the source demo scenes use a " +
             "different world layout, so their raw rotation values don't transfer 1:1.")]
    public Vector3 environmentRotationEuler;

    public Color lightColor = Color.white;
    public float lightIntensity = 1f;

    public bool fogEnabled = true;
    public Color fogColor = Color.gray;
    public float linearFogStart;
    public float linearFogEnd = 70f;

    public float ambientIntensity = 1f;
}
