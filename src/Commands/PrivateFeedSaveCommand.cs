using Dynamicweb.Core;
using Dynamicweb.CoreUI.Data;
using Dynamicweb.Data;
using Dynamicweb.Security.SystemTools;
using SJS.DW.PrivateNugetProvider.Models;

namespace SJS.DW.PrivateNugetProvider.Commands;

public class PrivateFeedSaveCommand : CommandBase<NugetFeedModel>
{

    public override CommandResult Handle()
    {
        if (Model == null)
            return new CommandResult
            {
                Status = CommandResult.ResultType.Error,
            };
        
        var encryptedPassword = string.IsNullOrEmpty(Model.Password) 
            ? null 
            : Crypto.Encrypt(Model.Password);

        using var conn = Database.CreateConnection();
        if (Model.FeedId is null or <= 0)
        {
            var insert = new CommandBuilder();
            insert.Add("INSERT INTO PrivateFeeds (Name, Url, Username, Password)");
            insert.Add("OUTPUT INSERTED.FeedId");
            insert.Add("VALUES ({0}, {1}, {2}, {3})", Model.Name, Model.Url, Model.Username, encryptedPassword);
            Model.FeedId = Converter.ToInt64(Database.ExecuteScalar(insert, conn));
        }
        else
        {
            var update = new CommandBuilder();
            update.Add("UPDATE PrivateFeeds SET");
            update.Add("Name = {0}, Url = {1}, Username = {2}",
                Model.Name, Model.Url, Model.Username);
            if (encryptedPassword != null)
            {
                update.Add(", Password = {0}", encryptedPassword);
            }
            update.Add("WHERE FeedId = {0}", Model.FeedId);
            Database.ExecuteNonQuery(update, conn);
        }
        
        return new CommandResult
        {
            Model = Model,
            Status = CommandResult.ResultType.Ok
        };
    }
}

public class PrivateFeedDeleteCommand :CommandBase<NugetFeedModel>
{
    public long? FeedId { get; set; }
    
    public override CommandResult Handle()
    {
        if(FeedId is null or <= 0)
            return new CommandResult
            {
                Status = CommandResult.ResultType.Error,
            };
        
        using var conn = Database.CreateConnection();
        var delete = new CommandBuilder();
        delete.Add("DELETE FROM PrivateFeeds WHERE FeedId = {0}", FeedId);
        Database.ExecuteNonQuery(delete, conn);
        
        return new CommandResult
        {
            Status = CommandResult.ResultType.Ok
        };
    }
}