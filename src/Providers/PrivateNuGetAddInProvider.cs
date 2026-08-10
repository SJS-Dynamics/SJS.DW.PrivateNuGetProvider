using Dynamicweb.Marketplace.Providers;
using System.Collections.Concurrent;
using Dynamicweb.Configuration;
using Dynamicweb.Core;
using Dynamicweb.Extensibility.AddIns;
using Dynamicweb.Marketplace;
using Markdig;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using SJS.DW.PrivateNugetProvider.Models;
using DwLogManager = Dynamicweb.Logging.LogManager;

namespace SJS.DW.PrivateNugetProvider.Providers;

public sealed class PrivateNuGetAddInProvider : AddinProvider, IDisposable
{
    private const string DefaultVersion = "1.0.0";
    private const string AppData = "APPDATA";
    private const string FilePath = "/Files/System";
    private const int MaxConcurrentDownloadThreads = 10;
    private const string ObsoleteTag = "Obsolete";
    private const string NuGetOrgV3Source = "https://api.nuget.org/v3/index.json";

    private static ILogger Logger { get; set; } = NullLogger.Instance;
    private static SourceCacheContext PackageCache { get; set; } = new() { DirectDownload = true };

    private static readonly Lazy<SourceRepository> NuGetOrgDependencyRepository =
        new(() => Repository.Factory.GetCoreV3(NuGetOrgV3Source));

    private static IEnumerable<SourceRepository> GetConfiguredRepositories()
    {
        return NugetFeedsConfig
            .LoadFeeds()
            .Select(feed => feed.GetSourceRepository())
            .Where(repository => repository is not null)
            .Cast<SourceRepository>();
    }

    private static IEnumerable<SourceRepository> GetDependencyRepositories()
    {
        foreach (var repository in GetConfiguredRepositories())
        {
            yield return repository;
        }

        yield return NuGetOrgDependencyRepository.Value;
    }

    private static readonly NuGetFramework TargetFramework =
        NuGetFramework.Parse("net10.0");

    private static readonly Dictionary<string, List<IPackageSearchMetadata>> FeedList =
        new(StringComparer.OrdinalIgnoreCase);

    private static void LogFeedFailure(SourceRepository repository, string operation, Exception exception)
    {
        LogProviderWarning(
            $"NuGet feed '{repository.PackageSource.Name ?? repository.PackageSource.Source}' failed during {operation}. " +
            "The feed was skipped and remaining feeds will still be processed.",
            exception);
    }

    private static void LogProviderWarning(string message, Exception exception)
    {
        // Failure reporting must never turn an optional-feed failure back into a request failure.
        try
        {
            DwLogManager.Current
                .GetLogger(nameof(PrivateNuGetAddInProvider))
                .Warn(message, exception);
        }
        catch
        {
            try
            {
                Logger.LogWarning($"{message} {exception.GetType().Name}: {exception.Message}");
            }
            catch
            {
                // Logging is best effort; the provider must remain non-fatal.
            }
        }
    }

