using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LiteOptions.Input
{
    /// <summary>
    /// Turns each click of a notched scroll wheel (120 units) into a quick glide of small wheel steps, like Options+'s
    /// "smooth scrolling". The amount still to scroll decays exponentially, so a click finishes in about 150 ms and
    /// fast flicks add up and keep gliding. Runs on its own thread; the hook only hands it the wheel delta.
    /// </summary>
    public sealed class SmoothScroller : IDisposable
    {
        /// <summary>Time constant of the glide in seconds: about 95% of a click has arrived after 3 × Tau.</summary>
        public const double Tau = 0.05;

        readonly object sync = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        double pending;   // wheel units still to send (signed)
        double carry;     // part of a unit already "sent" in the maths but not yet as an integer step
        Thread thread;
        volatile bool disposed;

        /// <summary>Tests replace the output; real use injects wheel input.</summary>
        internal Action<int> Output = SendWheel;

        public void Add(int delta)
        {
            if (delta == 0 || disposed) return;
            Queue(delta);
            if (thread == null)
            {
                thread = new Thread(Loop) { IsBackground = true, Name = "smooth-scroll", Priority = ThreadPriority.AboveNormal };
                thread.Start();
            }
            wake.Set();
        }

        void Loop()
        {
            while (!disposed)
            {
                wake.WaitOne();
                if (disposed) break;
                timeBeginPeriod(1); // 1 ms sleeps while gliding, so the steps are even
                try { Glide(); }
                finally { timeEndPeriod(1); }
            }
        }

        void Glide()
        {
            var clock = Stopwatch.StartNew();
            double last = 0;
            while (!disposed)
            {
                Thread.Sleep(5);
                double now = clock.Elapsed.TotalSeconds;
                int step = Step(now - last);
                last = now;
                if (step != 0) Output(step);
                lock (sync) if (pending == 0 && Math.Abs(carry) < 0.5) { carry = 0; return; }
            }
        }

        internal void Queue(int delta)
        {
            lock (sync)
            {
                // reversing direction stops the current glide at once rather than fighting it
                if (pending != 0 && Math.Sign(pending) != Math.Sign(delta)) { pending = 0; carry = 0; }
                pending += delta;
            }
        }

        /// <summary>Advance the glide by dt seconds and return the whole wheel units to send now.</summary>
        internal int Step(double dt)
        {
            lock (sync)
            {
                double portion = pending * (1 - Math.Exp(-Math.Max(0, dt) / Tau));
                if (Math.Abs(pending - portion) < 4) portion = pending; // finish the tail instead of creeping
                pending -= portion;
                carry += portion;
                int whole = (int)Math.Round(carry);
                carry -= whole;
                return whole;
            }
        }

        internal double PendingForTests { get { lock (sync) return pending + carry; } }

        static void SendWheel(int delta)
        {
            var input = new Native.INPUT
            {
                type = Native.INPUT_MOUSE,
                U = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = Native.MOUSEEVENTF_WHEEL, mouseData = unchecked((uint)delta), dwExtraInfo = ActionRunner.Signature } }
            };
            Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public void Dispose()
        {
            disposed = true;
            wake.Set();
        }

        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);
    }
}
