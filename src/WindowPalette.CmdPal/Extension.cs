// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See THIRD-PARTY-NOTICES.md in the repository root.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace WindowPalette.CmdPal;

#if PANESAIL_STORE
[Guid("C56BE4C0-5ADB-47CB-A9D6-859ED1AD66D1")]
#else
[Guid("C08C7DB7-EE97-419D-A839-677DD2CAFFB6")]
#endif
public sealed partial class Extension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly CommandsProvider _provider = new();

    public Extension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    public void Dispose() => _extensionDisposedEvent.Set();
}
