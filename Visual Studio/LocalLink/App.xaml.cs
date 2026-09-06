using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace LocalLink
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Global\LocalLink.YiLiu.SingleInstance";
        private const string ActivationPipeName = "LocalLink.YiLiu.Activation";
        private static Mutex? _singleInstanceMutex;
        private readonly CancellationTokenSource _activationPipeCancellation = new();
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                SendShowRequestToExistingInstance();
                Environment.Exit(0);
                return;
            }

            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
            _ = ListenForActivationRequestsAsync(_activationPipeCancellation.Token);
        }

        private async Task ListenForActivationRequestsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(
                        ActivationPipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(cancellationToken);
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var message = await reader.ReadLineAsync(cancellationToken);
                    if (string.Equals(message, "show", StringComparison.OrdinalIgnoreCase))
                    {
                        _window?.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (_window is MainWindow mainWindow)
                            {
                                mainWindow.ShowFromExternalActivation();
                                return;
                            }

                            _window?.Activate();
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                }
            }
        }

        private static void SendShowRequestToExistingInstance()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out);
                    pipe.Connect(50);
                    using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                    writer.WriteLine("show");
                    return;
                }
                catch
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