    public override async Task<IEnumerable<AddinInfo>> Search(string? searchTerm = null, int take = 1000, int skip = 0)
    {
        var cancellationToken = CancellationToken.None;
        var searchFilter = new SearchFilter(true)
        {
            IncludeDelisted = false,
        };

        var oldAppDataValue = Environment.GetEnvironmentVariable(AppData);
        try
        {
            Environment.SetEnvironmentVariable(AppData, SystemInformation.MapPath(FilePath));

            var addins = new List<AddinInfo>();

            foreach (var repository in GetConfiguredRepositories())
            {
                try
                {
                    // First, get the search results to find package IDs
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

                    if (FeedList.TryGetValue(repository.PackageSource.Source, out var existing))
                    {
                        var toAdd = allPackageVersions.Where(r =>
                            !existing.Any(e => e.Identity.Id.Equals(r.Identity.Id, StringComparison.OrdinalIgnoreCase)
                                               && e.Identity.Version == r.Identity.Version)).ToList();
                        existing.AddRange(toAdd);
                    }
                    else
                    {
                        FeedList[repository.PackageSource.Source] = allPackageVersions.ToList();
                    }

                    var loadedAssemblies = GetLoadedAssembliesVersions();

            // Find latest package
                    var latestPackages = allPackageVersions
                        .Where(p => !p.Tags.Contains(ObsoleteTag))
                        .GroupBy(p => p.Identity.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(p => p.Identity.Version).First());

            // Add to addins list
                    foreach (var package in latestPackages)
                    {
                        var compatible = package.DependencySets.SelectMany(ds => ds.Packages)
                            .All(p => !loadedAssemblies.TryGetValue(p.Id, out var val) || p.VersionRange.Satisfies(val));

                        addins.Add(ToAddinInfo(package, loadedAssemblies, compatible));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogFeedFailure(repository, "package search", ex);
                }
            }

            return addins;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogProviderWarning(
                "Private NuGet provider search failed before an individual feed could be processed. " +
                "No private packages will be returned for this request.",
                ex);
            return [];
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppData, oldAppDataValue);
        }
    }

    /// <summary>
    /// Tests the package for compatibility with the running application.
    /// </summary>
    /// <param name="id">Name of package</param>
    /// <param name="version">Optional version of package</param>
    /// <returns>resolvedPackage</returns>
    public override async Task<ResolvedPackage?> Validate(
        string id,
        NuGetVersion? version)
    {
        var cancellationToken = CancellationToken.None;

        var oldAppDataValue =
            Environment.GetEnvironmentVariable(AppData);

        try
        {
            Environment.SetEnvironmentVariable(
                AppData,
                SystemInformation.MapPath(FilePath));

            /*
             * Dynamicweb normally supplies the version during validation.
             * If it does not, locate the newest available private-feed version
             * rather than assuming 1.0.0.
             */
            var locatedPackage = await LocatePackageAsync(
                id,
                version,
                allowNuGetOrg: false,
                cancellationToken);

            if (locatedPackage == null)
            {
                throw new ValidationException(
                    $"Package '{id}' was not found in any configured feed.");
            }

            var resolvedVersion = locatedPackage.Identity.Version;
            var repository = locatedPackage.Repository;

            var resource =
                await repository.GetResourceAsync<FindPackageByIdResource>(
                    cancellationToken);

            using var packageStream = new MemoryStream();

            var copied = await resource.CopyNupkgToStreamAsync(
                id,
                resolvedVersion,
                packageStream,
                PackageCache,
                Logger,
                cancellationToken);

            if (!copied || packageStream.Length == 0)
                return null;

            packageStream.Position = 0;

            using var archive =
                new PackageArchiveReader(packageStream);

            var supportedFrameworks = archive
                .GetSupportedFrameworks()
                .Concat(
                    archive.GetPackageDependencies()
                        .Select(group => group.TargetFramework))
                .Distinct()
                .ToList();

            /*
             * Some packages have no lib framework and no framework-specific
             * dependencies, such as content-only packages. Do not reject those
             * solely because the framework list is empty.
             */
            if (supportedFrameworks.Count > 0)
            {
                var reducer = new FrameworkReducer();

                var nearestFramework = reducer.GetNearest(
                    TargetFramework,
                    supportedFrameworks);

                var hasAgnosticFramework =
                    supportedFrameworks.Any(framework =>
                        framework.IsAny ||
                        framework.IsAgnostic);

                if (nearestFramework == null && !hasAgnosticFramework)
                {
                    throw new ValidationException(
                        $"Package '{id}' does not support the current " +
                        $"target framework '{TargetFramework.GetShortFolderName()}'. " +
                        $"Package supports: " +
                        $"\"{string.Join(
                            "\", \"",
                            supportedFrameworks.Select(
                                framework =>
                                    framework.GetShortFolderName()))}\"");
                }
            }

