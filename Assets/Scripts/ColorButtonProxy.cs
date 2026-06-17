using UnityEngine;

// Attach to Canvas.prefab root in the Arena scene.
// Routes color button clicks to LobbyUIManager at runtime.
// Uses FindFirstObjectByType instead of static Instance — safe in MPPM Simulator mode.
public class ColorButtonProxy : MonoBehaviour
{
    public void OnBodyColor(int colorIndex)
    {
        Object.FindFirstObjectByType<LobbyUIManager>()?.OnBodyColorButton(colorIndex);
    }

    public void OnWeaponColor(int colorIndex)
    {
        Object.FindFirstObjectByType<LobbyUIManager>()?.OnWeaponColorButton(colorIndex);
    }
}
