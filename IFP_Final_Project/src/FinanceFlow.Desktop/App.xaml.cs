using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FinanceFlow.Desktop;

/// <summary>The entry point of the desktop application of BarsiG.</summary>
public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _activationCancellation = new();
    private Mutex? _instanceMutex;
    private Task? _activationListener;
    private bool _ownsInstance;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        string identity = GetInstanceIdentity();
        string mutexName = $@"Local\BarsiG-{identity}";
        string pipeName = $"barsig-{identity}";

        _instanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            try
            {
                SignalExistingInstance(pipeName);
            }
            finally
            {
                _instanceMutex.Dispose();
                _instanceMutex = null;
            }

            Shutdown();
            return;
        }

        _ownsInstance = true;
        base.OnStartup(e);

        MainWindow window = new();
        MainWindow = window;
        window.Show();

        _activationListener = Task.Run(
            () => ListenForActivationAsync(pipeName, _activationCancellation.Token));
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation.Cancel();

        try
        {
            _activationListener?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The application is already closing; the listener has no work to preserve.
        }

        if (_ownsInstance && _instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex is released by Windows when the owning process exits.
            }
        }

        _instanceMutex?.Dispose();
        _activationCancellation.Dispose();

        base.OnExit(e);
    }

    private static string GetInstanceIdentity()
    {
        string user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string executable = Path.GetFullPath(Environment.ProcessPath ?? AppContext.BaseDirectory);
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{user}\n{executable}"));

        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// The second process sends only an activation request. Its wait is bounded so a
    /// damaged primary instance can never leave the new launch stuck indefinitely.
    /// </summary>
    private static void SignalExistingInstance(string pipeName)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using NamedPipeClientStream client = new(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.None);

                client.Connect(250);

                byte[] processIdBytes = new byte[sizeof(int)];
                ReadExactly(client, processIdBytes);
                int processId = BitConverter.ToInt32(processIdBytes);

                NativeMethods.AllowSetForegroundWindow(processId);

                client.WriteByte(0x42);
                client.Flush();
                return;
            }
            catch (TimeoutException)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int read = 0;

        while (read < buffer.Length)
        {
            int current = stream.Read(buffer, read, buffer.Length - read);

            if (current == 0)
            {
                throw new EndOfStreamException("The running BarsiG instance closed the activation pipe.");
            }

            read += current;
        }
    }

    private async Task ListenForActivationAsync(string pipeName, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using NamedPipeServerStream pipe = new(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken);

                byte[] processIdBytes = BitConverter.GetBytes(Environment.ProcessId);
                await pipe.WriteAsync(processIdBytes, cancellationToken);
                await pipe.FlushAsync(cancellationToken);

                byte[] commandBuffer = new byte[1];
                int bytesRead = await pipe.ReadAsync(commandBuffer, cancellationToken);

                if (bytesRead == 1 && commandBuffer[0] == 0x42)
                {
                    _ = Dispatcher.BeginInvoke(
                        DispatcherPriority.Normal,
                        new Action(ActivateMainWindow));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A client that disconnects early does not affect the running window.
            }
        }
    }

    private void ActivateMainWindow()
    {
        Window? window = MainWindow;

        if (window is null)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();

        IntPtr handle = new WindowInteropHelper(window).Handle;

        if (handle != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(handle);
        }
    }
}