            return await InternalResolve(
                id,
                new PackageIdentity(id, resolvedVersion),
                dependencyOnly: false,
                cancellationToken: cancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AppData,
                oldAppDataValue);
        }
    }

    public async Task Download(string id)
    {
        var resolved = await Resolve(id);

        if (resolved == null)
            return;

        var downloaded =
            new ConcurrentDictionary<PackageIdentity, byte>(
                PackageIdentityComparer.Default);

        await InternalDownload(
            resolved,
            downloaded,
            null);
    }

    public async Task Download(string id, NuGetVersion version, ConcurrentDictionary<PackageIdentity, byte> downloaded)
    {
        var resolved = new ResolvedPackage() { Package = id, Version = version };
        await InternalDownload(resolved, downloaded, null);
    }

    public async Task Download(string id, NuGetVersion version, ConcurrentDictionary<PackageIdentity, byte> downloaded,
        string saveLocation)
    {
        var resolved = new ResolvedPackage() { Package = id, Version = version };
        await InternalDownload(resolved, downloaded, saveLocation).ConfigureAwait(true);
    }

    public static ApplicationResponse ReInstallAddInDependencies(IEnumerable<string>? dependencies)
    {
        var dependencyList = dependencies?.ToList();

        if (dependencyList is null || dependencyList.Count == 0)
            return new(true);

        var addIns = MarketplaceService.GetAllAddins().ToList();

        dependencyList.Reverse();

        foreach (var dependency in dependencyList)
        {
            if (IsRazorTemplateDependency(dependency))
                continue;

            var dependencyAddin = addIns.FirstOrDefault(a => a.Name == dependency);
            if (dependencyAddin is null)
                return new(false, $"Couldn't find addin with name {dependency}!");

            MarketplaceService.Install(dependencyAddin.Package + dependencyAddin.Extension,
                dependencyAddin.Version?.ToString(), dependencyAddin.AddinProvider);
        }

        return new(true);
    }

    public static ApplicationResponse UninstallAddInDependencies(string package)
    {
        if (string.IsNullOrWhiteSpace(package))
            return new(false, "Dependency is null");

        if (GetAssemblyLoadContextByAssemblyName(package) is not { } assemblyLoadContext)
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


    private static bool IsRazorTemplateDependency(string dependency) =>
        dependency.Contains("CompiledRazorTemplates", StringComparison.OrdinalIgnoreCase);

    private async Task InternalDownload(ResolvedPackage resolved,
        ConcurrentDictionary<PackageIdentity, byte> downloaded, string? saveLocation)
    {
        if (SystemConfiguration.Instance.GetBoolean("/Globalsettings/System/AddIns/EnableLogging"))
            Logger.LogDebug(
                $"Current contexts: {string.Join(", ", AssemblyLoadContextState.Contexts.Where(c => !(c.Name?.StartsWith("CompiledRazorTemplates.Dynamic", StringComparison.OrdinalIgnoreCase) ?? true)).Select(c => c.Name))}\n" +
                $"Trying to install/update {resolved.Package}");
        if (AssemblyLoadContextState.Contexts.Any(alc =>
                alc.Name?.Equals(resolved.Package, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            var res = UninstallAddin(resolved.Package);
            if (!res.Succeeded)
                throw new TypeAccessException(res.Message);
        }

        var version = resolved.Version;
        saveLocation ??= GetPathToId(resolved.Package, version);
        await ResolveAndDownload(saveLocation, resolved, downloaded).ConfigureAwait(true);
    }

    private sealed record LocatedPackage(
        PackageIdentity Identity,
        SourceRepository Repository);

    private static async Task<LocatedPackage?> LocatePackageAsync(
        string packageId,
        NuGetVersion? requestedVersion,
        bool allowNuGetOrg,
        CancellationToken cancellationToken)
    {
        var repositories = allowNuGetOrg
            ? GetDependencyRepositories()
            : GetConfiguredRepositories();

        foreach (var repository in repositories)
        {
            try
            {
                var resource =
                    await repository.GetResourceAsync<FindPackageByIdResource>(
                        cancellationToken);

                if (requestedVersion != null)
                {
                    var exists = await resource.DoesPackageExistAsync(
                    packageId,
                    requestedVersion,
                    PackageCache,
                    Logger,
                    cancellationToken);

                    if (exists)
                    {
                        return new LocatedPackage(
                        new PackageIdentity(packageId, requestedVersion),
                        repository);
                }

                    continue;
                }

                var versions = await resource.GetAllVersionsAsync(
                packageId,
                PackageCache,
                Logger,
                cancellationToken);

            var availableVersions = versions.ToList();

            // Prefer the newest stable version.
            var selectedVersion = availableVersions
                .Where(version => !version.IsPrerelease)
                .OrderByDescending(version => version)
                .FirstOrDefault();

            // Fall back to prerelease only when no stable version exists.
            selectedVersion ??= availableVersions
                .OrderByDescending(version => version)
                .FirstOrDefault();

                if (selectedVersion != null)
                {
                    return new LocatedPackage(
                    new PackageIdentity(packageId, selectedVersion),
                    repository);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogFeedFailure(repository, $"resolution of package '{packageId}'", ex);
            }
        }

        return null;
    }

    //Recursive download of specified package and all it's dependencies
    private async Task ResolveAndDownload(
        string saveLocation,
        ResolvedPackage package,
        ConcurrentDictionary<PackageIdentity, byte> downloaded,
        bool dependencyOnly = false)
    {
        var version = package.Version
                      ?? throw new InvalidOperationException(
                          $"Package '{package.Package}' has no resolved version.");

        var identity = new PackageIdentity(
            package.Package,
            version);

        if (!downloaded.TryAdd(identity, 0))
            return;

        var cancellationToken = CancellationToken.None;

        await DownloadPackage(
            package,
            saveLocation,
            dependencyOnly,
            cancellationToken);

        if (package.Dependencies.Count == 0)
            return;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrentDownloadThreads,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            package.Dependencies,
            options,
            async (dependency, token) =>
            {
                var dependencyIdentity =
                    await ResolveDependencyIdentityAsync(
                        dependency,
                        token);

                if (dependencyIdentity == null)
                {
                    throw new InvalidOperationException(
                        $"Could not resolve dependency " +
                        $"'{dependency.Name}' matching version range " +
                        $"'{dependency.VersionRange}'. Private feeds and " +
                        $"nuget.org were searched.");
                }

                var dependencyInfo = await InternalResolve(
                    dependencyIdentity.Id,
                    dependencyIdentity,
                    recursive: 0,
                    dependencyOnly: true,
                    cancellationToken: token);

                if (dependencyInfo == null)
                {
                    throw new InvalidOperationException(
                        $"Dependency '{dependencyIdentity}' was found, " +
                        $"but its package metadata could not be resolved.");
                }

                await ResolveAndDownload(
                    saveLocation,
                    dependencyInfo,
                    downloaded,
                    dependencyOnly: true);
            });
    }

    //Downloads the specified package
    private async Task<NuGetFramework?> DownloadPackage(
        ResolvedPackage resolved,
        string saveLocation,
        bool dependencyOnly,
        CancellationToken cancellationToken)
    {
        var oldAppDataValue =
            Environment.GetEnvironmentVariable(AppData);

        try
        {
            Environment.SetEnvironmentVariable(
                AppData,
                SystemInformation.MapPath(FilePath));

            var version = resolved.Version
                          ?? throw new InvalidOperationException(
                              $"Package '{resolved.Package}' has no resolved version.");

            var repository = await FindRepositoryAsync(
                resolved.Package,
                version,
                allowNuGetOrg: dependencyOnly,
                cancellationToken);

            if (repository == null)
            {
                var sourceDescription = dependencyOnly
                    ? "the configured private feeds or nuget.org"
                    : "the configured private feeds";

                throw new InvalidOperationException(
                    $"Could not find package '{resolved.Package}' " +
                    $"version '{version}' in {sourceDescription}.");
            }

            var resource =
                await repository.GetResourceAsync<FindPackageByIdResource>(
                    cancellationToken);

            using var packageStream = new MemoryStream();

            var copied = await resource.CopyNupkgToStreamAsync(
                resolved.Package,
                version,
                packageStream,
                PackageCache,
                Logger,
                cancellationToken);

            if (!copied || packageStream.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Package '{resolved.Package}' version '{version}' " +
                    $"was located but could not be downloaded.");
            }

            packageStream.Position = 0;

            using var archive =
                new PackageArchiveReader(packageStream);

            NuGetFramework? selectedFramework;

            var libGroups = archive
                .GetLibItems()
                .ToList();

            var lib = GetCorrectFramework(
                libGroups,
                out selectedFramework);

            if (libGroups.Count > 0 && !lib.Any())
            {
                throw new InvalidOperationException(
                    $"Package '{resolved.Package}' version '{version}' " +
                    $"does not contain compatible library assets for " +
                    $"'{TargetFramework.GetShortFolderName()}'.");
            }

            var content = GetCorrectFramework(
                archive.GetContentItems(),
                out _);

            /*
             * If the package has lib assets but none are compatible, fail
             * instead of silently installing an add-in without its DLLs.
             */
            var allLibGroups = archive.GetLibItems().ToList();

            if (allLibGroups.Count > 0 && !lib.Any())
            {
                var packageFrameworks = allLibGroups
                    .Select(group =>
                        group.TargetFramework.GetShortFolderName());

                throw new InvalidOperationException(
                    $"Package '{resolved.Package}' version '{version}' " +
                    $"does not contain compatible library assets for " +
                    $"'{TargetFramework.GetShortFolderName()}'. " +
                    $"Available frameworks: " +
                    $"\"{string.Join("\", \"", packageFrameworks)}\"");
            }

            var files = archive.GetFiles("Files").ToList();

            if (files.Count > 0)
            {
                var basePath =
                    SystemInformation.MapPath("/Files");

                foreach (var file in files)
                {
                    if (file.Length <= 6)
                        continue;

                    var relativePath = file[6..];

                    var destinationPath = Path.Combine(
                        basePath,
                        relativePath);

                    archive.ExtractFile(
                        file,
                        destinationPath,
                        Logger);
                }
            }

            var loadedAssemblies =
                GetDefaultAssemblies();

            var dependencyGroups =
                await archive.GetPackageDependenciesAsync(
                    cancellationToken);

            var dependencyGroup =
                GetBestDependencyGroup(dependencyGroups);

            resolved.Dependencies = dependencyGroup?.Packages
                                        .Select(packageDependency =>
                                            new Dynamicweb.Marketplace.PackageDependency
                                            {
                                                Name = packageDependency.Id,
                                                VersionRange =
                                                    packageDependency.VersionRange
                                            })
                                        .Where(dependency =>
                                            !loadedAssemblies.TryGetValue(
                                                dependency.Name,
                                                out var loadedVersion)
                                            ||
                                            dependency.VersionRange == null
                                            ||
                                            !dependency.VersionRange.Satisfies(
                                                loadedVersion))
                                        .ToList()
                                    ?? [];

            var allItems = lib.Union(content);

            SaveFiles(
                allItems,
                saveLocation,
                (file, fullPath) =>
                    archive.ExtractFile(
                        file,
                        fullPath,
                        Logger));

            return selectedFramework;
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AppData,
                oldAppDataValue);
        }
    }

    public override async Task<ResolvedPackage?> Resolve(string id, NuGetVersion? version)
    {
        var preference = version != null ? new PackageIdentity(id, new NuGetVersion(version)) : null;
        return await InternalResolve(id, preference, 2);
    }

    private static PackageDependencyGroup? GetBestDependencyGroup(
        IEnumerable<PackageDependencyGroup> groups)
    {
        var groupList = groups.ToList();

        if (groupList.Count == 0)
            return null;

        var reducer = new FrameworkReducer();

        var nearestFramework = reducer.GetNearest(
            TargetFramework,
            groupList.Select(group => group.TargetFramework));

        if (nearestFramework != null)
        {
            return groupList.FirstOrDefault(group =>
                group.TargetFramework.Equals(nearestFramework));
        }

        return groupList.FirstOrDefault(group =>
            group.TargetFramework.IsAny ||
            group.TargetFramework.IsAgnostic);
    }

    internal async Task<ResolvedPackage?> InternalResolve(
        string id,
        PackageIdentity? preference,
        int recursive = 0,
        bool dependencyOnly = false,
        CancellationToken cancellationToken = default)
    {
        var oldAppDataValue =
            Environment.GetEnvironmentVariable(AppData);

        try
        {
            Environment.SetEnvironmentVariable(
                AppData,
                SystemInformation.MapPath(FilePath));

            /*
             * Root packages:
             *   Search configured private repositories only.
             *
             * Dependencies:
             *   Search configured private repositories first,
             *   then nuget.org.
             */
            var locatedPackage = await LocatePackageAsync(
                id,
                preference?.Version,
                allowNuGetOrg: dependencyOnly,
                cancellationToken);

            if (locatedPackage == null)
                return null;

            var repository = locatedPackage.Repository;
            var identity = locatedPackage.Identity;

            var dependencyResource =
                await repository.GetResourceAsync<DependencyInfoResource>(
                    cancellationToken);

            /*
             * Use ResolvePackage, singular.
             *
             * ResolvePackages(id, cache, logger, token) is not supported
             * by every DependencyInfoResource implementation and may throw
             * NotSupportedException.
             *
             * ResolvePackage also lets NuGet select the dependency group
             * compatible with TargetFramework, including compatible
             * netstandard2.0 and netstandard2.1 groups.
             */
            var sourcePackage =
                await dependencyResource.ResolvePackage(
                    identity,
                    TargetFramework,
                    PackageCache,
                    Logger,
                    cancellationToken);

            if (sourcePackage == null)
                return null;

            var resolved = ToResolvedPackage(sourcePackage);

            /*
             * Dynamicweb invokes Resolve with recursive = 2.
             *
             * This retains that behavior, although ResolveAndDownload()
             * will perform the complete recursive download afterward.
             */
            if (recursive > 0 && resolved.Dependencies.Count > 0)
            {
                var transitiveDependencies =
                    new List<Dynamicweb.Marketplace.PackageDependency>();

                foreach (var dependency in resolved.Dependencies.ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var dependencyIdentity =
                        await ResolveDependencyIdentityAsync(
                            dependency,
                            cancellationToken);

                    if (dependencyIdentity == null)
                    {
                        throw new InvalidOperationException(
                            $"Could not resolve dependency " +
                            $"'{dependency.Name}' matching version range " +
                            $"'{dependency.VersionRange}'. Private feeds and " +
                            $"nuget.org were searched.");
                    }

                    var resolvedDependency =
                        await InternalResolve(
                            dependencyIdentity.Id,
                            dependencyIdentity,
                            recursive - 1,
                            dependencyOnly: true,
                            cancellationToken: cancellationToken);

                    if (resolvedDependency != null)
                    {
                        transitiveDependencies.AddRange(
                            resolvedDependency.Dependencies);
                    }
                }

                resolved.Dependencies.AddRange(
                    transitiveDependencies);

                resolved.Dependencies = resolved.Dependencies
                    .GroupBy(
                        dependency =>
                            $"{dependency.Name}|" +
                            $"{dependency.VersionRange?.ToNormalizedString()}",
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }

            return resolved;
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AppData,
                oldAppDataValue);
        }
    }


    private static IEnumerable<string> GetCorrectFramework(
        IEnumerable<FrameworkSpecificGroup> frameworkItems,
        out NuGetFramework? selectedFramework)
    {
        var groups = frameworkItems
            .Where(group => group.Items.Any())
            .ToList();

        if (groups.Count == 0)
        {
            selectedFramework = null;
            return [];
        }

        var reducer = new FrameworkReducer();

        var nearestFramework = reducer.GetNearest(
            TargetFramework,
            groups.Select(group => group.TargetFramework));

        if (nearestFramework != null)
        {
            var nearestGroup = groups.FirstOrDefault(group =>
                group.TargetFramework.Equals(nearestFramework));

            if (nearestGroup != null)
            {
                selectedFramework = nearestGroup.TargetFramework;
                return nearestGroup.Items;
            }
        }

        // Explicit fallback for packages containing framework-agnostic assets.
        var fallbackGroup = groups.FirstOrDefault(group =>
            group.TargetFramework.IsAny ||
            group.TargetFramework.IsAgnostic);

        if (fallbackGroup != null)
        {
            selectedFramework = fallbackGroup.TargetFramework;
            return fallbackGroup.Items;
        }

        selectedFramework = null;
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

    // private static PackageIdentity ToPackageIdentity(Dynamicweb.Marketplace.PackageDependency dependency)
    // {
    //     return new PackageIdentity(dependency.Name, dependency.VersionRange?.MinVersion ?? new(DefaultVersion));
    // }

    private static async Task<PackageIdentity?>
        ResolveDependencyIdentityAsync(
            Dynamicweb.Marketplace.PackageDependency dependency,
            CancellationToken cancellationToken)
    {
        var range = dependency.VersionRange
                    ?? VersionRange.All;

        foreach (var repository in GetDependencyRepositories())
        {
            var resource =
                await repository.GetResourceAsync<FindPackageByIdResource>(
                    cancellationToken);

            var versions = (
                    await resource.GetAllVersionsAsync(
                        dependency.Name,
                        PackageCache,
                        Logger,
                        cancellationToken))
                .Where(range.Satisfies)
                .ToList();

            // Private repositories are enumerated first. Therefore, a private
            // compatible package takes precedence over a nuget.org package.
            var selectedVersion = versions
                .Where(version => !version.IsPrerelease)
                .OrderBy(version => version)
                .FirstOrDefault();

            selectedVersion ??= versions
                .OrderBy(version => version)
                .FirstOrDefault();

            if (selectedVersion != null)
            {
                return new PackageIdentity(
                    dependency.Name,
                    selectedVersion);
            }
        }

        return null;
    }

    private static AddinInfo ToAddinInfo(IPackageSearchMetadata item, Dictionary<string, NuGetVersion> loadedAssemblies,
        bool compatible)
    {
        string description = string.Empty;

        if (!string.IsNullOrWhiteSpace(item.ReadmeUrl.ToString()))
        {
            // read the readme file from the url and convert it to html
            using var client = new HttpClient();
            description = client.GetStringAsync(item.ReadmeUrl)
                .GetAwaiter()
                .GetResult();
        }
        
        if(string.IsNullOrWhiteSpace(description))
        {
            description = item.Description ?? "";
        }
        
        var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .Build();

        var html = Markdown.ToHtml(description, pipeline);
        
        var addin = new AddinInfo
        {
            Image = item.IconUrl?.AbsoluteUri ?? "",
            Name = item.Title,
            Package = item.Identity.Id,
            Author = string.Join(',', item.Authors),
            Description = description, 
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

    public override Task Install(string package, NuGetVersion? version = null) => Install(package, version, false);

    public override async Task Install(string package, NuGetVersion? version = null, bool queue = false)
    {
        var downloaded =
            new ConcurrentDictionary<PackageIdentity, byte>(
                PackageIdentityComparer.Default);

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

    private bool _disposedValue;

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                PackageCache.Dispose();
            }

            _disposedValue = true;
        }
    }

    private static async Task<SourceRepository?> FindRepositoryAsync(
        string packageId,
        NuGetVersion version,
        bool allowNuGetOrg,
        CancellationToken cancellationToken)
    {
        var repositories = allowNuGetOrg
            ? GetDependencyRepositories()
            : GetConfiguredRepositories();

        foreach (var repository in repositories)
        {
            var findPackageResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

            if (await findPackageResource.DoesPackageExistAsync(
                    packageId,
                    version,
                    PackageCache,
                    Logger,
                    cancellationToken))
            {
                return repository;
            }
        }

        return null;
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }

    #endregion
}
