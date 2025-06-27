using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace jeanf.tooltip
{
    public class ToolTipTimer
    {
        private CancellationTokenSource _cts;
        private bool _isTimerRunning = false;
        public bool IsTimerRunning => _isTimerRunning;

        public async UniTask StartTimer(float seconds, Action onTimerDone)
        {
            StopTimer();
            _cts = new CancellationTokenSource();
            _isTimerRunning = true;
            await PlayTimer(seconds, _cts.Token, onTimerDone);
        }


        public void StopTimer()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            _isTimerRunning = false;
        }

        private async UniTask PlayTimer(float seconds, CancellationToken token, Action onTimerDone)
        {
            try
            {
                await UniTask.Delay((int)(seconds * 1000), cancellationToken: token);
                if (!token.IsCancellationRequested)
                {
                    StopTimer();
                    onTimerDone?.Invoke();
                }
            }
            catch (OperationCanceledException) { StopTimer();}
        }
    }
}
