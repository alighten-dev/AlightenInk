using System;
using System.Threading;
using Velopack;

namespace AlightenInk
{
    public static class Program
    {
        private const string MutexName = @"Local\AlightenInk.SingleInstance.6E1C";
        internal const string ShowEventName = @"Local\AlightenInk.ShowToolbar.6E1C";

        [STAThread]
        public static int Main(string[] args)
        {
            // Must run first: Velopack install/uninstall/update hooks exit the process from here.
            VelopackApp.Build().Run();

            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // Already running: ask that instance to bring its toolbar back, then leave quietly.
                try
                {
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
                    {
                        using (ev) ev.Set();
                    }
                }
                catch
                {
                    // Nothing useful to do; the other instance keeps running.
                }
                return 1;
            }

            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var app = new App(showEvent);
            app.InitializeComponent();
            int code = app.Run();
            GC.KeepAlive(mutex);
            return code;
        }
    }
}
