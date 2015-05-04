// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Framework.Runtime.Internal;
using NuGet;

namespace Microsoft.Framework.Runtime.Servicing
{
    public class Breadcrumbs
    {
        // It is recommended that you disable breadcrumbs only for perf runs
        private const string NoBreadcrumbsEnvironmentVariableName = "DNX_NO_BREADCRUMBS";

        private static readonly string _logType = typeof(Breadcrumbs).Name;

        private readonly bool _isEnabled;
        private readonly string _breadcrumbsFolder;
        private readonly HashSet<BreadcrumbInfo> _breadcrumbsToWrite = new HashSet<BreadcrumbInfo>();
        private readonly object _addLock = new object();

        private bool _writeWasCalled;

        public static Breadcrumbs Instance { get; private set; } = new Breadcrumbs();

        public Breadcrumbs()
            : this(ResolveBreadcrumbsFolder())
        {
        }

        public Breadcrumbs(string breadcrumbsFolder)
        {
            if (Environment.GetEnvironmentVariable(NoBreadcrumbsEnvironmentVariableName) == "1")
            {
                _isEnabled = false;
                Logger.TraceWarning(
                    "[{0}] Breadcrumbs writing disabled because the environment variable {1} == 1.",
                    _logType,
                    NoBreadcrumbsEnvironmentVariableName);
                return;
            }

            // If the directory doesn't exist, don't create it because it
            // needs special permissions on it
            if (Directory.Exists(breadcrumbsFolder))
            {
                _isEnabled = true;
                _breadcrumbsFolder = breadcrumbsFolder;
            }
            else
            {
                _isEnabled = false;
                Logger.TraceInformation(
                    "[{0}] Breadcrumbs for servicing will not be written because the breadcrumbs folder ({1}) does not exist.",
                    _logType,
                    breadcrumbsFolder);
            }
        }

        public bool IsPackageServiceable(PackageInfo package)
        {
            if (!_isEnabled)
            {
                return false;
            }

            return package.LockFileLibrary.IsServiceable;
        }

        public void AddBreadcrumb(string packageId, SemanticVersion packageVersion)
        {
            if (!_isEnabled)
            {
                return;
            }

            var breadcrumb = new BreadcrumbInfo()
            {
                PackageId = packageId,
                PackageVersion = packageVersion.ToString()
            };

            lock (_addLock)
            {
                if (_writeWasCalled)
                {
                    if (_breadcrumbsToWrite.Contains(breadcrumb))
                    {
                        // This breadcrumb was already here, no need to do anything
                        return;
                    }

                    // If we get here, this is a new breadcrumb
                    throw new InvalidOperationException("New breadcrumbs cannot be added after writing has started.");
                }

                _breadcrumbsToWrite.Add(breadcrumb);
            }
        }

        public void WriteAllBreadcrumbs(bool background = false)
        {
            if (!_isEnabled)
            {
                return;
            }

            // The lock ensures that no add is happening while or after we set the flag
            lock (_addLock)
            {
                _writeWasCalled = true;
            }

            if (background)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    WriteAllBreadcrumbsInternal();
                });
            }
            else
            {
                WriteAllBreadcrumbsInternal();
            }
        }

        private void WriteAllBreadcrumbsInternal()
        {
            foreach (var breadcrumb in _breadcrumbsToWrite)
            {
                CreateBreadcrumb(breadcrumb.PackageId, breadcrumb.PackageVersion);
            }
        }

        /// <summary>
        /// Writes a breadcrumb on the disk 
        /// </summary>
        /// <param name="packageId">The ID ofthe package</param>
        /// <param name="packageVersion">The version of the the package</param>
        private void CreateBreadcrumb(string packageId, string packageVersion)
        {
            // Create both files for now until we get clear instructions about the format of the name
            CreateBreadcrumbFile(packageId);
            CreateBreadcrumbFile(packageId + "." + packageVersion);
        }

        private static string ResolveBreadcrumbsFolder()
        {
            var programDataFolder = Environment.GetEnvironmentVariable("ProgramData");
            if (string.IsNullOrWhiteSpace(programDataFolder))
            {
                return null;
            }

            string breadcrumbsFolder = Path.Combine(
                programDataFolder,
                "Microsoft DNX",
                "BreadcrumbStore");

            return breadcrumbsFolder;
        }

        private void CreateBreadcrumbFile(string fileName)
        {
            string fullFilePath = Path.Combine(_breadcrumbsFolder, fileName);

            // Execute with file locked because multiple processes can run at the same time
            ConcurrencyUtilities.ExecuteWithFileLocked(fullFilePath, _ =>
            {
                try
                {
                    if (!File.Exists(fullFilePath))
                    {

                        File.Create(fullFilePath).Dispose();

                        Logger.TraceInformation(
                            "[{0}] Wrote servicing breadcrumb for {1}",
                            _logType,
                            fileName);
                    }
                }
                catch (UnauthorizedAccessException exception)
                {
                    LogBreadcrumbsCreationFailure(fileName, exception);
                }
                catch (DirectoryNotFoundException exception)
                {
                    LogBreadcrumbsCreationFailure(fileName, exception);
                }

                return Task.FromResult(1);
            }).GetAwaiter().GetResult();
        }

        private static void LogBreadcrumbsCreationFailure(string fileName, Exception exception)
        {
            Logger.TraceError(
                "[{0}] Failed to write servicing breadcrumb for {1} because an exception was thrown: {2}",
                _logType,
                fileName,
                exception);
        }

        private class BreadcrumbInfo
        {
            public string PackageId { get; set; }
            public string PackageVersion { get; set; }

            public override int GetHashCode()
            {
                return PackageId.GetHashCode() ^ PackageVersion.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                BreadcrumbInfo other = obj as BreadcrumbInfo;
                if (other != null)
                {
                    return PackageId.Equals(other.PackageId, StringComparison.Ordinal) &&
                        PackageVersion.Equals(other.PackageVersion, StringComparison.Ordinal);
                }

                return base.Equals(obj);
            }
        }
    }
}
