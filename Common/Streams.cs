using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Tofvesson.Common
{
    public sealed class TimeStampWriter : TextWriter
    {
        private readonly DateTime time = DateTime.Now;
        private readonly string dateFormat;
        private readonly TextWriter underlying;
        private bool triggered;

        public TimeStampWriter(TextWriter underlying, string dateFormat, bool emulateNL = true)
        {
            this.dateFormat = dateFormat;
            this.underlying = underlying;
            triggered = emulateNL;
        }

        public TimeStampWriter(TextWriter underlying, string dateFormat, IFormatProvider formatProvider, bool emulateNL = true) : base(formatProvider)
        {
            this.dateFormat = dateFormat;
            this.underlying = underlying;
            triggered = emulateNL;
        }

        public override Encoding Encoding => underlying.Encoding;

        public override void Write(char value)
        {
            if (triggered)
            {
                StringBuilder s = new StringBuilder();
                s.Append('[').Append(time.ToString(dateFormat)).Append("] ");
                foreach (var c in s.ToString()) underlying.Write(c); 
            }
            underlying.Write(value);
            triggered = value == '\n';
        }
    }

    // A TextWriter wrapper for the Debug output
    public sealed class DebugAdapterWriter : TextWriter
    {
        public override Encoding Encoding => throw new NotImplementedException();

        public override void Write(char value)
        {
            Debug.Write(value);
        }
    }
}
