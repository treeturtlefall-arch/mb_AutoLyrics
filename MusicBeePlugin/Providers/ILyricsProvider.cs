namespace MusicBeePlugin.Providers
{
    public interface ILyricsProvider
    {
        string Name { get; }

        string FetchLyrics(string artist, string trackTitle, string album);
    }
}
