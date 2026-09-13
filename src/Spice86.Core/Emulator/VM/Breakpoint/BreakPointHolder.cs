namespace Spice86.Core.Emulator.VM.Breakpoint;

using System.Linq;
using System.Threading;

/// <summary>
/// Holds breakpoints and triggers them when certain conditions are met.
/// </summary>
public class BreakPointHolder {
    /// <summary>
    /// Guards every read and write of the collections below.
    /// </summary>
    /// <remarks>
    /// <b>The CPU thread walks these while UI and MCP threads mutate them.</b>
    /// <see cref="TriggerMatchingBreakPoints"/> runs on the emulation thread for every executed
    /// instruction once any breakpoint exists, while <see cref="ToggleBreakPoint"/> is called from
    /// the debugger UI and from MCP tool threads. Without this lock the list walk can index past an
    /// element another thread has just removed — observed 2026-09-13 as
    /// <c>ArgumentOutOfRangeException</c> in <c>TriggerBreakPointsFromList</c>, taking the whole
    /// emulator down mid-session, and the dictionary is equally exposed to a concurrent add.
    ///
    /// <para>The cost is one uncontended lock per checked address, and only while breakpoints are
    /// registered at all — <c>CfgCpu</c> tests <see cref="HasActiveBreakpoints"/> first, so a run
    /// with no breakpoints never reaches it.</para>
    ///
    /// <para><b>Callbacks run while it is held</b>, which is deliberate: a breakpoint action that
    /// toggles another breakpoint must see a consistent collection, and <see cref="Lock"/> is
    /// re-entrant for that reason. What makes that safe is that a breakpoint action never
    /// <i>waits</i> — every one of them ends at <c>PauseHandler.RequestPause</c>, which sets the
    /// request and returns; the emulation loop does the actual waiting in <c>WaitIfPaused</c>, long
    /// after this method has returned and released the lock. An action that blocked in here would
    /// hold the lock against the UI thread, so keep them non-blocking.</para>
    /// </remarks>
    private readonly Lock _gate = new();

    private readonly Dictionary<long, List<BreakPoint>> _addressBreakPoints = new(1000);
    private readonly List<BreakPoint> _unconditionalBreakPoints = new(1000);
    private readonly HashSet<BreakPoint> _registeredBreakPoints = [];
    private int _activeBreakpoints;

