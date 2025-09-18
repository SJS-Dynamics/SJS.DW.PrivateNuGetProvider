using Dynamicweb.Updates;

namespace SJS.DW.PrivateNugetProvider.Providers;

public class PrivateFeedUpdateProvider : UpdateProvider
{
    private const string PrivateFeedGuid = "dd74e352-8c68-4b38-b1af-e27eba265b87";

    public override IEnumerable<Update> GetUpdates()
    {
        // Log in Dynamicweb so we know we made it here
        Dynamicweb.Logging.LogManager
            .Current
            .GetLogger("UPDATEPROVIDER")
            .Debug("Preparing SQL/table updates for MyUpdateProvider");
        
        return [
            SqlUpdate.AddTable(PrivateFeedGuid, this, "PrivateFeeds", @"(
                [FeedId] [bigint] IDENTITY(1,1) NOT NULL,
                [Name] [nvarchar](255) NOT NULL,
                [Url] [nvarchar](2000) NOT NULL,
                [Username] [nvarchar](255) NULL,
                [Password] [nvarchar](255) NULL,
                CONSTRAINT [PK_PrivateFeed] PRIMARY KEY CLUSTERED ([FeedId] ASC)
            )")
        ];
    }
    
}