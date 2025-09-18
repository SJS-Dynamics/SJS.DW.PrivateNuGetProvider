using Dynamicweb.CoreUI.Actions;
using Dynamicweb.CoreUI.Actions.Implementations;
using Dynamicweb.CoreUI.Data;
using Dynamicweb.CoreUI.Icons;
using Dynamicweb.CoreUI.Lists;
using Dynamicweb.CoreUI.Lists.ViewMappings;
using Dynamicweb.CoreUI.Screens;
using SJS.DW.PrivateNugetProvider.Models;
using SJS.DW.PrivateNugetProvider.Queries;

namespace SJS.DW.PrivateNugetProvider.Screens;

public class PrivateFeedListScreen : ListScreenBase<LimitedNugetFeedModel>
{
    
    protected override string GetScreenName() => "Private NuGet Feeds";

    protected override ActionNode GetManageAction()
    {
        return new ActionNode
        {
            Name = "Add New Feed",
            Icon = Icon.Plus,
            NodeAction = NavigateScreenAction.To<PrivateFeedEditScreen>()
        };
    }

    protected override IEnumerable<ActionGroup> GetListContextActions()
    {
        var actions = new List<ActionGroup>();
        var createAction = new ActionNode
        {
            Name = "Edit",
            Icon = Icon.Edit,
            NodeAction = NavigateScreenAction.To<PrivateFeedEditScreen>()
        };

        actions.Add(createAction);

        return actions;
    }
    
    protected override ActionBase GetListItemPrimaryAction(LimitedNugetFeedModel model) => 
        NavigateScreenAction.To<PrivateFeedEditScreen>().With(new PrivateFeedByIdQuery()
        {
            FeedId = model.FeedId
        });

    protected override IEnumerable<string> GetExcludedFilters()
    {
        return ["FeedId", "Password"];
    }

    protected override IEnumerable<ListViewMapping> GetViewMappings()
    {
        var row = new RowViewMapping
        {
            Columns = new List<ModelMapping>
            {
                CreateMapping(p => p.Name),
                CreateMapping(p => p.Url),
                CreateMapping(p => p.Username)
            }
        };
        
        return new ListViewMapping[] { row };
    }
}