    /// <summary>
    /// Gets a value indicating whether this BreakPointHolder is empty.
    /// </summary>
    public bool IsEmpty {
        get {
            lock (_gate) {
                return _addressBreakPoints.Count == 0 && _unconditionalBreakPoints.Count == 0;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether at least one breakpoint is currently enabled.
    /// </summary>
    /// <remarks>
    /// Read without the lock, on the CPU's hot path: <c>CfgCpu</c> asks this before every
    /// instruction. The counter is maintained with <see cref="Interlocked"/> because a breakpoint's
    /// <c>IsEnabled</c> setter raises its change event on whatever thread flipped it — the debugger
    /// UI — which is outside <c>_gate</c> entirely.
    /// </remarks>
    public bool HasActiveBreakpoints => Volatile.Read(ref _activeBreakpoints) > 0;

    private IEnumerable<BreakPoint> GetAllBreakpoints() {
        // Materialised inside the lock: the caller enumerates later, by which time another thread
        // may have toggled something.
        lock (_gate) {
            return _addressBreakPoints.Values
                .SelectMany(list => list).Concat(_unconditionalBreakPoints).ToList();
        }
    }

    internal IEnumerable<AddressBreakPoint> SerializableBreakpoints => GetAllBreakpoints().Where
        (x => x.IsUserBreakpoint).OfType<AddressBreakPoint>();

    internal IEnumerable<UnconditionalBreakPoint> SerializableWildcardBreakpoints {
        get {
            lock (_gate) {
                return _unconditionalBreakPoints.Where(x => x.IsUserBreakpoint)
                    .OfType<UnconditionalBreakPoint>().ToList();
            }
        }
    }

    /// <summary>
    /// Toggles the specified breakpoint on or off.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to toggle.</param>
    /// <param name="on">True to enable the breakpoint; false to disable it.</param>
    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        switch (breakPoint) {
            case UnconditionalBreakPoint:
                ToggleUnconditionalBreakPoint(breakPoint, on);
                break;
            case AddressBreakPoint addressBreakPoint:
                ToggleAddressBreakPoint(addressBreakPoint, on);
                break;
        }
    }

    private void ToggleAddressBreakPoint(AddressBreakPoint breakPoint, bool on) {
        lock (_gate) {
            ToggleAddressBreakPointLocked(breakPoint, on);
        }
    }

    private void ToggleAddressBreakPointLocked(AddressBreakPoint breakPoint, bool on) {
        long address = breakPoint.Address;
        _addressBreakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList);
        if (on) {
            if (breakPointList == null) {
                _addressBreakPoints.Add(address, [breakPoint]);
                RegisterBreakPoint(breakPoint);
                return;
            }

            if (_registeredBreakPoints.Contains(breakPoint)) {
                return;
            }

            breakPointList.Add(breakPoint);
            RegisterBreakPoint(breakPoint);
        } else if (breakPointList != null && breakPointList.Remove(breakPoint)) {
            if (breakPointList.Count == 0) {
                _addressBreakPoints.Remove(address);
            }

            UnregisterBreakPoint(breakPoint);
        }
    }

    private void ToggleUnconditionalBreakPoint(BreakPoint breakPoint, bool on) {
        lock (_gate) {
            ToggleUnconditionalBreakPointLocked(breakPoint, on);
        }
    }

    private void ToggleUnconditionalBreakPointLocked(BreakPoint breakPoint, bool on) {
        if (on) {
            if (_registeredBreakPoints.Contains(breakPoint)) {
                return;
            }

            _unconditionalBreakPoints.Add(breakPoint);
            RegisterBreakPoint(breakPoint);
        } else if (_unconditionalBreakPoints.Remove(breakPoint)) {
            UnregisterBreakPoint(breakPoint);
        }
    }

    /// <summary>
    /// Triggers all breakpoints that match the specified address.
    /// </summary>
    /// <param name="address">The address to match.</param>
    /// <returns>true if trigged, false instead</returns>
    public bool TriggerMatchingBreakPoints(long address) {
        lock (_gate) {
            return TriggerMatchingBreakPointsLocked(address);
        }
    }

    private bool TriggerMatchingBreakPointsLocked(long address) {
        bool triggered = false;
        if (_addressBreakPoints.Count > 0) {
            if (_addressBreakPoints.TryGetValue(address, out List<BreakPoint>? breakPointList)) {
                triggered = TriggerBreakPointsFromList(breakPointList, address);
                if (breakPointList.Count == 0) {
                    _addressBreakPoints.Remove(address);
                }
            }
        }

        if (_unconditionalBreakPoints.Count > 0) {
            triggered |= TriggerBreakPointsFromList(_unconditionalBreakPoints, address);
        }

        return triggered;
    }

    private bool TriggerBreakPointsFromList(List<BreakPoint> breakPointList, long address) {
        bool triggered = false;
        for (int i = breakPointList.Count - 1; i >= 0; i--) {
            BreakPoint breakPoint = breakPointList[i];
            if (!breakPoint.Matches(address)) {
                continue;
            }
            if (breakPoint.IsRemovedOnTrigger) {
                breakPointList.RemoveAt(i);
                UnregisterBreakPoint(breakPoint);
            }
            // trigger it later because action might try to delete it
            breakPoint.Trigger();
            triggered = true;

        }

        return triggered;
    }

    private void RegisterBreakPoint(BreakPoint breakPoint) {
        if (!_registeredBreakPoints.Add(breakPoint)) {
            return;
        }

        breakPoint.IsEnabledChanged += OnBreakPointIsEnabledChanged;
        if (breakPoint.IsEnabled) {
            Interlocked.Increment(ref _activeBreakpoints);
        }
    }

    private void UnregisterBreakPoint(BreakPoint breakPoint) {
        if (!_registeredBreakPoints.Remove(breakPoint)) {
            return;
        }

        breakPoint.IsEnabledChanged -= OnBreakPointIsEnabledChanged;
        if (breakPoint.IsEnabled) {
            DecrementActiveBreakpoints();
        }
    }

    private void OnBreakPointIsEnabledChanged(BreakPoint breakPoint, bool isEnabled) {
        if (isEnabled) {
            Interlocked.Increment(ref _activeBreakpoints);
        } else {
            DecrementActiveBreakpoints();
        }
    }

    private void DecrementActiveBreakpoints() {
        // This should never happen, but as a safeguard, throw if the count becomes negative.
        if (Interlocked.Decrement(ref _activeBreakpoints) < 0) {
            throw new InvalidOperationException("Active breakpoints count cannot be negative.");
        }
    }
}
