using Dynamicweb.Application.UI.Helpers;
using Dynamicweb.CoreUI.Actions;
using Dynamicweb.CoreUI.Data;
using Dynamicweb.CoreUI.Editors;
using Dynamicweb.CoreUI.Editors.Inputs;
using Dynamicweb.CoreUI.Screens;
using SJS.DW.PrivateNugetProvider.Commands;
using SJS.DW.PrivateNugetProvider.Models;

namespace SJS.DW.PrivateNugetProvider.Screens;

public class PrivateFeedEditScreen : EditScreenBase<NugetFeedModel>
{
    protected override string GetScreenName() => "Edit Private NuGet Feed";
    
    protected override void BuildEditScreen()
    {
        AddComponents("General", "Feed Info", new []
        {
            EditorFor(m => m.Name),
            EditorFor(m => m.Url),
            EditorFor(m => m.Username),
            EditorFor(m => m.Password),
        });
        
    }

    protected override IEnumerable<ActionGroup>? GetScreenActions()
    {
        if (Model?.FeedId is null or 0)
            return null;
        
        var deleteAction = ActionBuilder.Delete(
                new PrivateFeedDeleteCommand() { FeedId = Model.FeedId },
                "Delete the Feed?",
                $"Do you want to delete the Feed: {Model.Name}")
            .WithLabel("Delete");
        
        var actions = new List<ActionGroup>();
        actions.Add(deleteAction);
        return actions;
        

    }

    protected override CommandBase<NugetFeedModel> GetSaveCommand() =>
        new PrivateFeedSaveCommand();

    protected override EditorBase GetEditor(string property) => property switch
    {
        nameof(Model.Password) => new Password(),
        _ => new Text(),
    };

}