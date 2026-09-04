namespace MusicBeePlugin.Providers
{
    public interface IDurationAwareProvider
    {
        string FetchLyrics(string artist, string trackTitle, string album, double? durationSeconds);
    }
}
