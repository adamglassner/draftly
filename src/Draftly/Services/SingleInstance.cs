using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Draftly.Services;

/// <summary>
/// Lets a file opened from File Explorer go to a new tab in the Draftly window that's already open
/// (like Windows 11 Notepad), instead of starting a second window. Uses a named pipe that only the
/// current user in the current Windows session can connect to.
/// </summary>
public static class SingleInstance
{
    /// <summary>Overridable so the self-test can use its own pipe.</summary>
    public static string PipeName { get; set; } =
        $"Draftly-{Environment.UserName}-{Process.GetCurrentProcess().SessionId}";

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
    private const int ASFW_ANY = -1;

    /// <summary>Sends files to a running Draftly. Returns false if none is listening.</summary>
    public static bool TrySend(IReadOnlyList<string> files, int timeoutMs = 400)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            AllowSetForegroundWindow(ASFW_ANY);   // let the running window come to the front
            using var w = new StreamWriter(client, new UTF8Encoding(false));
            w.WriteLine("DRAFTLY-OPEN-1");
            foreach (var f in files) w.WriteLine(f);
            w.WriteLine("END");
            w.Flush();
            return true;
        }
        catch { return false; }
    }

    private static Thread _server;

    /// <summary>Starts listening for files from later launches. <paramref name="onFiles"/> runs on a background thread.</summary>
    public static void Listen(Action<string[]> onFiles)
    {
        if (_server != null) return;
        _server = new Thread(() =>
        {
            while (true)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.CurrentUserOnly);
                }
                catch { Thread.Sleep(2000); continue; }   // another Draftly window owns the pipe for now (e.g. closing after an update)
                try
                {
                    using (pipe)
                    {
                        pipe.WaitForConnection();
                        using var r = new StreamReader(pipe, Encoding.UTF8);
                        if (r.ReadLine() != "DRAFTLY-OPEN-1") continue;
                        var files = new List<string>();
                        string line;
                        while ((line = r.ReadLine()) != null && line != "END")
                            if (line.Length > 0 && line.Length < 32768) files.Add(line);
                        onFiles(files.ToArray());
                    }
                }
                catch { Thread.Sleep(100); }
            }
        })
        { IsBackground = true, Name = "Draftly file receiver" };
        _server.Start();
    }
}
