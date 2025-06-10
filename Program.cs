using System;
using System.Net;
using System.Net.Http;

namespace MinimalProxy
{
    class Program
    {
        
        static void Main(string[] args)
        {
            using var server = new MinimalProxy.Server(args);

            Console.Read();
        }
    }
}
