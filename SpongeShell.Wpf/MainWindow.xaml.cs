using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SpongeShell.Wpf;

public partial class MainWindow : Window
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
        if (_shellRunning) return;

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
            _shellProcess.Exited += (s, args) => Dispatcher.Invoke(() => OnShellExited());

            _shellProcess.Start();
            _shellRunning = true;

            _shellInput = new StreamWriter(_shellProcess.StandardInput.BaseStream, Encoding.UTF8) { AutoFlush = true };

            ShellStatusText.Text = "Shell çalışıyor — komut yazabilirsiniz";
            InputOverlay.Focus();
            Keyboard.Focus(InputOverlay);

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
                if (count <= 0) break;

                sb.Clear();
                for (int i = 0; i < count; i++)
                {
                    char c = buffer[i];
                    if (c == '\0') continue;
                    sb.Append(c);
                }

                if (sb.Length > 0)
                    AppendOutput(sb.ToString());
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
            Dispatcher.Invoke(() =>
            {
                if (TerminalOutput == null) return;
                TerminalOutput.AppendText(text);
                TerminalOutput.SelectionStart = TerminalOutput.Text.Length;
                TerminalScroll.ScrollToBottom();
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

    private void Window_Activated(object? sender, EventArgs e)
    {
        if (InputOverlay != null && _shellRunning)
            InputOverlay.Focus();
    }

    private void InputOverlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_shellRunning || _shellInput == null || _shellProcess == null || _shellProcess.HasExited)
        {
            e.Handled = true;
            return;
        }

        string? toSend = null;

        if (e.Key == Key.Enter)
            toSend = "\r\n";
        else if (e.Key == Key.Back)
            toSend = "\b";
        else if (e.Key == Key.Tab)
            toSend = "\t";
        else if (e.Key == Key.Up)
            toSend = "\x1B[A";
        else if (e.Key == Key.Down)
            toSend = "\x1B[B";
        else if (e.Key == Key.Right)
            toSend = "\x1B[C";
        else if (e.Key == Key.Left)
            toSend = "\x1B[D";
        else if (e.Key == Key.Escape)
            toSend = "\x1B";
        else if (e.Key == Key.Space)
            toSend = " ";
        else if (e.Key >= Key.A && e.Key <= Key.Z)
            toSend = ((char)(e.Key - Key.A + (Keyboard.Modifiers == ModifierKeys.Shift ? 'A' : 'a'))).ToString();
        else if (e.Key >= Key.D0 && e.Key <= Key.D9)
            toSend = ((char)(e.Key - Key.D0 + '0')).ToString();
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            toSend = ((char)(e.Key - Key.NumPad0 + '0')).ToString();
        else if (e.Key == Key.OemComma) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? ";" : ",");
        else if (e.Key == Key.OemPeriod) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? ":" : ".");
        else if (e.Key == Key.OemMinus) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? "_" : "-");
        else if (e.Key == Key.OemPlus) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? "+" : "=");
        else if (e.Key == Key.Oem1) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? ":" : ";");
        else if (e.Key == Key.Oem2) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? "?" : "/");
        else if (e.Key == Key.Oem3) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? "~" : "`");
        else if (e.Key == Key.Oem4) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? "{" : "[");
        else if (e.Key == Key.Oem5) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? "|" : "\\");
        else if (e.Key == Key.Oem6) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? "}" : "]");
        else if (e.Key == Key.Oem7) toSend = (Keyboard.Modifiers == ModifierKeys.Shift ? "\"" : "'");

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

    private void InputOverlay_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_shellRunning && _shellInput != null && _shellProcess != null && !_shellProcess.HasExited && !string.IsNullOrEmpty(e.Text))
        {
            try
            {
                _shellInput.Write(e.Text);
                _shellInput.Flush();
            }
            catch { }
            e.Handled = true;
        }
    }
}
