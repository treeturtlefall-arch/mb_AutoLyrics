using System.Linq;

namespace MusicBeePlugin.Providers
{
    public static class ProviderRegistry
    {
        public static ILyricsProvider[] CreateAllProviders()
        {
            return new ILyricsProvider[]
            {
                new KashiNaviProvider(),
                new UtaTenProvider(),
                new JLyricProvider(),
                new UtaNetProvider(),
                new PetitLyricsProvider(),
                new LrcLibProvider(),
                new GeniusProvider(),
                new OriconProvider()
            };
        }

        public static string[] GetAllProviderNames()
        {
            return CreateAllProviders().Select(p => p.Name).ToArray();
        }
    }
}
