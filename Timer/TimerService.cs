using System;

namespace Timer
{
    public class TimerService
    {
        private TimeSpan _duration;
        private TimeSpan _remaining;

        private DateTime _lastTickUtc;

        public TimeSpan Duration => _duration;
        public TimeSpan Remaining => _remaining;


        public bool _isIdle = true;
        public bool _isRunning;
        public bool _isPaused;

        public event Action<TimeSpan>? Tick;
        public event Action? Completed;

        #region Public API

        public void SetDuration(TimeSpan duration, bool resetRemaining = true)
        {
            _duration = duration;

            if (resetRemaining)
            {
                _remaining = duration;
            }
        }

        public void Start()
        {
            if (_remaining <= TimeSpan.Zero)
                return;

            _isRunning = true;
            _isPaused = false;
            _isIdle = false;
            _lastTickUtc = DateTime.UtcNow;
        }

        public void Pause()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _isPaused = true;
        }

        public void Reset()
        {
            _isRunning = false;
            _isPaused = false;
            _isIdle = true;
            _remaining = _duration;

            Tick?.Invoke(_remaining);
        }

        public void Finish()
        {
            if (_isIdle) return;

            Complete();
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

        public void TogglePlayPause()
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
            _isPaused = false;
            _isIdle = true;
            _remaining = TimeSpan.Zero;

            Tick?.Invoke(_remaining);
            Completed?.Invoke();
        }

        #endregion
    }
}