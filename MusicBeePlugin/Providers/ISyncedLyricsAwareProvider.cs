namespace MusicBeePlugin.Providers
{
    public interface ISyncedLyricsAwareProvider
    {
        string FetchLyrics(string artist, string trackTitle, string album, double? durationSeconds, bool preferSynchronized);
    }
}
