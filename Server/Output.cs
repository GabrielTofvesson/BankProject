using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public static class Output
    {
        public static void WriteLine(string message, bool error = false)
        {
            if (error) Error(message);
            else Info(message);
        }
        public static void Write(string message, bool error)
        {
            if (error) Error(message, false);
            else Info(message, false);
        }
        public static void Positive(string message, bool newline = true) => Write(message, ConsoleColor.DarkGreen, ConsoleColor.Black, newline, Console.Out);
        public static void Info(string message, bool newline = true) => Write(message, ConsoleColor.Gray, ConsoleColor.Black, newline, Console.Out);
        public static void Error(string message, bool newline = true) => Write(message, ConsoleColor.Gray, ConsoleColor.Black, newline, Console.Out);
        public static void Fatal(string message, bool newline = true) => Write(message, ConsoleColor.Gray, ConsoleColor.Black, newline, Console.Error);

        private static void Write(string message, ConsoleColor f, ConsoleColor b, bool newline, TextWriter writer)
        {
            ConsoleColor f1 = Console.ForegroundColor, b1 = Console.BackgroundColor;
            Console.ForegroundColor = f;
            Console.BackgroundColor = b;
            writer.Write(message);
            if (newline) writer.WriteLine();
            Console.ForegroundColor = f1;
            Console.BackgroundColor = b1;
        }
    }
}
