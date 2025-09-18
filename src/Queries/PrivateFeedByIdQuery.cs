using Dynamicweb.CoreUI.Data;
using Dynamicweb.Data;
using SJS.DW.PrivateNugetProvider.Models;

namespace SJS.DW.PrivateNugetProvider.Queries;

public class PrivateFeedByIdQuery : DataQueryModelBase<NugetFeedModel>
{
    public long? FeedId { get; set; }
    
    public override NugetFeedModel? GetModel()
    {
        if (FeedId == null)
            return null;

        var sql = CommandBuilder.Create(
            "SELECT FeedId, Name, Url, Username FROM PrivateFeeds WHERE FeedId = {0}", 
            FeedId);
        using var reader = Database.CreateDataReader(sql);
        if (!reader.Read()) 
            return null;
        
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
            Dynamicweb.Logging.LogManager.Current.GetLogger("PrivateFeedByIdQuery")
                .Error("Error reading Username from database", ex);
        }

        return model;

    }
}