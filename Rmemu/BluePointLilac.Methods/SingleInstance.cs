using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BluePointLilac.Methods
{
    public static class SingleInstance
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int cmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static Mutex instanceMutex;
        private static readonly object InstanceGate = new object();

        public static bool IsRunning()
        {
            lock(InstanceGate)
            {
                if(instanceMutex != null) return false;
                string executable = Path.GetFullPath(Application.ExecutablePath).ToUpperInvariant();
                string name = @"Local\Rmemu-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable)));
                var candidate = new Mutex(false, name);
                bool acquired;
                try { acquired = candidate.WaitOne(0); }
                catch(AbandonedMutexException) { acquired = true; }
                if(acquired)
                {
                    instanceMutex = candidate; // Keep ownership until process exit, including the startup window.
                    return false;
                }
                candidate.Dispose();
            }
            using(Process current = Process.GetCurrentProcess())
            {
                foreach(Process process in Process.GetProcessesByName(current.ProcessName))
                    using(process)
                    {
                        if(process.Id == current.Id) continue;
                        try
                        {
                            if(!string.Equals(process.MainModule?.FileName, current.MainModule?.FileName, StringComparison.OrdinalIgnoreCase)) continue;
                            ShowWindowAsync(process.MainWindowHandle, 9);
                            SetForegroundWindow(process.MainWindowHandle);
                            return true;
                        }
                        catch(Win32Exception) { /* A different integrity level may deny process inspection. */ }
                        catch(InvalidOperationException) { /* The other process exited while being inspected. */ }
                    }
                return true;
            }
        }

        public static bool WaitForPreviousInstance(string[] args)
        {
            int index = Array.IndexOf(args, "--restart-from");
            if(index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out int id)) return true;
            if(id == Environment.ProcessId) return true;
            try
            {
                using(Process previous = Process.GetProcessById(id))
                {
                    if(!string.Equals(previous.MainModule?.FileName, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return true;
                    return previous.WaitForExit(10000);
                }
            }
            catch(ArgumentException) { return true; }
            catch(InvalidOperationException) { return true; }
            catch(Win32Exception) { return true; }
        }

        public static void Restart(string[] args = null, bool elevated = false)
        {
            var start = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true };
            if(elevated) start.Verb = "runas";
            start.ArgumentList.Add("--restart-from");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if(args != null) foreach(string argument in args) start.ArgumentList.Add(argument);
            using(Process process = Process.Start(start))
                if(process != null) Application.Exit();
        }
    }
}
