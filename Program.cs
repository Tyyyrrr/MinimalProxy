using System;
using System.Net;
using System.Net.Http;

namespace MinimalProxy
{
    class Program
    {
        
        static void Main(string[] _)
        {

            using var listener = new HttpListener();

            Console.WriteLine($"{listener.DefaultServiceNames.Count} DEFAULT SERVICES: ");

            foreach (var service in listener.DefaultServiceNames)
                Console.WriteLine(service);


            Console.WriteLine($"{listener.Prefixes.Count} PREFIXES: ");

            foreach (var prefix in listener.Prefixes)
                Console.WriteLine(prefix);
            
        }

    }
}
