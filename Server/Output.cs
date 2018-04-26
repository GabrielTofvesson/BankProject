using Common;
using System;
using System.IO;

namespace Server
{
    public static class Output
    {
        // Fancy timestamped output
        private static readonly TextWriter stampedError = new TimeStampWriter(Console.Error, "HH:mm:ss.fff");
        private static readonly TextWriter stampedOutput = new TimeStampWriter(Console.Out, "HH:mm:ss.fff");
        private static bool overwrite = false;

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
            if (overwrite) ClearLine();
            overwrite = false;
            ConsoleColor f1 = Console.ForegroundColor, b1 = Console.BackgroundColor;
            Console.ForegroundColor = f;
            Console.BackgroundColor = b;
            writer.Write(message);
            if (newline)
            {
                writer.WriteLine();
                OnNewLine?.Invoke();
            }
            Console.ForegroundColor = f1;
            Console.BackgroundColor = b1;
        }

        public static string ReadLine()
        {
            string s = Console.ReadLine();
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
