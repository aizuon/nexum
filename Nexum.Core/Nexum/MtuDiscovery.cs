using System;
using System.Runtime.CompilerServices;

namespace Nexum.Core
{
    internal sealed class MtuDiscovery
    {
        private readonly object _lock = new object();

        private int _confirmedMtu = MtuConfig.DefaultMtu;
        private bool _discoveryComplete;
        private int _failureCount;

        private int _highBound = MtuConfig.MaxMtu;
        private double _lastProbeReceivedTime;

        private double _lastProbeSentTime;

        private int _lowBound = MtuConfig.MinMtu;
        private bool _probeInFlight;
        private int _probingMtu;

        private int _successCount;

        internal int ConfirmedMtu
        {
            get
            {
                lock (_lock)
                {
                    return _confirmedMtu;
                }
            }
        }

        internal bool IsDiscoveryComplete
        {
            get
            {
                lock (_lock)
                {
                    return _discoveryComplete;
                }
            }
        }

        internal int ProbingMtu
        {
            get
            {
                lock (_lock)
                {
                    return _probeInFlight ? _probingMtu : 0;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int GetProbePaddingSize(double currentTime)
        {
            lock (_lock)
            {
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnPongReceived(int receivedPaddingSize, double currentTime)
        {
            lock (_lock)
            {
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Update(double currentTime)
        {
            lock (_lock)
            {
                if (_discoveryComplete)
                    return;

                if (_probeInFlight && currentTime - _lastProbeSentTime > MtuConfig.ProbeTimeoutSeconds)
                    HandleProbeTimeout();
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
            lock (_lock)
            {
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
        }

        internal void SetMtu(int mtu)
        {
            lock (_lock)
            {
                _confirmedMtu = Math.Clamp(mtu, MtuConfig.MinMtu, MtuConfig.MaxMtu);
                _discoveryComplete = true;
            }
        }
    }
}
