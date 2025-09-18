using System.Data;
using Dynamicweb.CoreUI.Data;
using Dynamicweb.Data;
using Dynamicweb.Security.SystemTools;
using SJS.DW.PrivateNugetProvider.Models;

namespace SJS.DW.PrivateNugetProvider.Queries;

public sealed class PrivateFeedListQuery : DataQueryListBase<LimitedNugetFeedModel, NugetFeedModel>
{
    protected override IEnumerable<LimitedNugetFeedModel> MapModels(IEnumerable<NugetFeedModel> items)
    {
        return items.Select(i => new LimitedNugetFeedModel
        {
            FeedId = i.FeedId,
            Name = i.Name,
            Url = i.Url,
            Username = i.Username
        });
    }

    protected override IEnumerable<NugetFeedModel> GetListItems()
    {
        var feeds = new List<NugetFeedModel>();
        var sql = CommandBuilder.Create("SELECT FeedId, Name, Url, Username FROM PrivateFeeds");
        using var reader = Database.CreateDataReader(sql);
        while (reader.Read())
        {
            feeds.Add(ExtractModel(reader));
        }

        return feeds;
    }

    private NugetFeedModel ExtractModel(IDataReader reader)
    {
        var model = new NugetFeedModel
        {
            FeedId = reader.GetInt64(0),
            Name = reader.GetString(1),
            Url = reader.GetString(2)
        };
        
        try{
            if (!reader.IsDBNull(3))
                model.Username = reader.GetString(3);
        }
        catch (Exception ex)
        {
            Dynamicweb.Logging.LogManager.Current.GetLogger("PrivateFeedListQuery").Error("Error reading Username from database", ex);
        }

        return model;
    }
    
}

public sealed class PrivateFeedFullListQuery : DataQueryListBase<NugetFeedModel, NugetFeedModel>
{
    protected override IEnumerable<NugetFeedModel> MapModels(IEnumerable<NugetFeedModel> items)
    {
        return items;
    }

    protected override IEnumerable<NugetFeedModel> GetListItems()
    {
        var feeds = new List<NugetFeedModel>();
        var sql = CommandBuilder.Create("SELECT FeedId, Name, Url, Username, Password FROM PrivateFeeds");
        using var reader = Database.CreateDataReader(sql);
        while (reader.Read())
        {
            feeds.Add(ExtractModel(reader));
        }

        return feeds;
    }

    private NugetFeedModel ExtractModel(IDataReader reader)
    {
        var model = new NugetFeedModel
        {
            FeedId = reader.GetInt64(0),
            Name = reader.GetString(1),
            Url = reader.GetString(2)
        };
        
        try{
            if (!reader.IsDBNull(3))
                model.Username = reader.GetString(3);
        }
        catch (Exception ex)
        {
            Dynamicweb.Logging.LogManager.Current.GetLogger("PrivateFeedFullListQuery").Error("Error reading Username from database", ex);
        }
        
        try{
            if (!reader.IsDBNull(4))
            {
                var password = reader.GetString(4);
                model.Password = string.IsNullOrEmpty(password) ? null : Crypto.Decrypt(password);
            }
        }
        catch (Exception ex)
        {
            Dynamicweb.Logging.LogManager.Current.GetLogger("PrivateFeedFullListQuery").Error("Error reading Password from database", ex);
        }

        return model;
    }
    
}