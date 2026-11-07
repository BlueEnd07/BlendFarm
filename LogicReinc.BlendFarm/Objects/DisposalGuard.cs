using System;
using System.Collections.Generic;

namespace LogicReinc.BlendFarm.Objects
{
    /// <summary>
    /// Lets background work borrow an object (eg. an Avalonia bitmap the UI may replace and dispose at any moment):
    /// while leases are held, Dispose requests are postponed until the last lease is released.
    /// Disposing a bitmap that is still being encoded on another thread crashes native Skia code (uncatchable).
    /// </summary>
    public sealed class DisposalGuard
    {
        private sealed class State
        {
            public int Leases;
            public bool DisposeRequested;
        }

        private sealed class Lease : IDisposable
        {
            private DisposalGuard _guard;
            private readonly IDisposable _target;
            public Lease(DisposalGuard guard, IDisposable target) { _guard = guard; _target = target; }
            public void Dispose()
            {
                DisposalGuard guard = System.Threading.Interlocked.Exchange(ref _guard, null);
                guard?.Release(_target);
            }
        }

        private sealed class NoLease : IDisposable
        {
            public static readonly NoLease Instance = new NoLease();
            public void Dispose() { }
        }

        private readonly object _lock = new object();
        private readonly Dictionary<object, State> _states = new Dictionary<object, State>(ReferenceEqualityComparer.Instance);
        private readonly Action<Action> _runDeferred;

        /// <param name="runDeferred">Runs a postponed disposal (eg. posts it to the UI thread); null runs it on the releasing thread</param>
        public DisposalGuard(Action<Action> runDeferred = null)
        {
            _runDeferred = runDeferred;
        }

        /// <summary>Number of leases currently held on the target (for tests and diagnostics).</summary>
        public int LeaseCount(IDisposable target)
        {
            if (target == null) return 0;
            lock (_lock)
                return _states.TryGetValue(target, out State state) ? state.Leases : 0;
        }

        /// <summary>Keeps the target alive until the returned lease is disposed.</summary>
        public IDisposable Acquire(IDisposable target)
        {
            if (target == null)
                return NoLease.Instance;
            lock (_lock)
            {
                if (!_states.TryGetValue(target, out State state))
                    _states[target] = state = new State();
                state.Leases++;
            }
            return new Lease(this, target);
        }

        /// <summary>Disposes the target now, or as soon as its last lease is released. Returns true if it was disposed immediately.</summary>
        public bool Dispose(IDisposable target)
        {
            if (target == null) return true;
            lock (_lock)
            {
                if (_states.TryGetValue(target, out State state) && state.Leases > 0)
                {
                    state.DisposeRequested = true;
                    return false;
                }
            }
            SafeDispose(target);
            return true;
        }

        private void Release(IDisposable target)
        {
            bool dispose = false;
            lock (_lock)
            {
                if (!_states.TryGetValue(target, out State state))
                    return;
                if (--state.Leases > 0)
                    return;
                _states.Remove(target);
                dispose = state.DisposeRequested;
            }
            if (!dispose) return;
            if (_runDeferred != null)
                _runDeferred(() => SafeDispose(target));
            else
                SafeDispose(target);
        }

        private static void SafeDispose(IDisposable target)
        {
            try { target.Dispose(); }
            catch { }
        }
    }
}
