using Dynamicweb.Application.UI;
using Dynamicweb.CoreUI.Actions.Implementations;
using Dynamicweb.CoreUI.Icons;
using Dynamicweb.CoreUI.Navigation;
using SJS.DW.PrivateNugetProvider.Queries;
using SJS.DW.PrivateNugetProvider.Screens;

namespace SJS.DW.PrivateNugetProvider.Providers;

public class PrivateFeedsNodeProvider : NavigationNodeProvider<SystemSection>
{
    public override IEnumerable<NavigationNode> GetRootNodes() => [];

    private static readonly string AddinsNodeId = "Settings_Addins";
    private const string NodeName = "Private NuGet Feeds";

    public override IEnumerable<NavigationNode> GetSubNodes(NavigationNodePath parentNodePath)
    {
        var nodes = new List<NavigationNode>();
        if (parentNodePath.Last.Equals(AddinsNodeId, StringComparison.OrdinalIgnoreCase))
        {
            nodes.Add(new NavigationNode
            {
                Id = "PrivateNugetFeeds",
                Title = NodeName,
                Name = NodeName,
                Icon = Icon.Clouds,
                NodeAction = NavigateScreenAction.To<PrivateFeedListScreen>()
                    .With(new PrivateFeedListQuery()),
                AddAction = NavigateScreenAction.To<PrivateFeedEditScreen>()
                
            });
        }

        return nodes;
    }
}