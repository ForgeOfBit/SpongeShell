using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;

namespace SpongeShell
{
    public sealed partial class MainWindow : Window
    {
        private Process? _shellProcess;
        private StreamWriter? _shellInput;
        private CancellationTokenSource? _readCts;
        private bool _shellRunning;
        private readonly object _outputLock = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TerminalOutput.Text = "";
            await StartShellAsync();
        }

        private async Task StartShellAsync()
        {
            if (_shellRunning)
                return;

            try
            {
                ShellStatusText.Text = "Shell başlatılıyor...";

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/K",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardInputEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };

                _shellProcess = new Process { StartInfo = psi };
                _shellProcess.EnableRaisingEvents = true;
                _shellProcess.Exited += (s, args) => _ = DispatcherQueue.TryEnqueue(() => OnShellExited());

                _shellProcess.Start();
                _shellRunning = true;

                _shellInput = new StreamWriter(_shellProcess.StandardInput.BaseStream, Encoding.UTF8)
                {
                    AutoFlush = true
                };

                ShellStatusText.Text = "Shell çalışıyor — komut yazabilirsiniz";
                TerminalOutput.Focus(FocusState.Programmatic);

                _readCts = new CancellationTokenSource();
                var token = _readCts.Token;

                _ = Task.Run(() => ReadOutputLoopAsync(_shellProcess.StandardOutput, token));
                _ = Task.Run(() => ReadOutputLoopAsync(_shellProcess.StandardError, token));
            }
            catch (Exception ex)
            {
                AppendOutput($"\r\n[Hata] Shell başlatılamadı: {ex.Message}\r\n");
                ShellStatusText.Text = "Shell başlatılamadı";
            }
        }

        private async Task ReadOutputLoopAsync(StreamReader reader, CancellationToken token)
        {
            var buffer = new char[4096];
            var sb = new StringBuilder();

            try
            {
                while (!token.IsCancellationRequested && _shellProcess != null && !_shellProcess.HasExited)
                {
                    int count = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                    if (count <= 0)
                        break;

                    sb.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        char c = buffer[i];
                        if (c == '\0')
                            continue;
                        sb.Append(c);
                    }

                    if (sb.Length > 0)
                    {
                        string chunk = sb.ToString();
                        AppendOutput(chunk);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (_shellRunning)
                    AppendOutput("\r\n[Çıktı okuma sonlandı]\r\n");
            }
        }

        private void AppendOutput(string text)
        {
            lock (_outputLock)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (TerminalOutput == null) return;
                    TerminalOutput.Text += text;
                    TerminalOutput.SelectionStart = TerminalOutput.Text.Length;
                    TerminalOutput.SelectionLength = 0;
                    TerminalScroll.ChangeView(TerminalScroll.HorizontalOffset, TerminalScroll.ScrollableHeight, 1f);
                });
            }
        }

        private void OnShellExited()
        {
            _shellRunning = false;
            _readCts?.Cancel();
            ShellStatusText.Text = "Shell kapandı";
            AppendOutput("\r\n[Shell oturumu sonlandı]\r\n");
        }

        private void TerminalOutput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!_shellRunning || _shellInput == null || _shellProcess == null || _shellProcess.HasExited)
            {
                e.Handled = true;
                return;
            }

            VirtualKey key = e.Key;
            string? toSend = null;

            if (key == VirtualKey.Enter)
                toSend = "\r\n";
            else if (key == VirtualKey.Back)
                toSend = "\b";
            else if (key == VirtualKey.Tab)
                toSend = "\t";
            else if (key == VirtualKey.Up)
                toSend = "\x1B[A";
            else if (key == VirtualKey.Down)
                toSend = "\x1B[B";
            else if (key == VirtualKey.Right)
                toSend = "\x1B[C";
            else if (key == VirtualKey.Left)
                toSend = "\x1B[D";
            else if (key == VirtualKey.Escape)
                toSend = "\x1B";
            else if (key >= VirtualKey.Space && key <= VirtualKey.Z || key >= VirtualKey.Number0 && key <= VirtualKey.Number9 ||
                     key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9 || key == VirtualKey.Decimal ||
                     key == VirtualKey.Add || key == VirtualKey.Subtract || key == VirtualKey.Multiply || key == VirtualKey.Divide ||
                     key == VirtualKey.Oem1 || key == VirtualKey.Oem2 || key == VirtualKey.Oem3 || key == VirtualKey.Oem4 ||
                     key == VirtualKey.Oem5 || key == VirtualKey.Oem6 || key == VirtualKey.Oem7 || key == VirtualKey.OemComma ||
                     key == VirtualKey.OemPeriod || key == VirtualKey.OemMinus || key == VirtualKey.OemPlus)
            {
                try
                {
                    var coreWindow = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
                    bool shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                    char ch = KeyToChar(key, shift);
                    if (ch != '\0')
                        toSend = ch.ToString();
                }
                catch { }
            }

            if (toSend != null)
            {
                try
                {
                    _shellInput.Write(toSend);
                    _shellInput.Flush();
                }
                catch { }
                e.Handled = true;
            }
        }

        private static char KeyToChar(VirtualKey key, bool shift)
        {
            if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
                return (char)(key - VirtualKey.Number0 + '0');
            if (key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9)
                return (char)(key - VirtualKey.NumberPad0 + '0');
            if (key >= VirtualKey.A && key <= VirtualKey.Z)
                return (char)(key - VirtualKey.A + (shift ? 'A' : 'a'));
            if (key == VirtualKey.Space) return ' ';

            return key switch
            {
                VirtualKey.Decimal => '.',
                VirtualKey.Add => '+',
                VirtualKey.Subtract => '-',
                VirtualKey.Multiply => '*',
                VirtualKey.Divide => '/',
                VirtualKey.OemComma => shift ? ';' : ',',
                VirtualKey.OemPeriod => shift ? ':' : '.',
                VirtualKey.OemMinus => shift ? '_' : '-',
                VirtualKey.OemPlus => shift ? '+' : '=',
                VirtualKey.Oem1 => shift ? ':' : ';',
                VirtualKey.Oem2 => shift ? '?' : '/',
                VirtualKey.Oem3 => shift ? '~' : '`',
                VirtualKey.Oem4 => shift ? '{' : '[',
                VirtualKey.Oem5 => shift ? '|' : '\\',
                VirtualKey.Oem6 => shift ? '}' : ']',
                VirtualKey.Oem7 => shift ? '"' : '\'',
                _ => '\0'
            };
        }

        private void TerminalOutput_KeyUp(object sender, KeyRoutedEventArgs e) { }
    }
}
