using MediaBrowser.Model.Providers;

namespace ThePornDB.Models
{
    public sealed class MatchedSearchResult : RemoteSearchResult
    {
        public Scene Scene { get; set; }

        public double Score { get; set; }
    }
}
