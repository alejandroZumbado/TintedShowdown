using System.Collections.Generic;
using UnityEngine;

// Plain C# class (not a Component) — created and driven entirely by GameManager,
// server-side only. Weapon and body color are decided independently (see DecideWeapon/
// DecideBody) so GameManager can put each on its own timer — otherwise every bot flips
// both colors in the same instant, which reads as robotic/synced instead of alive.
public class BotController
{
    private readonly ActionPlayerManager player;

    public BotController(ActionPlayerManager player)
    {
        this.player = player;
    }

    // Weapon = the body color most common among opponents → hits the most enemies
    public void DecideWeapon(IReadOnlyList<ActionPlayerManager> allPlayers)
    {
        var counts = CountColors(allPlayers, p => p.bodyColor.Value);
        player.AttackColor(PickBest(counts, wantMax: true));
    }

    // Body = the weapon color least common among opponents → dodges the most attacks
    public void DecideBody(IReadOnlyList<ActionPlayerManager> allPlayers)
    {
        var counts = CountColors(allPlayers, p => p.weaponColor.Value);
        player.ChangeColor(PickBest(counts, wantMax: false));
    }

    // Convenience for callers that want both at once (initial spawn colors, and a
    // "sniper" bot's single last-instant reveal) — just calls both independently.
    public void Decide(IReadOnlyList<ActionPlayerManager> allPlayers)
    {
        DecideWeapon(allPlayers);
        DecideBody(allPlayers);
    }

    private int[] CountColors(IReadOnlyList<ActionPlayerManager> allPlayers, System.Func<ActionPlayerManager, int> select)
    {
        var counts = new int[4];
        foreach (var other in allPlayers)
        {
            if (other == player) continue;
            counts[select(other)]++;
        }
        return counts;
    }

    // Finds the index with the highest (or lowest) count, breaking ties randomly among
    // whichever indices share that extreme value — avoids every bot converging on the
    // same "first match wins" color whenever counts are tied (including all-zero).
    private static int PickBest(int[] counts, bool wantMax)
    {
        int best = wantMax ? int.MinValue : int.MaxValue;
        for (int i = 0; i < counts.Length; i++)
        {
            if (wantMax ? counts[i] > best : counts[i] < best)
                best = counts[i];
        }

        var candidates = new List<int>(4);
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] == best) candidates.Add(i);

        return candidates[Random.Range(0, candidates.Count)];
    }
}
