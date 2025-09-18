using SJS.DW.PrivateNugetProvider.Queries;

namespace SJS.DW.PrivateNugetProvider.Models;

public static class NugetFeedsConfig
{
    private static bool _initialized = false;
    private static List<NugetFeedModel> _feeds = new();

    /// <summary>
    /// Feeds to be used by the application. This property can only be set once.
    /// </summary>
    public static List<NugetFeedModel> Feeds
    {
        set
        {
            if (_initialized) 
                return;
            
            _feeds = value;
            _initialized = true;
        }
    }

    /// <summary>
    /// Load feeds from the database and merge with existing feeds from configuration.
    /// </summary>
    /// <returns></returns>
    public static List<NugetFeedModel> LoadFeeds()
    {
        var query = new PrivateFeedFullListQuery();
        var dbFeed = query.GetModel()?.Data.ToList() ?? [];
        
        // Compare and add new feeds
        foreach (var feed in from feed in dbFeed let sameFeed = _feeds.Any(f =>
                     f.Url.Equals(feed.Url, StringComparison.OrdinalIgnoreCase) &&
                     f.Username == feed.Username
                 ) where !sameFeed select feed)
        {
            _feeds.Add(feed);
        }
        
        return _feeds;
    }
}