// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See THIRD-PARTY-NOTICES.md in the repository root.

using System;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using WindowPalette.Windows;

namespace WindowPalette.CmdPal;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        // The resident worker has its own process and does not share COM disposal.
        if (args.Length == 1 && args[0] == "--background")
        {
            Environment.ExitCode = BackgroundProcess.Run();
            return;
        }

        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            return;
        }

        ComServer server = new();
        using ManualResetEvent extensionDisposedEvent = new(false);

        // Reuse one extension instance until Command Palette releases it.
        Extension extensionInstance = new(extensionDisposedEvent);
        server.RegisterClass<Extension, IExtension>(() => extensionInstance);
        server.Start();

        try
        {
            extensionDisposedEvent.WaitOne();
        }
        finally
        {
            server.Stop();
            server.UnsafeDispose();
        }
    }
}
