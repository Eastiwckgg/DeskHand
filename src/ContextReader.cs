using System;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace DeskHand
{
    sealed class CaretContext
    {
        public string Before = "";
        public bool AtStart;
        public bool Password;
        public bool Known;
    }

    sealed class ContextReader
    {
        const int SettleMs = 40;
        const int Chars = 48;

        readonly SynchronizationContext ui;
        readonly Action<int, bool, CaretContext> deliver;
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly object gate = new object();
        Thread thread;
        int seq, version;
        bool allowStartCap;

        public ContextReader(SynchronizationContext ui, Action<int, bool, CaretContext> deliver)
        {
            this.ui = ui;
            this.deliver = deliver;
        }

        public void Request(int engineVersion, bool allowStart)
        {
            lock (gate)
            {
                seq++;
                version = engineVersion;
                allowStartCap = allowStart;
            }
            if (thread == null)
            {
                thread = new Thread(Loop) { IsBackground = true, Name = "DeskHand.Context" };
                thread.SetApartmentState(ApartmentState.MTA);
                thread.Start();
            }
            signal.Set();
        }

        void Loop()
        {
            while (true)
            {
                signal.WaitOne();
                int mySeq;
                do
                {
                    lock (gate) mySeq = seq;
                    Thread.Sleep(SettleMs);
                } while (mySeq != Volatile.Read(ref seq));

                int v;
                bool allow;
                lock (gate) { v = version; allow = allowStartCap; }
                var ctx = Read();
                ui.Post(delegate { deliver(v, allow, ctx); }, null);
            }
        }

        static CaretContext Read()
        {
            var ctx = new CaretContext();
            try
            {
                var el = AutomationElement.FocusedElement;
                if (el == null) return ctx;
                ctx.Password = el.Current.IsPassword;

                object pattern;
                if (el.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                {
                    var text = (TextPattern)pattern;
                    var sel = text.GetSelection();
                    if (sel.Length == 0) return ctx;
                    var range = sel[0].Clone();
                    range.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
                    int moved = range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -Chars);
                    ctx.AtStart = moved > -Chars;

                    var field = text.DocumentRange;
                    if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, field, TextPatternRangeEndpoint.Start) < 0)
                    {
                        range.MoveEndpointByRange(TextPatternRangeEndpoint.Start, field, TextPatternRangeEndpoint.Start);
                        ctx.AtStart = true;
                    }
                    ctx.Before = range.GetText(-1) ?? "";
                    ctx.Known = true;
                }
                else if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                {
                    if (string.IsNullOrEmpty(((ValuePattern)pattern).Current.Value))
                    {
                        ctx.AtStart = true;
                        ctx.Known = true;
                    }
                }
            }
            catch { }
            return ctx;
        }
    }
}
