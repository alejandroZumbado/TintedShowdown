using UnityEngine;

// Local per-device match counters (PlayerPrefs), split solo-vs-bots ("offline") from
// real multiplayer ("online") since they answer different questions for the player.
// Plain static class, not a MonoBehaviour — PlayerPrefs itself is already global/static,
// no GameObject needed to read or write it.
public static class MatchStats
{
    private const string OfflinePlayedKey = "Stats_OfflinePlayed";
    private const string OfflineWonKey    = "Stats_OfflineWon";
    private const string OnlinePlayedKey  = "Stats_OnlinePlayed";
    private const string OnlineWonKey     = "Stats_OnlineWon";

    public static int OfflinePlayed => PlayerPrefs.GetInt(OfflinePlayedKey, 0);
    public static int OfflineWon    => PlayerPrefs.GetInt(OfflineWonKey, 0);
    public static int OnlinePlayed  => PlayerPrefs.GetInt(OnlinePlayedKey, 0);
    public static int OnlineWon     => PlayerPrefs.GetInt(OnlineWonKey, 0);

    public static void RecordMatchStarted(bool isSolo)
    {
        Increment(isSolo ? OfflinePlayedKey : OnlinePlayedKey);
    }

    public static void RecordWin(bool isSolo)
    {
        Increment(isSolo ? OfflineWonKey : OnlineWonKey);
    }

    private static void Increment(string key)
    {
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
        PlayerPrefs.Save();
    }
}
