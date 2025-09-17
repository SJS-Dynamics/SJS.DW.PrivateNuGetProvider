using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace SJS.DW.PrivateNugetProvider.Models;

public static class NugetFeedsConfig
{
    public static List<NugetFeed> Feeds { get; set; } = new();
}

public class NugetFeed
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Username { get; set; }
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