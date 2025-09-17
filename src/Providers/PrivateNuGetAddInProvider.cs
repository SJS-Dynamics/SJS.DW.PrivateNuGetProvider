using Dynamicweb.Marketplace.Providers;
using System.Collections.Concurrent;
using System.Runtime.Loader;
using Dynamicweb.Configuration;
using Dynamicweb.Core;
using Dynamicweb.Extensibility.AddIns;
using Dynamicweb.Marketplace;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;
using SJS.DW.PrivateNugetProvider.Models;

namespace SJS.DW.PrivateNugetProvider.Providers;

public sealed class PrivateNuGetAddInProvider : AddinProvider
{
    private const int MAX_CONCURRENT_DOWNLOAD_THREADS = 10;
    private const string OBSOLOTE_TAG = "Obsolete";

    private static ILogger Logger { get; set; } = NullLogger.Instance;
    private static SourceCacheContext PackageCache { get; set; } = new() { DirectDownload = true };
    private static readonly Lazy<HashSet<NuGetFramework>> _validFrameworks = new(() =>
    {
        var set = new HashSet<NuGetFramework>//TODO: Load from settings / configuration page
        {
            NuGetFramework.AnyFramework,
            NuGetFramework.AgnosticFramework,
            NuGetFramework.Parse("netstandard2.0"),
            NuGetFramework.Parse("net5.0"),
            NuGetFramework.Parse("net6.0"),
            NuGetFramework.Parse("net7.0"),
            NuGetFramework.Parse("net8.0"),
            NuGetFramework.Parse("net9.0"),
            NuGetFramework.Parse("net10.0"),
            NuGetFramework.Parse("net11.0"),
            NuGetFramework.Parse("net12.0")
        };
        return set;
    });
    
    private static HashSet<NuGetFramework> ValidFrameworks => _validFrameworks.Value;

    private static Dictionary<string, List<IPackageSearchMetadata>> _feedList = new(StringComparer.OrdinalIgnoreCase);
    
    public override async Task<IEnumerable<AddinInfo>> Search(string? searchTerm = null, int take = 1000, int skip = 0)
    {
        var cancellationToken = CancellationToken.None;
        var searchFilter = new SearchFilter(true)
        {
            IncludeDelisted = false, 
        };

        var oldAppDataValue = Environment.GetEnvironmentVariable("APPDATA");
        Environment.SetEnvironmentVariable("APPDATA", SystemInformation.MapPath("/Files/System"));

        var addins = new List<AddinInfo>();
        var tasks = new List<Task>();

        foreach (var repository in NugetFeedsConfig.Feeds.Select(f => f.GetSourceRepository()))
        {
            // First, get the search results to find package IDs
            if (repository == null) 
                continue;
            
            var searchResource = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
            var searchResults = await searchResource.SearchAsync(
                searchTerm,
                searchFilter,
                skip,
                take,
                Logger,
                cancellationToken);

            // Then, for each package ID, get ALL versions using PackageMetadataResource
            var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
            var allPackageVersions = new List<IPackageSearchMetadata>();
            
            foreach (var searchResult in searchResults)
            {
                var allVersions = await metadataResource.GetMetadataAsync(
                    searchResult.Identity.Id, 
                    includePrerelease: true, 
                    includeUnlisted: false, 
                    PackageCache, 
                    Logger, 
                    cancellationToken);
        
                allPackageVersions.AddRange(allVersions);
            }

            if(_feedList.TryGetValue(repository.PackageSource.Source, out List<IPackageSearchMetadata>? existing))
            {
                var toAdd = allPackageVersions.Where(r => 
                    !existing.Any(e => e.Identity.Id.Equals(r.Identity.Id, StringComparison.OrdinalIgnoreCase) 
                                       && e.Identity.Version == r.Identity.Version)).ToList();
                existing.AddRange(toAdd);
            }
            else
            {
                _feedList[repository.PackageSource.Source] = allPackageVersions.ToList();
            }

            var loadedAssemblies = GetLoadedAssembliesVersions();
            
            // Find latest package
            var latestPackages = allPackageVersions
                .Where(p => !p.Tags.Contains(OBSOLOTE_TAG))
                .GroupBy(p => p.Identity.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p => p.Identity.Version)
                    .First());
            
            // Add to addins list
            foreach (var package in latestPackages)
            {
                var compatible = package.DependencySets.SelectMany(ds => ds.Packages)
                    .All(p => !loadedAssemblies.TryGetValue(p.Id, out var val) || p.VersionRange.Satisfies(val));
                
                addins.Add(ToAddinInfo(package, loadedAssemblies, compatible));
            }
            
            async Task GetLatestApplicablePackage(IPackageSearchMetadata packageSearchMetaData)
            {
                if (packageSearchMetaData.Tags.Contains(OBSOLOTE_TAG)) return;
                
                var metadataSource = await repository.GetResourceAsync<PackageMetadataResource>();
                var orderedPackageVersions = (await metadataSource.GetMetadataAsync(packageSearchMetaData.Identity.Id, true, false, PackageCache, Logger, cancellationToken)).OrderByDescending(m => m.Identity.Version);

                var package = orderedPackageVersions.FirstOrDefault(m =>
                    m.DependencySets.SelectMany(ds => ds.Packages)
                        .All(p => !loadedAssemblies.TryGetValue(p.Id, out var val) || p.VersionRange.Satisfies(val)));

                if (package is not null)
                    addins.Add(ToAddinInfo(package, loadedAssemblies, true));
                else if (orderedPackageVersions.Any())
                    addins.Add(ToAddinInfo(orderedPackageVersions.First(), loadedAssemblies, false));
            }
        }

