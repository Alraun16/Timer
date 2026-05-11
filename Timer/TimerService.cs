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


        // Состояние таймера открыто наружу только для чтения.
        public bool IsIdle { get; private set; } = true;
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }

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

            IsRunning = true;
            IsPaused = false;
            IsIdle = false;
            _lastTickUtc = DateTime.UtcNow;
        }

        public void Pause()
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            IsPaused = true;
        }

        public void Reset()
        {
            IsRunning = false;
            IsPaused = false;
            IsIdle = true;
            _remaining = _duration;

            Tick?.Invoke(_remaining);
        }

        public void Finish()
        {
            if (IsIdle) return;

            Complete();
        }

        #endregion

        #region Tick Logic

        public void UpdateTick()
        {
            if (!IsRunning)
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
            if (IsRunning)
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
            IsRunning = false;
            IsPaused = false;
            IsIdle = true;
            _remaining = TimeSpan.Zero;

            Tick?.Invoke(_remaining);
            Completed?.Invoke();
        }

        #endregion
    }
}
