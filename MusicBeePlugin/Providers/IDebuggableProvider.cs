using System.Collections.Generic;

namespace MusicBeePlugin.Providers
{
    public interface IDebuggableProvider
    {
        IEnumerable<string> DebugSearch(string artist, string trackTitle);
    }
}
