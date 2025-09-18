using Dynamicweb.CoreUI.Data;

namespace SJS.DW.PrivateNugetProvider.Models;

public class LimitedNugetFeedModel : DataViewModelBase
{
    public long? FeedId { get; set; }
    
    [ConfigurableProperty]
    public string Name { get; set; }
    
    [ConfigurableProperty]
    public string Url { get; set; }
    
    [ConfigurableProperty]
    public string? Username { get; set; }
}