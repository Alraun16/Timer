using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Timer
{
    internal sealed class SingleInstanceService : IDisposable
    {
        private const string MutexName = "Timer.SingleInstance";
        private const string PipeName = "Timer.SingleInstance.Activation";
        private const char Separator = '\u001f';

        private readonly Action<string[]> _activationReceived;
        private readonly CancellationTokenSource _cancellation = new();

        private Mutex? _mutex;

        public SingleInstanceService(Action<string[]> activationReceived)
        {
            _activationReceived = activationReceived;
        }

        public bool TryStart(string[] args)
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                SendActivation(args);
                return false;
            }

            _ = ListenAsync(_cancellation.Token);
            return true;
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _mutex?.Dispose();
            _cancellation.Dispose();
        }

        private static void SendActivation(string[] args)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(500);

                using var writer = new StreamWriter(pipe, Encoding.UTF8);
                writer.Write(string.Join(Separator, args));
            }
            catch
            {
                // If the existing instance is closing, silently ignore this activation.
            }
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(cancellationToken);

                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    string payload = await reader.ReadToEndAsync(cancellationToken);
                    string[] args = string.IsNullOrEmpty(payload)
                        ? Array.Empty<string>()
                        : payload.Split(Separator);

                    _activationReceived(args);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Keep the activation pipe alive after transient IPC failures.
                }
            }
        }
    }
}
