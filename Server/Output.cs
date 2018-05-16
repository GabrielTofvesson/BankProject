using Tofvesson.Common;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Server
{
    public static class Output
    {
        // Fancy timestamped output
        private static readonly object writeLock = new object();
        private static readonly TextWriter stampedError = new TimeStampWriter(Console.Error, "HH:mm:ss.fff");
        private static readonly TextWriter stampedOutput = new TimeStampWriter(Console.Out, "HH:mm:ss.fff");
        private static bool overwrite = false;
        private static short readStart_x = 0, readStart_y = 0;
        private static bool reading = false;
        private static List<char> peek = new List<char>();

        public static bool ReadingLine { get => reading; }
        public static Action OnNewLine { get; set; }


        public static void WriteLine(object message, bool error = false, bool timeStamp = true)
        {
            if (error) Error(message, true, timeStamp);
            else Info(message, true, timeStamp);
        }
        public static void Write(object message, bool error = false, bool timeStamp = true)
        {
            if (error) Error(message, false, timeStamp);
            else Info(message, false, timeStamp);
        }

        public static void WriteOverwritable(string message)
        {
            Info(message, false, false);
            overwrite = true;
        }

        public static void Raw(object message, bool newline = true) => Info(message, newline, false);
        public static void RawErr(object message, bool newline = true) => Error(message, newline, false);

        public static void Positive(object message, bool newline = true, bool timeStamp = true) =>
            Write(message == null ? "null" : message.ToString(), ConsoleColor.DarkGreen, ConsoleColor.Black, newline, timeStamp ? stampedOutput : Console.Out);
        public static void Info(object message, bool newline = true, bool timeStamp = true) =>
            Write(message == null ? "null" : message.ToString(), ConsoleColor.Gray, ConsoleColor.Black, newline, timeStamp ? stampedOutput : Console.Out);
        public static void Error(object message, bool newline = true, bool timeStamp = true) =>
            Write(message == null ? "null" : message.ToString(), ConsoleColor.Red, ConsoleColor.Black, newline, timeStamp ? stampedOutput : Console.Out);
        public static void Fatal(object message, bool newline = true, bool timeStamp = true) =>
            Write(message == null ? "null" : message.ToString(), ConsoleColor.Red, ConsoleColor.White, newline, timeStamp ? stampedError : Console.Error);

        private static void Write(string message, ConsoleColor f, ConsoleColor b, bool newline, TextWriter writer)
        {
            lock (writeLock)
            {
                string read = null;
                if (reading && newline) read = PeekLine();
                if (overwrite) ClearLine();
                overwrite = false;
                ConsoleColor f1 = Console.ForegroundColor, b1 = Console.BackgroundColor;
                Console.ForegroundColor = f;
                Console.BackgroundColor = b;
                writer.Write(message);
                readStart_y = (short)Console.CursorTop;
                if (newline)
                {
                    writer.WriteLine();
                    Console.ForegroundColor = f1;
                    Console.BackgroundColor = b1;
                    readStart_x = 0;
                    ++readStart_y;
                    OnNewLine?.Invoke();
                    readStart_y = (short)Console.CursorTop;
                    readStart_x = (short)Console.CursorLeft;
                    if (reading) Console.Out.Write(read);
                }
                else
                {
                    readStart_x = (short)Console.CursorLeft;
                    Console.ForegroundColor = f1;
                    Console.BackgroundColor = b1;
                }
            }
        }

        // Read currently entered keyboard input (even if enter hasn't been pressed)
        public static string PeekLine()
        {
            IEnumerator<string> e = ConsoleReader.ReadFromBuffer(readStart_x, readStart_y, (short)((short)Console.BufferWidth - readStart_x), 1).GetEnumerator();
            e.MoveNext();
            Console.SetCursorPosition(readStart_x, readStart_y);
            StringBuilder builder = new StringBuilder(e.Current);
            while (builder.Length > 0 && builder[builder.Length - 1] == ' ') --builder.Length;
            return builder.ToString();
        }

        public static void Clear()
        {
            string peek = PeekLine();
            Console.Clear();
            readStart_x = 0;
            readStart_y = 0;
            OnNewLine?.Invoke();
            Console.Out.Write(peek);
        }

        public static string ReadLine()
        {
            if (reading) throw new SystemException("Cannot concurrently read line!");
            reading = true;
            readStart_x = (short)Console.CursorLeft;
            readStart_y = (short)Console.CursorTop;
            string s = Console.ReadLine();

            reading = false;
            overwrite = false;
            OnNewLine?.Invoke();
            return s;
        }

        private static void ClearLine(int from = 0)
        {
            from = Math.Min(from, Console.WindowWidth);
            int y = Console.CursorTop;
            Console.SetCursorPosition(from, y);
            char[] msg = new char[Console.WindowWidth - from];
            for (int i = 0; i < msg.Length; ++i) msg[i] = ' ';
            Console.Write(new string(msg));
            Console.SetCursorPosition(from, y);
        }
    }
}
