using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteOptions.Ipc;
using Microsoft.UI.Dispatching;

namespace LiteOptions.SettingsApp
{
    /// <summary>What the window knows: a working copy of the settings plus the engine's view of devices and Windows settings.</summary>
    public sealed class Store
    {
        public readonly IEngine Engine;
        readonly DispatcherQueue dq;
        readonly DispatcherQueueTimer saveTimer;

        public AppSettings Settings { get; private set; }
        public List<DeviceDto> Devices { get; private set; } = new List<DeviceDto>();
        public SystemDto System { get; private set; } = new SystemDto();
        public bool Loaded { get; private set; }
        public bool Connected => Engine.Connected;

        /// <summary>The profile being edited: "" = all applications, otherwise an exe name.</summary>
        public string CurrentApp = "";

        public event Action SettingsChanged, DevicesChanged, SystemChanged, ConnectionChanged;
        public event Action<int, int> Captured;

        public Store(IEngine engine, DispatcherQueue dq)
        {
            Engine = engine;
            this.dq = dq;
            Settings = new AppSettings();
            Settings.Normalize();
            saveTimer = dq.CreateTimer();
            saveTimer.Interval = TimeSpan.FromMilliseconds(250);
            saveTimer.IsRepeating = false;
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); _ = SaveNowAsync(); };
            engine.ConnectionChanged += OnConnection;
            engine.Pushed += OnPushed;
        }

        public void Start() => Engine.Start(dq);

        public DeviceDto Mouse => Devices.FirstOrDefault(d => d.Kind == "Mouse");
        public DeviceDto Keyboard => Devices.FirstOrDefault(d => d.Kind == "Keyboard");

        public Profile Current => Settings.Profiles.FirstOrDefault(p => p.App == (CurrentApp ?? "")) ?? Settings.Global;

        async void OnConnection(bool on)
        {
            ConnectionChanged?.Invoke();
            if (!on) return;
            var r = await Engine.RequestAsync(new IpcMessage { Cmd = "hello", Text = "ui" });
            if (r.Error != null) return;
            Apply(r);
        }

        void Apply(IpcMessage r)
        {
            if (r.Settings != null && !saveTimer.IsRunning)
            {
                r.Settings.Normalize();
                Settings = r.Settings;
                if (Settings.ForApp(CurrentApp) == null) CurrentApp = "";
                SettingsChanged?.Invoke();
            }
            if (r.Devices != null) { Devices = r.Devices; DevicesChanged?.Invoke(); }
            if (r.System != null) { System = r.System; SystemChanged?.Invoke(); }
            Loaded = true;
        }

        void OnPushed(IpcMessage m)
        {
            switch (m.Event)
            {
                case "settings":
                    Apply(new IpcMessage { Settings = m.Settings });
                    break;
                case "devices":
                    Apply(new IpcMessage { Devices = m.Devices });
                    _ = RefreshSystemAsync(); // the reverse-scroll state depends on which mouse is connected
                    break;
                case "captured":
                    Captured?.Invoke(m.Mods, m.Vk);
                    break;
            }
        }

        public async Task RefreshAsync()
        {
            var r = await Engine.RequestAsync(new IpcMessage { Cmd = "state" });
            if (r.Error == null) Apply(r);
        }

        async Task RefreshSystemAsync()
        {
            var r = await Engine.RequestAsync(new IpcMessage { Cmd = "state" });
            if (r.Error == null && r.System != null) { System = r.System; SystemChanged?.Invoke(); }
        }

        /// <summary>Settings were edited in the window: send them to the engine shortly (edits in quick succession go together).</summary>
        public void Save()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        public async Task SaveNowAsync()
        {
            saveTimer.Stop();
            Settings.Normalize();
            var r = await Engine.RequestAsync(new IpcMessage { Cmd = "saveSettings", Settings = Settings });
            if (r.Error != null) try { Logger.Write("settings window: save failed: " + r.Error); } catch { }
        }

        public Task<IpcMessage> Send(string cmd, int value = 0, bool on = false, string text = null, int timeoutMs = 8000) =>
            Engine.RequestAsync(new IpcMessage { Cmd = cmd, Value = value, On = on, Text = text }, timeoutMs);

        public bool IsLow(DeviceDto d) => d != null && d.BatteryLow;
    }
}
