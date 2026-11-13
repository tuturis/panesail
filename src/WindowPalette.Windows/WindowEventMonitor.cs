using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowPalette.Core;
using WindowPalette.Windows.Native;

namespace WindowPalette.Windows;

/// <summary>Observes ordinary title-bar drags and applies one cross-monitor resize after release.</summary>
public sealed class WindowEventMonitor : IDisposable
{
    private readonly Action<string> _reportActivity;
    private readonly BlockingCollection<Observation> _observations = new(new ConcurrentQueue<Observation>());
    private readonly CancellationTokenSource _stop = new();
    // These delegates must stay rooted for the complete native registration lifetime.
    private readonly NativeMethods.HookProc _mouseCallback;
    private readonly NativeMethods.HookProc _keyboardCallback;
    private readonly NativeMethods.WinEventProc _eventCallback;
    private readonly NativeMethods.WindowProc _windowCallback;
    private readonly List<Observation> _recentMoveEvents = [];
    private PaletteSettings _settings;
    private nint _window;
    private int _started, _disposed, _running, _shiftBits;
    private bool _primaryRight;
    private Session? _session;
    private Verification? _verification;
    private Exception? _engineError;

    public WindowEventMonitor(PaletteSettings settings, Action<string> reportActivity)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _reportActivity = reportActivity ?? throw new ArgumentNullException(nameof(reportActivity));
        _mouseCallback = MouseHook;
        _keyboardCallback = KeyboardHook;
        _eventCallback = WinEvent;
        _windowCallback = WindowProcedure;
    }

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public void UpdateSettings(PaletteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Volatile.Write(ref _settings, settings);
        Enqueue(new(ObservationKind.Settings, 0, default, Tick));
        if (!settings.Enabled)
        {
            RequestStop();
        }
    }

    /// <summary>Runs the hook message loop on the caller's dedicated thread until cancellation.</summary>
    public void Run(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The window monitor can only be started once.");
        }

        if (!_settings.Enabled || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var previousDpi = NativeMethods.SetThreadDpiAwarenessContext(-4);
        var module = NativeMethods.GetModuleHandleW(null);
        var className = "WindowPalette.Monitor." + Guid.NewGuid().ToString("N");
        var windowClass = new NativeMethods.WindowClass { Procedure = _windowCallback, Instance = module, ClassName = className };
        nint mouse = 0, keyboard = 0, movement = 0, arrangement = 0;
        var classRegistered = false;
        Task? engine = null;
        try
        {
            if (NativeMethods.RegisterClassW(ref windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The window monitor could not initialize.");
            }

            classRegistered = true;
            // An invisible top-level window receives display-setting broadcasts; a message-only window does not.
            _window = NativeMethods.CreateWindowExW(0x80, className, "PaneSail", 0, 0, 0, 0, 0, 0, 0, module, 0);
            if (_window == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The window monitor could not create its message window.");
            }

            _primaryRight = NativeMethods.GetSystemMetrics(23) != 0;
            _shiftBits = ((NativeMethods.GetAsyncKeyState(NativeMethods.VkLeftShift) & 0x8000) != 0 ? 1 : 0)
                | ((NativeMethods.GetAsyncKeyState(NativeMethods.VkRightShift) & 0x8000) != 0 ? 2 : 0);
            mouse = NativeMethods.SetWindowsHookExW(NativeMethods.WhMouseLl, _mouseCallback, module, 0);
            keyboard = NativeMethods.SetWindowsHookExW(NativeMethods.WhKeyboardLl, _keyboardCallback, module, 0);
            movement = NativeMethods.SetWinEventHook(NativeMethods.MoveStart, NativeMethods.MoveEnd, 0, _eventCallback, 0, 0, 0x0002);
            arrangement = NativeMethods.SetWinEventHook(NativeMethods.ArrangementPreview, NativeMethods.ArrangementPreview, 0, _eventCallback, 0, 0, 0x0002);
            if (mouse == 0 || keyboard == 0 || movement == 0 || arrangement == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not allow the drag monitor to start.");
            }

            engine = Task.Factory.StartNew(() => ProcessObservations(lifetime.Token), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            using var cancellation = lifetime.Token.Register(() => NativeMethods.PostMessageW(_window, NativeMethods.WmClose, 0, 0));
            Volatile.Write(ref _running, 1);
            Report("Automatic resizing is ready.");
            while (!lifetime.IsCancellationRequested)
            {
                var result = NativeMethods.GetMessageW(out var message, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result < 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The window monitor's message loop stopped unexpectedly.");
                }

                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessageW(in message);
            }
        }
        finally
        {
            Volatile.Write(ref _running, 0);
            if (mouse != 0) NativeMethods.UnhookWindowsHookEx(mouse);
            if (keyboard != 0) NativeMethods.UnhookWindowsHookEx(keyboard);
            if (movement != 0) NativeMethods.UnhookWinEvent(movement);
            if (arrangement != 0) NativeMethods.UnhookWinEvent(arrangement);
            lifetime.Cancel();
            _observations.CompleteAdding();
            if (engine is not null)
            {
                // Native cross-process queries use bounded timeouts; no window operation is retried.
                engine.GetAwaiter().GetResult();
            }

            if (_window != 0) NativeMethods.DestroyWindow(_window);
            _window = 0;
            if (classRegistered) NativeMethods.UnregisterClassW(className, module);
            if (previousDpi != 0) NativeMethods.SetThreadDpiAwarenessContext(previousDpi);
            GC.KeepAlive(_mouseCallback);
            GC.KeepAlive(_keyboardCallback);
            GC.KeepAlive(_eventCallback);
            GC.KeepAlive(_windowCallback);
        }

        if (_engineError is not null)
        {
            throw new InvalidOperationException("The window monitor stopped after an unexpected error.", _engineError);
        }
    }

    private nint MouseHook(int code, nuint message, nint data)
    {
        if (code >= 0)
        {
            var down = _primaryRight ? NativeMethods.WmRightDown : NativeMethods.WmLeftDown;
            var up = _primaryRight ? NativeMethods.WmRightUp : NativeMethods.WmLeftUp;
            if ((uint)message == down || (uint)message == up)
            {
                if ((uint)message == down)
                {
                    // Key-up can occur on another desktop (for example while locked).
                    // Re-sample at the gesture boundary, then latch transitions during the drag.
                    _shiftBits = ((NativeMethods.GetAsyncKeyState(NativeMethods.VkLeftShift) & 0x8000) != 0 ? 1 : 0)
                        | ((NativeMethods.GetAsyncKeyState(NativeMethods.VkRightShift) & 0x8000) != 0 ? 2 : 0);
                }
                var mouse = Marshal.PtrToStructure<NativeMethods.MouseData>(data);
                Enqueue(new((uint)message == down ? ObservationKind.MouseDown : ObservationKind.MouseUp,
                    0, new(mouse.Point.X, mouse.Point.Y), mouse.Time, _shiftBits != 0));
            }
        }

        return NativeMethods.CallNextHookEx(0, code, message, data);
    }

    private nint KeyboardHook(int code, nuint message, nint data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<NativeMethods.KeyboardData>(data);
            var down = (uint)message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            var shiftBit = key.VirtualKey switch { NativeMethods.VkLeftShift => 1, NativeMethods.VkRightShift => 2, NativeMethods.VkShift => 4, _ => 0 };
            if (shiftBit != 0)
            {
                _shiftBits = down ? _shiftBits | shiftBit : _shiftBits & ~shiftBit;
                if (down) Enqueue(new(ObservationKind.Shift, 0, default, key.Time, true));
            }
            else if (key.VirtualKey == NativeMethods.VkEscape && down)
            {
                Enqueue(new(ObservationKind.Escape, 0, default, key.Time));
            }
        }

        return NativeMethods.CallNextHookEx(0, code, message, data);
    }

    private void WinEvent(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        var kind = eventId switch
        {
            NativeMethods.MoveStart => ObservationKind.MoveStart,
            NativeMethods.MoveEnd => ObservationKind.MoveEnd,
            _ => ObservationKind.SnapPreview,
        };
        Enqueue(new(kind, hwnd, default, time));
    }

    private nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (message == NativeMethods.WmClose)
        {
            NativeMethods.PostQuitMessage(0);
            return 0;
        }

        if (message is NativeMethods.WmDisplayChange or NativeMethods.WmSettingChange)
        {
            _primaryRight = NativeMethods.GetSystemMetrics(23) != 0;
            Enqueue(new(ObservationKind.DisplayChanged, 0, default, Tick));
        }

        return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void Enqueue(Observation observation)
    {
        try
        {
            if (!_observations.IsAddingCompleted) _observations.Add(observation);
        }
        catch (InvalidOperationException)
        {
            // Shutdown can race with the last callback. Never throw across a native callback boundary.
        }
    }

    private void ProcessObservations(CancellationToken cancellationToken)
    {
        var previousDpi = NativeMethods.SetThreadDpiAwarenessContext(-4);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Block indefinitely while idle; only active gestures and readback use timed wakeups.
                var wait = _session is null && _verification is null ? Timeout.Infinite : 25;
                if (_observations.TryTake(out var observation, wait, cancellationToken))
                {
                    Handle(observation);
                }

                cancellationToken.ThrowIfCancellationRequested();
                Advance(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _engineError = exception;
            Trace.TraceError("Window drag monitor: {0}", exception);
            Report("Automatic resizing stopped. Restart it from PaneSail.");
            NativeMethods.PostMessageW(_window, NativeMethods.WmClose, 0, 0);
        }
        finally
        {
            _session = null;
            _verification = null;
            if (previousDpi != 0) NativeMethods.SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    private void Handle(Observation observation)
    {
        if (observation.Kind == ObservationKind.MouseDown)
        {
            Begin(observation);
            return;
        }

        if (observation.Kind is ObservationKind.MoveStart or ObservationKind.MoveEnd)
        {
            _recentMoveEvents.Add(observation);
            if (_recentMoveEvents.Count > 16) _recentMoveEvents.RemoveAt(0);
        }

        var session = _session;
        if (session is null) return;
        if (observation.Kind == ObservationKind.Settings)
        {
            if (!Volatile.Read(ref _settings).Enabled) _session = null;
            return;
        }

        if (Elapsed(observation.Time, session.DownTime) < 0) return;
        switch (observation.Kind)
        {
            case ObservationKind.MouseUp:
                session.Released = true;
                session.Release = observation.Point;
                session.ReleaseTime = observation.Time;
                session.ShiftUsed |= observation.Shift;
                break;
            case ObservationKind.MoveStart when observation.Window == session.Identity.Handle:
                session.MoveStarted = true;
                session.MoveStartTime = observation.Time;
                break;
            case ObservationKind.MoveEnd when observation.Window == session.Identity.Handle:
                session.MoveEnded = true;
                session.MoveEndTime = observation.Time;
                break;
            case ObservationKind.Shift:
                session.ShiftUsed = true;
                break;
            case ObservationKind.Escape:
                session.Cancelled = true;
                break;
            case ObservationKind.SnapPreview:
                session.SnapObserved = true;
                break;
            case ObservationKind.DisplayChanged:
                session.DisplayChanged = true;
                break;
        }

        PrepareCompletion(session);
    }

    private void Begin(Observation observation)
    {
        _session = null;
        _verification = null;
        var settings = Volatile.Read(ref _settings);
        // A delayed mouse-down cannot establish pre-transition geometry safely.
        if (!settings.Enabled || Elapsed(Tick, observation.Time) > 150) return;
        var window = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(WindowSnapshot.ToNative(observation.Point)), 2);
        if (!WindowSnapshot.TryIdentity(window, out var identity) || !WindowSnapshot.IsNormalResizable(identity)
            || !WindowSnapshot.TryVisibleBounds(window, out var visible) || !visible.Contains(observation.Point)
            || !WindowSnapshot.TryMonitor(NativeMethods.MonitorFromWindow(window, 0), out var monitor)
            || WindowSnapshot.IsFullscreen(visible, monitor) || !WindowSnapshot.IsCaption(window, observation.Point)) return;

        // Cross-process hit-testing can dispatch messages. Verify that the source rectangle stayed unchanged.
        if (!identity.StillExists || !WindowSnapshot.TryVisibleBounds(window, out var afterHitTest) || afterHitTest != visible
            || Elapsed(Tick, observation.Time) > 150) return;

        if (!WindowSnapshot.TryTopology(out var topology)) return;
        var session = new Session(identity, visible, monitor, topology, observation.Point, observation.Time, settings.Mode)
        {
            ShiftUsed = observation.Shift,
        };
        _session = session;
        // WinEvent delivery is asynchronous. Keep a small timestamp-filtered history for reordered callbacks.
        foreach (var prior in _recentMoveEvents)
        {
            if (prior.Window != window || Elapsed(prior.Time, observation.Time) < 0) continue;
            if (prior.Kind == ObservationKind.MoveStart)
            {
                session.MoveStarted = true;
                session.MoveStartTime = prior.Time;
            }

            if (prior.Kind == ObservationKind.MoveEnd)
            {
                session.MoveEnded = true;
                session.MoveEndTime = prior.Time;
            }
        }
    }

    private void PrepareCompletion(Session session)
    {
        if (session.CommitAt is not null || !session.MoveStarted || !session.MoveEnded || !session.Released) return;
        // Delivery may be reordered, but the event timestamps must still describe a move
        // that began while the button was held. A later keyboard move is not this gesture.
        if (Elapsed(session.MoveStartTime, session.ReleaseTime) > 0 || Elapsed(session.MoveEndTime, session.MoveStartTime) < 0)
        {
            _session = null;
            return;
        }

        if (!WindowSnapshot.TryVisibleBounds(session.Identity.Handle, out var bounds))
        {
            _session = null;
            return;
        }

        session.ReleaseBounds = bounds;
        session.CommitAt = Environment.TickCount64 + 90;
    }

    private void Advance(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_session is { } session)
        {
            if (!Volatile.Read(ref _settings).Enabled)
            {
                _session = null;
            }
            else if (session.CommitAt is { } due && Environment.TickCount64 >= due)
            {
                _session = null; // Consume once before any operation that may emit further events.
                Commit(session, cancellationToken);
            }
            else if ((!session.MoveStarted && Elapsed(Tick, session.DownTime) > 5000)
                || (session.Released && Elapsed(Tick, session.ReleaseTime) > 700))
            {
                if (session.MoveStarted) Report("Left this window unchanged because the drag could not be confirmed.");
                _session = null;
            }
        }

        if (_verification is { } verification && Environment.TickCount64 >= verification.Due)
        {
            _verification = null;
            if (!verification.Identity.StillExists || !WindowSnapshot.TryVisibleBounds(verification.Identity.Handle, out var actual))
            {
                Report("The window closed before its new size could be confirmed.");
                return;
            }

            if (Math.Abs(actual.Width - verification.Requested.Width) <= 2 && Math.Abs(actual.Height - verification.Requested.Height) <= 2)
            {
                Report(verification.Mode == ResizeMode.MatchScreenProportions
                    ? "Resized the window to match the screen proportions."
                    : "Fitted the window while keeping its shape.");
            }
            else
            {
                Report("The application adjusted or declined the requested size. Its result was left unchanged.");
            }
        }
    }

    private void Commit(Session session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = session.Identity.Handle;
        var destinationFound = WindowSnapshot.TryMonitor(NativeMethods.MonitorFromPoint(WindowSnapshot.ToNative(session.Release), 0), out var destination);
        var identityValid = session.Identity.StillExists;
        var visibleFound = WindowSnapshot.TryVisibleBounds(window, out var current);
        var supported = identityValid && WindowSnapshot.IsNormalResizable(session.Identity) && destinationFound
            && visibleFound && !WindowSnapshot.IsFullscreen(current, destination);
        var evidence = new GestureEvidence
        {
            CaptionHit = true,
            StartedNormalResizable = supported,
            MoveStarted = session.MoveStarted,
            MoveEnded = session.MoveEnded,
            MouseReleased = session.Released,
            Cancelled = session.Cancelled,
            ShiftUsed = session.ShiftUsed,
            SnapObserved = session.SnapObserved,
            IdentityUnchanged = identityValid,
            TopologyUnchanged = !session.DisplayChanged && WindowSnapshot.TryTopology(out var topology) && session.Topology == topology,
            SameMonitor = destinationFound && destination.Handle == session.SourceMonitor.Handle,
            DestinationSafeInterior = destinationFound && WindowSnapshot.SafeDrop(session.Release, destination, window),
            PlacementStable = visibleFound && current == session.ReleaseBounds,
        };
        var decision = GesturePolicy.Evaluate(evidence);
        if (decision != GestureDecision.Apply)
        {
            Trace.WriteLine($"PaneSail drag skipped: {decision}");
            Report(decision switch
            {
                GestureDecision.ShiftBypass => "Left the Shift-drag gesture to your window manager.",
                GestureDecision.SnapObserved or GestureDecision.UnsafeDestination => "Left the snap or screen-edge gesture unchanged.",
                GestureDecision.Cancelled => "The drag was cancelled.",
                GestureDecision.SameMonitor => "The window stayed on the same screen.",
                _ => "Left this window unchanged because its final placement was uncertain.",
            });
            return;
        }

        var request = new ResizeRequest(session.SourceBounds, session.SourceMonitor.WorkArea, destination.WorkArea,
            session.Release, (session.Down.X - session.SourceBounds.Left) / (double)session.SourceBounds.Width,
            Math.Max(0, session.Release.Y - current.Top));
        if (!Volatile.Read(ref _settings).Enabled || !session.Identity.StillExists) return;
        cancellationToken.ThrowIfCancellationRequested();
        var result = WindowResizeOperation.Apply(current, request, session.Mode, desiredBounds =>
        {
            if (!Volatile.Read(ref _settings).Enabled || !session.Identity.StillExists) return false;
            cancellationToken.ThrowIfCancellationRequested();
            return WindowSnapshot.TryApplyVisibleBounds(window, desiredBounds);
        }, out var desired);
        if (result == ResizeOperationResult.AlreadyFits)
        {
            Report("Left the window unchanged because it fits on this screen.");
            return;
        }
        if (result == ResizeOperationResult.Rejected)
        {
            Report("The application did not accept the resize request.");
            return;
        }

        _verification = new(session.Identity, desired, session.Mode, Environment.TickCount64 + 150);
    }

    private void Report(string message)
    {
        try { _reportActivity(message); }
        catch (Exception exception) { Trace.TraceError("Window monitor status callback: {0}", exception); }
    }

    private void RequestStop()
    {
        try { _stop.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) RequestStop();
    }

    private static uint Tick => unchecked((uint)Environment.TickCount64);
    private static int Elapsed(uint later, uint earlier) => unchecked((int)(later - earlier));
    private enum ObservationKind { MouseDown, MouseUp, MoveStart, MoveEnd, Shift, Escape, SnapPreview, DisplayChanged, Settings }
    private readonly record struct Observation(ObservationKind Kind, nint Window, PixelPoint Point, uint Time, bool Shift = false);
    private sealed record Verification(WindowIdentity Identity, PixelRect Requested, ResizeMode Mode, long Due);

    private sealed class Session(WindowIdentity identity, PixelRect sourceBounds, MonitorSnapshot sourceMonitor,
        string topology, PixelPoint down, uint downTime, ResizeMode mode)
    {
        internal WindowIdentity Identity { get; } = identity;
        internal PixelRect SourceBounds { get; } = sourceBounds;
        internal MonitorSnapshot SourceMonitor { get; } = sourceMonitor;
        internal string Topology { get; } = topology;
        internal PixelPoint Down { get; } = down;
        internal uint DownTime { get; } = downTime;
        internal ResizeMode Mode { get; } = mode;
        internal bool MoveStarted, MoveEnded, Released, Cancelled, ShiftUsed, SnapObserved, DisplayChanged;
        internal PixelPoint Release;
        internal uint ReleaseTime, MoveStartTime, MoveEndTime;
        internal PixelRect ReleaseBounds;
        internal long? CommitAt;
    }
}
