// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Bootstrapper.FunctionalTests;
using Microsoft.Framework.CommonTestUtils;
using Microsoft.Framework.Runtime.Common.Impl;
using Microsoft.Framework.Runtime;
using NuGet;
using Xunit;

namespace Microsoft.Framework.ApplicationHost
{
    public class AppHostTests
    {
        public static IEnumerable<object[]> RuntimeComponents
        {
            get
            {
                return TestUtils.GetRuntimeComponentsCombinations();
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void AppHostReturnsNonZeroExitCodeWhenNoSubCommandWasGiven(string flavor, string os, string architecture)
        {
            using (var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture))
            {
                string stdOut, stdErr;
                var exitCode = BootstrapperTestUtils.ExecBootstrapper(
                    runtimeHomeDir,
                    arguments: ".",
                    stdOut: out stdOut,
                    stdErr: out stdErr);

                Assert.NotEqual(0, exitCode);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void AppHostReturnsZeroExitCodeWhenHelpOptionWasGiven(string flavor, string os, string architecture)
        {
            using (var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture))
            {
                string stdOut, stdErr;
                var exitCode = BootstrapperTestUtils.ExecBootstrapper(
                    runtimeHomeDir,
                    arguments: ". --help",
                    stdOut: out stdOut,
                    stdErr: out stdErr);

                Assert.Equal(0, exitCode);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void AppHostShowsVersionAndReturnsZeroExitCodeWhenVersionOptionWasGiven(string flavor, string os, string architecture)
        {
            using (var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture))
            {
                string stdOut, stdErr;
                var exitCode = BootstrapperTestUtils.ExecBootstrapper(
                    runtimeHomeDir,
                    arguments: ". --version",
                    stdOut: out stdOut,
                    stdErr: out stdErr);

                Assert.Equal(0, exitCode);
                Assert.Contains(TestUtils.GetRuntimeVersion(), stdOut);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void AppHostShowsErrorWhenNoProjectJsonWasFound(string flavor, string os, string architecture)
        {
            using (var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture))
            using (var emptyFolder = TestUtils.CreateTempDir())
            {
                string stdOut, stdErr;
                var exitCode = BootstrapperTestUtils.ExecBootstrapper(
                    runtimeHomeDir,
                    arguments: $"{emptyFolder} run",
                    stdOut: out stdOut,
                    stdErr: out stdErr);

                Assert.NotEqual(0, exitCode);
                Assert.Contains("Unable to resolve project", stdErr);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void AppHostShowsErrorWhenGivenSubcommandWasNotFoundInProjectJson(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);
            var projectStructure = @"{
  'project.json': '{ }'
}";

            using (runtimeHomeDir)
            using (var projectPath = TestUtils.CreateTempDir())
            {
                DirTree.CreateFromJson(projectStructure).WriteTo(projectPath);

                string stdOut, stdErr;
                var exitCode = BootstrapperTestUtils.ExecBootstrapper(
                    runtimeHomeDir,
                    arguments: $"{projectPath} invalid",
                    stdOut: out stdOut,
                    stdErr: out stdErr,
                    environment: new Dictionary<string, string> { { EnvironmentNames.AppBase, projectPath } });

                Assert.NotEqual(0, exitCode);
                Assert.Contains("Unable to load application or execute command 'invalid'.", stdErr);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void AppHostShowsErrorWhenCurrentTargetFrameworkWasNotFoundInProjectJson(string flavor, string os, string architecture)
        {
            var runtimeTargetFrameworkString = flavor == "coreclr" ? FrameworkNames.LongNames.DnxCore50 : FrameworkNames.LongNames.Dnx451;
            var runtimeTargetFramework = new FrameworkName(runtimeTargetFrameworkString);
            var runtimeTargetFrameworkShortName = VersionUtility.GetShortFrameworkName(runtimeTargetFramework);
            var runtimeType = flavor == "coreclr" ? "CoreCLR" : "CLR";
            var alternativeRuntimeType = flavor == "coreclr" ? "CLR" : "CoreCLR";
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);
            var projectJsonContents = @"{
  'frameworks': {
    'FRAMEWORK_NAME': { }
  }
}".Replace("FRAMEWORK_NAME", flavor == "coreclr" ? "dnx451" : "dnxcore50");

            using (runtimeHomeDir)
            using (var projectPath = new DisposableDir())
            {
                var projectName = new DirectoryInfo(projectPath).Name;
                var projectJsonPath = Path.Combine(projectPath, Project.ProjectFileName);
                File.WriteAllText(projectJsonPath, projectJsonContents);

                string stdOut, stdErr;
                var exitCode = BootstrapperTestUtils.ExecBootstrapper(
                    runtimeHomeDir,
                    arguments: $"{projectPath} run",
                    stdOut: out stdOut,
                    stdErr: out stdErr);

                var expectedErrorMsg = string.Join(Environment.NewLine, new[]{
                    $"'{projectName}' doesn't support current runtime target framework." + Environment.NewLine,
                    $"Runtime Target Framework: '{runtimeTargetFramework} ({runtimeTargetFrameworkShortName})'",
                    $"Runtime Type: {runtimeType}",
                    $"Runtime Architecture: {architecture}",
                    $"Runtime Version: {TestUtils.GetRuntimeVersion()}" + Environment.NewLine,
                    $"Please make sure the runtime matches a framework specified in {Project.ProjectFileName}" });

                Assert.NotEqual(0, exitCode);
                Assert.Contains(expectedErrorMsg, stdErr);
            }
        }
    }
}
