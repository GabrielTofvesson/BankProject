﻿using System;
using System.IO;
using System.Runtime.InteropServices;
using Client;
using Client.ConsoleForms;
using Client.ConsoleForms.Parameters;
using Common;

namespace ConsoleForms
{
    class Program
    {
        public static TextWriter DebugStream = new DebugAdapterWriter();
        private static ConsoleController controller = ConsoleController.singleton;

        static void Main(string[] args)
        {
            // Set up timestamps in debug output
            DebugStream = new TimeStampWriter(DebugStream, "HH:mm:ss.fff");

            byte[] serialized;

            using (BinaryCollector collector = new BinaryCollector(1))
            {
                collector.Push(5f);
                serialized = collector.ToArray();
            }


            Padding p = new AbsolutePadding(2, 2, 1, 1);

            Console.CursorVisible = false;
            Console.Title = "Tofvesson Enterprises"; // Set console title

            // Start with the networking context
            ContextManager manager = new ContextManager();

            manager.LoadContext(new NetContext(manager));

            // Start input listener loop. Graphics happen here too (triggered by keystrokes)
            ConsoleController.KeyEvent info = new ConsoleController.KeyEvent(default(ConsoleKeyInfo))
            {
                ValidEvent = false
            };
            bool first = true;
            do
            {
                if (first) first = false;
                else info = controller.ReadKey();

                bool b = manager.Update(info), haskey = false;
                while (b)
                {
                    System.Threading.Thread.Sleep(25);
                    haskey = _kbhit() != 0;
                    if (haskey) info = controller.ReadKey(false);
                    b = manager.Update(info, haskey);
                    controller.Draw();
                }
            } while (!info.ValidEvent ||  info.Event.Key != ConsoleKey.Escape);
        }

        // Detects if a key has been hit without blocking
        [DllImport("msvcrt")]
        public static extern int _kbhit();
    }
}