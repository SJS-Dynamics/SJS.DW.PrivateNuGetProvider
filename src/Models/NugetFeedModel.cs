using System.ComponentModel.DataAnnotations.Schema;
using Dynamicweb.CoreUI.Data;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace SJS.DW.PrivateNugetProvider.Models;

public class NugetFeedModel : DataViewModelBase
{
    [Column("Id")]
    public long? FeedId { get; set; }
    
    [ConfigurableProperty]
    public string Name { get; set; }
    
    [ConfigurableProperty]
    public string Url { get; set; }
    
    [ConfigurableProperty]
    public string? Username { get; set; }
    
    [ConfigurableProperty]
    public string? Password { get; set; }
    
    // Return SourceRepository
    private SourceRepository? _sourceRepository;
    
    public SourceRepository? GetSourceRepository()
    {
        if (_sourceRepository != null)
            return _sourceRepository;

        if (string.IsNullOrEmpty(Url))
            return null;

        var packageSource = new PackageSource(Url);

        if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
        {
            packageSource.Credentials = new PackageSourceCredential(
                source: Url,
                username: Username,
                passwordText: Password,
                isPasswordClearText: true,
                validAuthenticationTypesText: null);
        }

        _sourceRepository = new SourceRepository(packageSource, Repository.Provider.GetCoreV3());
        return _sourceRepository;
    }
}