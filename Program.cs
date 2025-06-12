using System;

namespace MinimalProxy
{
    class Program
    {
        
        static void Main(string[] args)
        {
            Server server;

            try
            {
                server = new Server(args);
            }
            catch(Exception ex)
            {
                string str = "Failed to setup server: " + ex.Message;

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

            Console.Read();

            if(server.IsRunning)
                server.Dispose();
        }
    }
}
