using System;

namespace Timer
{
    public class TimerService
    {
        private TimeSpan _duration;
        private TimeSpan _remaining;

        private bool _isRunning;
        private bool _isFinished;

        private DateTime _lastTickUtc;

        public TimeSpan Duration => _duration;
        public TimeSpan Remaining => _remaining;

        public bool IsRunning => _isRunning;
        public bool IsFinished => _isFinished;

        public event Action<TimeSpan>? Tick;
        public event Action? Completed;

        #region Public API

        public void SetDuration(TimeSpan duration, bool resetRemaining = true)
        {
            _duration = duration;

            if (resetRemaining)
            {
                _remaining = duration;
                _isFinished = false;
            }
        }

        public void Start()
        {
            if (_remaining <= TimeSpan.Zero)
                return;

            _isRunning = true;
            _isFinished = false;
            _lastTickUtc = DateTime.UtcNow;
        }

        public void Pause()
        {
            _isRunning = false;
        }

        public void Reset()
        {
            _isRunning = false;
            _isFinished = false;
            _remaining = _duration;

            Tick?.Invoke(_remaining);
        }

        #endregion

        #region Tick Logic

        public void UpdateTick()
        {
            if (!_isRunning)
                return;

            var now = DateTime.UtcNow;
            _remaining -= now - _lastTickUtc;
            _lastTickUtc = now;

            if (_remaining <= TimeSpan.Zero)
            {
                Complete();
                return;
            }

            Tick?.Invoke(_remaining);
        }

        #endregion

        #region State Transitions

        public void Toggle()
        {
            if (_isRunning)
            {
                Pause();
            }
            else
            {
                Start();
            }
        }

        private void Complete()
        {
            _isRunning = false;
            _isFinished = true;
            _remaining = TimeSpan.Zero;

            Tick?.Invoke(_remaining);
            Completed?.Invoke();
        }

        #endregion
    }
}