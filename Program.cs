﻿using System;

namespace MinimalProxy
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using var server = new Server(args);

                Console.Read();
            }
            catch(Exception ex)
            {
                Environment.ExitCode = 1;

                string str = $"Failed to setup {nameof(MinimalProxy)}: " + ex.Message;

                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                    str += "\n" + ex.Message;
                }

                str += "\n\nsee: --help\n";

                Console.WriteLine(str);
                Console.Read();
                return;
            }
        }
    }
}
