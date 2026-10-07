using System;
using System.Collections.Generic;
using System.Windows.Interop;
using AlightenInk.Core;

namespace AlightenInk
{
    /// <summary>Global hotkeys via RegisterHotKey on a message-only window. Failures are recorded, not fatal.</summary>
    internal sealed class HotkeyManager : IDisposable
    {
        private const int BaseId = 0xA170;
        private readonly HwndSource _source;
        private readonly Dictionary<int, HotkeyCommand> _ids = new Dictionary<int, HotkeyCommand>();

        public HotkeyManager()
        {
            var p = new HwndSourceParameters("AlightenInk Hotkeys")
            {
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
                WindowStyle = 0,
            };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
        }

        public event EventHandler<HotkeyCommand>? Pressed;

        /// <summary>Commands whose hotkey another app already owns.</summary>
        public List<HotkeyCommand> Failed { get; } = new List<HotkeyCommand>();

        public void RegisterDefaults()
        {
            int i = 0;
            foreach (var kv in DefaultHotkeys.Bindings)
            {
                int id = BaseId + i++;
                if (!Hotkey.TryParse(kv.Value, out var hk))
                {
                    Failed.Add(kv.Key);
                    continue;
                }
                if (Native.RegisterHotKey(_source.Handle, id, (uint)hk.Modifiers | Native.MOD_NOREPEAT, hk.VirtualKey))
                {
                    _ids[id] = kv.Key;
                }
                else
                {
                    Failed.Add(kv.Key);
                }
            }
        }

        /// <summary>Tooltip text for a command: "Ctrl+Shift+D" or a note that it is taken.</summary>
        public string Describe(HotkeyCommand cmd)
        {
            string keys = DefaultHotkeys.Bindings[cmd];
            return Failed.Contains(cmd) ? keys + " (in use by another app)" : keys;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && _ids.TryGetValue(wParam.ToInt32(), out var cmd))
            {
                handled = true;
                Pressed?.Invoke(this, cmd);
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            foreach (int id in _ids.Keys) Native.UnregisterHotKey(_source.Handle, id);
            _ids.Clear();
            _source.Dispose();
        }
    }
}
