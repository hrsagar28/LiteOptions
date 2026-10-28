using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace LiteOptions.Ipc
{
    /// <summary>Named-pipe server inside the engine. Requests are handled on the UI thread; replies and pushes go back per client.</summary>
    public sealed class EngineServer : IDisposable
    {
        public sealed class Client
        {
            readonly object gate = new object();
            internal readonly NamedPipeServerStream Pipe;
            internal StreamWriter Writer;
            public bool IsUi;

            internal Client(NamedPipeServerStream pipe)
            {
                Pipe = pipe;
                Writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            }

            public void Send(IpcMessage m)
            {
                try { lock (gate) Writer.WriteLine(Ipc.ToLine(m)); }
                catch { /* client went away */ }
            }
        }

        readonly Func<IpcMessage, Client, IpcMessage> handler;
        readonly SynchronizationContext ui;
        readonly List<Client> clients = new List<Client>();
        volatile bool stopping;
        Thread acceptThread;
        readonly string name = Ipc.PipeName;

        public EngineServer(SynchronizationContext ui, Func<IpcMessage, Client, IpcMessage> handler)
        {
            this.ui = ui;
            this.handler = handler;
        }

        public void Start()
        {
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "ipc-accept" };
            acceptThread.Start();
        }

        NamedPipeServerStream NewPipe()
        {
            var security = new PipeSecurity();
            var me = WindowsIdentity.GetCurrent().User;
            security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
            return new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 8192, 8192, security);
        }

        void AcceptLoop()
        {
            while (!stopping)
            {
                NamedPipeServerStream pipe;
                try { pipe = NewPipe(); }
                catch (Exception ex) { Logger.Write("settings link: " + ex.Message); Thread.Sleep(2000); continue; }
                try { pipe.WaitForConnection(); }
                catch { pipe.Dispose(); if (stopping) return; continue; }
                if (stopping) { pipe.Dispose(); return; }
                var c = new Client(pipe);
                lock (clients) clients.Add(c);
                new Thread(() => Serve(c)) { IsBackground = true, Name = "ipc-client" }.Start();
            }
        }

        void Serve(Client c)
        {
            try
            {
                using (var reader = new StreamReader(c.Pipe, new UTF8Encoding(false)))
                {
                    string line;
                    while (!stopping && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        IpcMessage msg;
                        try { msg = Ipc.FromLine(line); }
                        catch (Exception ex) { c.Send(new IpcMessage { Ok = false, Error = "bad message: " + ex.Message }); continue; }
                        IpcMessage reply = null;
                        try { ui.Send(_ => reply = handler(msg, c), null); }
                        catch (Exception ex) { reply = new IpcMessage { Ok = false, Error = ex.Message }; }
                        if (reply != null && msg.Id != 0) { reply.Id = msg.Id; c.Send(reply); }
                    }
                }
            }
            catch { }
            finally
            {
                lock (clients) clients.Remove(c);
                try { c.Pipe.Dispose(); } catch { }
            }
        }

        public void Broadcast(IpcMessage m, bool uiOnly = false)
        {
            List<Client> list;
            lock (clients) list = new List<Client>(clients);
            foreach (var c in list) if (!uiOnly || c.IsUi) c.Send(m);
        }

        public bool HasUi
        {
            get { lock (clients) return clients.Exists(c => c.IsUi && c.Pipe.IsConnected); }
        }

        public void Dispose()
        {
            stopping = true;
            // unblock WaitForConnection with a throwaway client
            try { using (var poke = new NamedPipeClientStream(".", name, PipeDirection.InOut)) poke.Connect(200); } catch { }
            List<Client> list;
            lock (clients) list = new List<Client>(clients);
            foreach (var c in list) try { c.Pipe.Dispose(); } catch { }
        }
    }
}
