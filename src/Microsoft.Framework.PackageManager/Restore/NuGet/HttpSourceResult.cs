// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;

namespace Microsoft.Framework.PackageManager.Restore.NuGet
{
    internal class HttpSourceResult : IDisposable
    {
        public string CacheFileName { get; set; }
        public Stream Stream { get; set; }

        public void Dispose()
        {
            if (Stream != null)
            {
                Stream.Dispose();
                Stream = null;
            }
        }
    }
}