        await Task.WhenAll(tasks);

        Environment.SetEnvironmentVariable("APPDATA", oldAppDataValue);

        return addins;
    }

    /// <summary>
    /// Tests the package for compatibility with the running application.
    /// </summary>
    /// <param name="id">Name of package</param>
    /// <param name="version">Optional version of package</param>
    /// <returns>resolvedPackage</returns>
    public override async Task<ResolvedPackage?> Validate(string id, NuGetVersion? version)
    {
        var cancellationToken = CancellationToken.None;
        version ??= new("1.0.0");

        var oldAppDataValue = Environment.GetEnvironmentVariable("APPDATA");
        Environment.SetEnvironmentVariable("APPDATA", SystemInformation.MapPath("/Files/System"));
        
        // Get 
        var repository = GetRepository(id, version);
        
        if (repository == null) 
            throw new ValidationException("Package not found in any configured feed.");

        var nugetResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        using MemoryStream packageStream = new();
        await nugetResource.CopyNupkgToStreamAsync(
            id,
            new NuGetVersion(version),
            packageStream,
            PackageCache,
            Logger,
            cancellationToken);

        Environment.SetEnvironmentVariable("APPDATA", oldAppDataValue);

        if (packageStream?.Length == 0) return default;

        using PackageArchiveReader archive = new(packageStream);

        // This concat is necessary to get the framework dependency for a package with no dependencies...
        var supportedFrameworks = archive.GetSupportedFrameworks()
            .Concat(archive.GetPackageDependencies().Select(pd => pd.TargetFramework));
        
        if (!supportedFrameworks.Intersect(ValidFrameworks).Any()) throw new ValidationException($"Package does not support current installation. Package supports: \"{string.Join("\", \"", supportedFrameworks)}\"");
        return await InternalResolve(id, new PackageIdentity(id, new NuGetVersion(version)), 0);
    }

    public async Task Download(string id)
    {
        var resolved = await Resolve(id);
        if (resolved == null) return;

        var downloaded = new ConcurrentDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        await InternalDownload(resolved, downloaded, null);
    }
    public async Task Download(string id, NuGetVersion version, ConcurrentDictionary<string, object?> downloaded)
    {
        var resolved = new ResolvedPackage() { Package = id, Version = version };
        await InternalDownload(resolved, downloaded, null);
    }

    public async Task Download(string id, NuGetVersion version, ConcurrentDictionary<string, object?> downloaded, string saveLocation)
    {
        var resolved = new ResolvedPackage() { Package = id, Version = version };
        await InternalDownload(resolved, downloaded, saveLocation).ConfigureAwait(true);
    }
    
    public static ApplicationResponse ReInstallAddInDependencies(IEnumerable<string>? dependencies)
    {
        var dependencyList = dependencies?.ToList();

        if (dependencyList is null || dependencyList.Count == 0)
            return new(true);

        var addIns = MarketplaceService.GetAllAddins();

        dependencyList.Reverse();

        foreach (var dependency in dependencyList)
        {
            if (IsRazorTemplateDependency(dependency))
                continue;

            var dependencyAddin = addIns.FirstOrDefault(a => a.Name == dependency);
            if (dependencyAddin is null)
                return new(false, $"Couldn't find addin with name {dependency}!");

            MarketplaceService.Install(dependencyAddin.Package + dependencyAddin.Extension, dependencyAddin.Version?.ToString(), dependencyAddin.AddinProvider ?? typeof(LocalAddinProvider).FullName ?? "");
        }

        return new(true);
    }

    public static ApplicationResponse UninstallAddInDependencies(string package)
    {
        if (package is null)
            return new(false, "Dependency is null");

        if (GetAssemblyLoadContextByAssemblyName(package) is not AssemblyLoadContext assemblyLoadContext)
        {
            return UninstallAddin(package);
        }

        ApplicationResponse? response = null;
        var dependencies = new List<string>(AssemblyLoadContextState.GetDependencies(assemblyLoadContext));

        if (dependencies.Count == 0)
        {
            return new(true, new List<string>());
        }


        //uninstall all dependency momentarily
        foreach (var dependency in dependencies)
        {
            var res = UninstallAddInDependencies(dependency);

            if (!res.Succeeded)
                return res;

            var uninstalledDependencies = new List<string>((IEnumerable<string>?)res.Data ?? []);

            if (IsRazorTemplateDependency(dependency))
            {
                AssemblyLoadContextState.RemoveDependency(dependency);
                response = new ApplicationResponse(true);
            }
            else
            {
                response = UninstallAddin(dependency);
            }

            if (!response.Succeeded)
                return response;

            var alreadyRegisteredUninstalledDependencies = (List<string>?)res.Data ?? [];
            alreadyRegisteredUninstalledDependencies.Add(dependency);

            if (uninstalledDependencies.Count > 0)
                alreadyRegisteredUninstalledDependencies.AddRange(uninstalledDependencies);

            response = new(true, alreadyRegisteredUninstalledDependencies);
        }

        return new(true, (IEnumerable<string>?)response?.Data ?? []);
    }


    private static bool IsRazorTemplateDependency(string dependency) => dependency.Contains("CompiledRazorTemplates", StringComparison.OrdinalIgnoreCase);

    private async Task InternalDownload(ResolvedPackage resolved, ConcurrentDictionary<string, object?> downloaded, string? saveLocation)
    {
        if (SystemConfiguration.Instance.GetBoolean("/Globalsettings/System/AddIns/EnableLogging"))
            Logger.LogDebug($"Current contexts: {string.Join(", ", AssemblyLoadContextState.Contexts.Where(c => !(c.Name?.StartsWith("CompiledRazorTemplates.Dynamic", StringComparison.OrdinalIgnoreCase) ?? true)).Select(c => c.Name))}\n" +
                $"Trying to install/update {resolved.Package}");
        if (AssemblyLoadContextState.Contexts.Any(alc => alc.Name?.Equals(resolved.Package, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            var res = UninstallAddin(resolved.Package);
            if (!res.Succeeded)
                throw new TypeAccessException(res.Message);
        }
        
        var version = resolved.Version;
        saveLocation ??= GetPathToId(resolved.Package, version);
        await ResolveAndDownload(saveLocation, resolved, downloaded).ConfigureAwait(true);
    }

    //Recursive download of specified package and all it's dependencies
    private async Task ResolveAndDownload(string saveLocation, ResolvedPackage package, ConcurrentDictionary<string, object?> downloaded)
    {
        if (downloaded is null || package is null || !downloaded.TryAdd(package.Package, null)) return; //if already downloaded a version, skip            
        var cancellationToken = CancellationToken.None;
        var foundFramework = await DownloadPackage(package, saveLocation, cancellationToken);
        if (package.Dependencies.Any())
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = MAX_CONCURRENT_DOWNLOAD_THREADS,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(package.Dependencies, options, async (dependency, token) =>
            {
                var dependencyPackage = ToPackageIdentity(dependency);
                var dependencyInfo = await InternalResolve(dependencyPackage.Id, dependencyPackage);
                if (dependencyInfo != null)
                    await ResolveAndDownload(saveLocation, dependencyInfo, downloaded);
            });
        }
    }

    //Downloads the specified package
    private async Task<NuGetFramework?> DownloadPackage(ResolvedPackage resolved, string saveLocation, CancellationToken cancellationToken)
    {
        var oldAppDataValue = Environment.GetEnvironmentVariable("APPDATA");
        Environment.SetEnvironmentVariable("APPDATA", SystemInformation.MapPath("/Files/System"));
        
        // Get repository
        var repository = GetRepository(resolved.Package, resolved.Version);
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

        using MemoryStream packageStream = new();
        await resource.CopyNupkgToStreamAsync(
            resolved.Package,
            resolved.Version,
            packageStream,
            PackageCache,
            Logger,
            cancellationToken);

        using PackageArchiveReader archive = new(packageStream);

        NuGetFramework? framework;

        var lib = GetCorrectFramework(archive.GetLibItems(), ValidFrameworks, out framework);
        var content = GetCorrectFramework(archive.GetContentItems(), ValidFrameworks, out _);
        var files = archive.GetFiles("Files");
        if (files.Any())
        {
            var basePath = SystemInformation.MapPath("/Files");
            foreach (var file in files)
            {
                // Removing the "/Files" from the destination as the basePath includes the "Files" path itself, even if it's not called "Files", however we expect the package to have the "Files" root for everything that should be extracted.
                archive.ExtractFile(file, Path.Combine(basePath, file[6..]), Logger);
            }
        }
        var allItems = lib.Union(content);

        var loadedAssemblies = GetDefaultAssemblies();
        resolved.Dependencies = archive.GetPackageDependencies()
            .SelectMany(d => d.Packages.Select(p => new Dynamicweb.Marketplace.PackageDependency() { Name = p.Id, VersionRange = p.VersionRange }))
            .Where(d => !loadedAssemblies.ContainsKey(d.Name) || loadedAssemblies[d.Name] < (d.VersionRange?.MinVersion 
                ?? new("1.0.0")))
            .ToList();

        SaveFiles(allItems, saveLocation, (file, fullPath) => archive.ExtractFile(file, fullPath, Logger));
        archive.Dispose();
        packageStream.Dispose();

        Environment.SetEnvironmentVariable("APPDATA", oldAppDataValue);

        return framework;
    }

    public override async Task<ResolvedPackage?> Resolve(string id, NuGetVersion? version)
    {
        var preference = version != null ? new PackageIdentity(id, new NuGetVersion(version)) : null;
        return await InternalResolve(id, preference, 2);
    }

    internal async Task<ResolvedPackage?> InternalResolve(string id, PackageIdentity? preference, int recursive = 0)
    {
        var cancellationToken = CancellationToken.None;

        var oldAppDataValue = Environment.GetEnvironmentVariable("APPDATA");
        Environment.SetEnvironmentVariable("APPDATA", SystemInformation.MapPath("/Files/System"));

        var repository = GetRepository(id, preference?.Version ?? new NuGetVersion("1.0.0"));
        var resource = await repository.GetResourceAsync<DependencyInfoResource>();
        var dependencyInfo = await resource.ResolvePackages(
          id,
          PackageCache,
          Logger,
          cancellationToken);
        var validVersions = GetAvailablePackages(dependencyInfo);
        if (!validVersions.Any()) return default;

        var preferedPackageVersions = preference == null ? Enumerable.Empty<PackageIdentity>() : new[] { preference };
        var resolverContext = new PackageResolverContext(
            DependencyBehavior.Ignore,
            [id],
            [],
            [],
            preferedPackageVersions,
            validVersions,
            [repository.PackageSource],
            Logger);

        var resolver = new PackageResolver();
        var resolvedPackages = resolver.Resolve(resolverContext, cancellationToken);
        var resolvedVersion = resolvedPackages?.FirstOrDefault()?.Version;
        foreach (var validVersion in validVersions)
        {
            if (!validVersion.Version.Equals(resolvedVersion)) 
                continue;
            
            var resolved = ToResolvedPackage(validVersion);
            if (recursive != 0)
            {
                foreach (var dependency in resolved.Dependencies.ToArray())
                {
                    var version = dependency.VersionRange?.MinVersion ?? new("1.0.0");
                    var resolvedDependency = await InternalResolve(dependency.Name, new PackageIdentity(dependency.Name, version), recursive - 1);
                    if (resolvedDependency != null)
                        resolved.Dependencies.AddRange(resolvedDependency.Dependencies);
                }
                resolved.Dependencies = resolved.Dependencies.Distinct().ToList();
            }
            Environment.SetEnvironmentVariable("APPDATA", oldAppDataValue);

            return resolved;
        }
        Environment.SetEnvironmentVariable("APPDATA", oldAppDataValue);

        return default;
    }

    private static IEnumerable<SourcePackageDependencyInfo> GetAvailablePackages(IEnumerable<RemoteSourceDependencyInfo> sourceDependencies)
    {
        int minor = 500;
        int major = 500;
        var found = new List<SourcePackageDependencyInfo>();
        int currentMajor = -1;
        var repository = GetRepository(sourceDependencies.First().Identity.Id, sourceDependencies.First().Identity.Version);
        foreach (var sourceDependency in sourceDependencies.Reverse())
        {
            if (major == 0) break;
            if (!sourceDependency.Listed) continue;

            var version = sourceDependency.Identity.Version;

            if (currentMajor != version.Major)
            {
                currentMajor = version.Major;
                major--;
            }
            else if (minor <= 0) continue;


            var id = sourceDependency.Identity.Id;
            foreach (var dg in sourceDependency.DependencyGroups)
            {
                if (ValidFrameworks.Contains(dg.TargetFramework))
                {
                    minor--;
                    var dependencyInfo = new SourcePackageDependencyInfo(id, version, dg.Packages, sourceDependency.Listed, repository);
                    found.Add(dependencyInfo);
                    break;
                }
            }
        }
        return found;
    }

    private static IEnumerable<string> GetCorrectFramework(IEnumerable<FrameworkSpecificGroup> frameworkItems, HashSet<NuGetFramework> validFrameworks, out NuGetFramework? framework)
    {
        // We check all our valid frameworks in the reverse order to always target the newest framework that's valid
        foreach (var validFramework in validFrameworks.Reverse())
        {
            var frameworkItem = frameworkItems.FirstOrDefault(item => item.TargetFramework == validFramework);
            if (frameworkItem is not null && frameworkItem.Items.Any())
            {
                framework = frameworkItem.TargetFramework;
                return frameworkItem.Items;
            }
        }

        framework = null;
        return [];
    }

    private ResolvedPackage ToResolvedPackage(SourcePackageDependencyInfo validVersion)
    {
        Dictionary<string, NuGetVersion> loadedPackages = GetLoadedAssembliesVersions();
        var resolved = new ResolvedPackage()
        {
            Package = validVersion.Id,
            Version = validVersion.Version,
            Dependencies = validVersion.Dependencies.Select(d => new Dynamicweb.Marketplace.PackageDependency()
            {
                Name = d.Id, 
                VersionRange = d.VersionRange
            }).ToList()
        };
        if (loadedPackages.TryGetValue(resolved.Package, out NuGetVersion? version))
            resolved.InstalledVersion = version;
        return resolved;
    }

    private static PackageIdentity ToPackageIdentity(Dynamicweb.Marketplace.PackageDependency dependency)
    {
        return new PackageIdentity(dependency.Name, dependency.VersionRange?.MinVersion ?? new("1.0.0"));
    }

    private static AddinInfo ToAddinInfo(IPackageSearchMetadata item, Dictionary<string, NuGetVersion> loadedAssemblies, bool compatible)
    {
        var addin = new AddinInfo
        {
            Image = item.IconUrl?.AbsoluteUri ?? "",
            Name = item.Title,
            Package = item.Identity.Id,
            Author = string.Join(',', item.Authors),
            Description = item.Description,
            Version = item.Identity.Version,
            Categories = GetCategoriesFromTags(item.Tags),
            AddinProvider = typeof(PrivateNuGetAddInProvider).FullName ?? "",
            Compatible = compatible,
            Downloads = item.DownloadCount,
        };
        if (loadedAssemblies.TryGetValue(addin.Package, out NuGetVersion? version))
            addin.InstalledVersion = version.ToString();

        SetDependencies(addin, addin.Package);

        return addin;
    }
    public override Task Install(string packageId, NuGetVersion? version = null) => Install(packageId, version, false);

    public override async Task Install(string package, NuGetVersion? version = null, bool queue = false)
    {
        var downloaded = new ConcurrentDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (version == null)
        {
            var resolved = await Resolve(package).ConfigureAwait(false);
            if (resolved == null) return;
            version = resolved.Version;
            await InternalDownload(resolved, downloaded, null).ConfigureAwait(false);
        }
        else
        {
            await Download(package, version, downloaded).ConfigureAwait(false);
        }
        if (!queue)
            await LoadIntoMemory(package, version).ConfigureAwait(false);
    }

    #region "IDisposeable"
    
    private bool disposedValue;
    
    protected void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                PackageCache.Dispose();
            }

            disposedValue = true;
        }
    }

    private static SourceRepository? GetRepository(string id, NuGetVersion version)
    {
        var findMetaData = _feedList.Values.SelectMany(f => f)
            .FirstOrDefault(p => 
                p.Identity.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                && p.Identity.Version == version);
        
        var repoKey = _feedList.FirstOrDefault(f => f.Value.Contains(findMetaData)).Key;
        return NugetFeedsConfig.Feeds.Select(f => f.GetSourceRepository())
            .FirstOrDefault(r => r.PackageSource.Source.Equals(repoKey, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}