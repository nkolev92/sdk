﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Packaging.Core;

namespace Microsoft.NET.Build.Tasks
{
    internal class PublishAssembliesResolver
    {
        private  HashSet<PackageIdentity> _allResolvedPackages = new HashSet<PackageIdentity>();
        private readonly IPackageResolver _packageResolver;
        private IEnumerable<string> _excludeFromPublishPackageIds;
        private bool _preserveStoreLayout;

        public PublishAssembliesResolver(IPackageResolver packageResolver)
        {
            _packageResolver = packageResolver;
        }

        public PublishAssembliesResolver WithExcludeFromPublish(IEnumerable<string> excludeFromPublishPackageIds)
        {
            _excludeFromPublishPackageIds = excludeFromPublishPackageIds;
            return this;
        }
        public PublishAssembliesResolver WithPreserveStoreLayout(bool preserveStoreLayout)
        {
            _preserveStoreLayout = preserveStoreLayout;
            return this;
        }
        public IEnumerable<ResolvedFile> Resolve(ProjectContext projectContext)
        {
            List<ResolvedFile> results = new List<ResolvedFile>();

            foreach (LockFileTargetLibrary targetLibrary in projectContext.GetRuntimeLibraries(_excludeFromPublishPackageIds))
            {
                if (targetLibrary.Type != "package")
                {
                    continue;
                }

                _allResolvedPackages.Add(new PackageIdentity(targetLibrary.Name, targetLibrary.Version));

                string pkgRoot;
                string libraryPath = _packageResolver.GetPackageDirectory(targetLibrary.Name, targetLibrary.Version, out pkgRoot);

                results.AddRange(GetResolvedFiles(targetLibrary.RuntimeAssemblies, libraryPath, pkgRoot, AssetType.Runtime));
                results.AddRange(GetResolvedFiles(targetLibrary.NativeLibraries, libraryPath, pkgRoot, AssetType.Native));

                foreach (LockFileRuntimeTarget runtimeTarget in targetLibrary.RuntimeTargets.FilterPlaceHolderFiles())
                {
                    if (string.Equals(runtimeTarget.AssetType, "native", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(runtimeTarget.AssetType, "runtime", StringComparison.OrdinalIgnoreCase))
                    {
                        string sourcePath = Path.Combine(libraryPath, runtimeTarget.Path);
                        AssetType _assetType = AssetType.None;
                        Enum.TryParse<AssetType>(runtimeTarget.AssetType, true, out _assetType);

                        results.Add(
                            new ResolvedFile(
                                sourcePath: sourcePath,
                                destinationSubDirectory: GetDestinationSubDirectory(sourcePath,
                                                                                    pkgRoot,
                                                                                    GetRuntimeTargetDestinationSubDirectory(runtimeTarget)),
                                assetType: _assetType));
                    }
                }

                foreach (LockFileItem resourceAssembly in targetLibrary.ResourceAssemblies.FilterPlaceHolderFiles())
                {
                    string locale;
                    string sourcePath = Path.Combine(libraryPath, resourceAssembly.Path);
                    if (!resourceAssembly.Properties.TryGetValue("locale", out locale))
                    {
                        locale = null;
                    }

                    locale = GetDestinationSubDirectory(sourcePath, pkgRoot, locale);

                    results.Add(
                        new ResolvedFile(
                            sourcePath: sourcePath,
                            destinationSubDirectory: locale,
                            assetType: AssetType.Resources));
                }
            }

            return results;
        }

        public IEnumerable<PackageIdentity> GetResolvedPackages()
        {
            return _allResolvedPackages;
        }

        private IEnumerable<ResolvedFile> GetResolvedFiles(IEnumerable<LockFileItem> items, string libraryPath, string pkgRoot, AssetType assetType)
        {
            foreach (LockFileItem item in items.FilterPlaceHolderFiles())
            {
                string srcpath = Path.Combine(libraryPath, item.Path);

                yield return new ResolvedFile(
                    sourcePath: srcpath,
                    destinationSubDirectory: GetDestinationSubDirectory(srcpath, pkgRoot),
                    assetType: assetType);
            }
        }

        private static string GetRuntimeTargetDestinationSubDirectory(LockFileRuntimeTarget runtimeTarget)
        {
            if (!string.IsNullOrEmpty(runtimeTarget.Runtime))
            {
                return Path.GetDirectoryName(runtimeTarget.Path);
            }

            return null;
        }

        private string GetDestinationSubDirectory(string libraryPath, string pkgRoot, string destpath = null)
        {
            if (_preserveStoreLayout && pkgRoot != null)
            {
                if (! libraryPath.StartsWith(pkgRoot))
                {
                    throw new BuildErrorException(Strings.IncorrectPackageRoot, pkgRoot, libraryPath);
                }

                destpath = Path.GetDirectoryName(libraryPath.Substring(pkgRoot.Length));
            }
            return destpath;
        }
    }
}
