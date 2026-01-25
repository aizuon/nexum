using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Nexum.Core
{
    internal sealed class MtuDiscovery
    {
        private int _confirmedMtu = MtuConfig.DefaultMtu;
        private bool _discoveryComplete;
        private int _failureCount;

        private int _highBound = MtuConfig.MaxMtu;
        private double _lastProbeReceivedTime;

        private double _lastProbeSentTime;

        private int _lowBound = MtuConfig.MinMtu;
        private bool _probeInFlight;
        private int _probingMtu;
        private SpinLock _spinLock = new SpinLock(false);

        private int _successCount;

        internal int ConfirmedMtu
        {
            get
            {
                bool lockTaken = false;
                try
                {
                    _spinLock.Enter(ref lockTaken);
                    return _confirmedMtu;
                }
                finally
                {
                    if (lockTaken)
                        _spinLock.Exit(false);
                }
            }
        }

        internal bool IsDiscoveryComplete
        {
            get
            {
                bool lockTaken = false;
                try
                {
                    _spinLock.Enter(ref lockTaken);
                    return _discoveryComplete;
                }
                finally
                {
                    if (lockTaken)
                        _spinLock.Exit(false);
                }
            }
        }

        internal int ProbingMtu
        {
            get
            {
                bool lockTaken = false;
                try
                {
                    _spinLock.Enter(ref lockTaken);
                    return _probeInFlight ? _probingMtu : 0;
                }
                finally
                {
                    if (lockTaken)
                        _spinLock.Exit(false);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetProbePaddingSize(double currentTime)
        {
            bool lockTaken = false;
            try
            {
                _spinLock.Enter(ref lockTaken);

                if (_discoveryComplete)
                    return 0;

                if (_probeInFlight)
                {
                    if (currentTime - _lastProbeSentTime > MtuConfig.ProbeTimeoutSeconds)
                        HandleProbeTimeout();
                    else
                        return 0;
                }

                if (currentTime - _lastProbeSentTime < MtuConfig.ProbeIntervalSeconds)
                    return 0;

                _probingMtu = (_lowBound + _highBound) / 2;
                if (_highBound - _lowBound <= 50)
                {
                    _discoveryComplete = true;
                    _confirmedMtu = _lowBound;
                    return 0;
                }

                _probeInFlight = true;
                _lastProbeSentTime = currentTime;

                int padding = _probingMtu - MtuConfig.HeaderOverhead;
                return Math.Max(0, padding);
            }
            finally
            {
                if (lockTaken)
                    _spinLock.Exit(false);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnPongReceived(int receivedPaddingSize, double currentTime)
        {
            bool lockTaken = false;
            try
            {
                _spinLock.Enter(ref lockTaken);

                if (_discoveryComplete)
                    return;

                if (!_probeInFlight)
                    return;

                int receivedMtu = receivedPaddingSize + MtuConfig.HeaderOverhead;

                if (Math.Abs(receivedMtu - _probingMtu) <= 10)
                {
                    _probeInFlight = false;
                    _lastProbeReceivedTime = currentTime;
                    _failureCount = 0;
                    _successCount++;

                    if (_successCount >= MtuConfig.RequiredSuccessCount)
                    {
                        _lowBound = _probingMtu;
                        _successCount = 0;
                    }
                }
            }
            finally
            {
                if (lockTaken)
                    _spinLock.Exit(false);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Update(double currentTime)
        {
            bool lockTaken = false;
            try
            {
                _spinLock.Enter(ref lockTaken);

                if (_discoveryComplete)
                    return;

                if (_probeInFlight && currentTime - _lastProbeSentTime > MtuConfig.ProbeTimeoutSeconds)
                    HandleProbeTimeout();
            }
            finally
            {
                if (lockTaken)
                    _spinLock.Exit(false);
            }
        }

        private void HandleProbeTimeout()
        {
            _probeInFlight = false;
            _successCount = 0;
            _failureCount++;

            if (_failureCount >= MtuConfig.MaxFailureCount)
            {
                _highBound = _probingMtu;
                _failureCount = 0;

                if (_highBound - _lowBound <= 50)
                {
                    _discoveryComplete = true;
                    _confirmedMtu = _lowBound;
                }
            }
        }

        internal void Reset()
        {
            bool lockTaken = false;
            try
            {
                _spinLock.Enter(ref lockTaken);

                _confirmedMtu = MtuConfig.DefaultMtu;
                _probingMtu = 0;
                _lowBound = MtuConfig.MinMtu;
                _highBound = MtuConfig.MaxMtu;
                _successCount = 0;
                _failureCount = 0;
                _lastProbeSentTime = 0;
                _lastProbeReceivedTime = 0;
                _probeInFlight = false;
                _discoveryComplete = false;
            }
            finally
            {
                if (lockTaken)
                    _spinLock.Exit(false);
            }
        }

        internal void SetMtu(int mtu)
        {
            bool lockTaken = false;
            try
            {
                _spinLock.Enter(ref lockTaken);
                _confirmedMtu = Math.Clamp(mtu, MtuConfig.MinMtu, MtuConfig.MaxMtu);
                _discoveryComplete = true;
            }
            finally
            {
                if (lockTaken)
                    _spinLock.Exit(false);
            }
        }
    }
}
