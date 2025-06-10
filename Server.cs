using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace MinimalProxy
{
    internal class Server : IDisposable
    {

        private readonly HttpListener _httpListener;

        private volatile bool _isRunning;

        private readonly struct EndPoint
        {

            public string Host { get; init; }
            public int Port { get; init; }
            public bool SSL { get; init; }


            public bool IsValid => Host != string.Empty && Port > 0;

            private enum Arg { HELP, HOST, PORT, PATH, SSL, COUNT };

            private const string _helpContents = "Configuration options:\n-host [string]\n-port [integer]\n-ssl [t/f/y/n/yes/no/enable/disable/true/false] [optional] (default: false)";
            
            const string _boolReplacePatternTrue = @"(?i)\b(y|yes|t|true|enable)\b";
            const string _boolReplacePatternFalse = @"(?i)\b(n|no|f|false|disable)\b";

            static readonly Dictionary<Arg, string> _regexPatterns = new(){
                {Arg.HELP, @"^-{1,2}help(\s|$)" },
                {Arg.HOST, @"(?!.*-host\s.*-host\s)-host\s(?<HOST>\d{1,3}(\.\d{1,3}){3})" },
                {Arg.PORT, @"(?!.*-port\s.*-port\s)-port\s(?<PORT>\d+)" },
                {Arg.SSL, @"(?!.*-ssl\s.*-ssl\s)-ssl\s(?<SSL>(?i)\b(y|yes|t|true|enable|n|no|f|false|disable)\b)" }
            };

            public static EndPoint FromArgs(string[] args)
            {
                if (args.Length == 0)
                    args = ["-host 127.0.0.1", "-port 8799", "-ssl t"];

                    //throw new ArgumentException(nameof(MinimalProxy) + " can not run without arguments.");

                string argStr = string.Join(" ", args);


                argStr = Regex.Replace(argStr, _boolReplacePatternTrue, "true");
                argStr = Regex.Replace(argStr, _boolReplacePatternFalse, "false");

                if(Regex.Match(argStr, _regexPatterns[Arg.HELP]).Success)
                {
                    Console.WriteLine(GetHelp());
                    return new();
                }

                string host = Regex.Match(argStr, _regexPatterns[Arg.HOST]).Groups[Arg.HOST.ToString()].Value;
                ArgumentException.ThrowIfNullOrEmpty(host);

                int port = int.TryParse(Regex.Match(argStr, _regexPatterns[Arg.PORT]).Groups[Arg.PORT.ToString()].Value, out port) ? port : -1;

                bool ssl = bool.TryParse(Regex.Match(argStr, _regexPatterns[Arg.SSL]).Groups[Arg.SSL.ToString()].Value, out ssl) && ssl;                

                var ep = new EndPoint()
                {
                    Host = host,
                    Port = port,
                    SSL = ssl,
                };

                if(!ep.IsValid) throw new ArgumentException("Invalid endpoint configuration: " + ep.ToString());
                
                return ep;
            }


            public static string GetHelp() => _helpContents;

            public override string ToString()
            {
                string output = SSL ? "https://" : "http://";
                output += Host + ":" + Port.ToString() + "/";
                return output;
            }

        }


        static readonly AsyncCallback _asyncCallback = new(OnClientRequest);

        public Server(string[] args)
        {
            EndPoint ep;
            try
            {
                ep = EndPoint.FromArgs(args);
            }
            catch(ArgumentException e)
            {
                Console.WriteLine("Failed to setup endpoint for httplistener: " + e.Message + "\nsee --help\n");
                return;
            }


            if (!ep.IsValid) return;

            Console.WriteLine("Setting server endpoint to: " + ep.ToString());

            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add(ep.ToString());
                _httpListener.Start();
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }

            if (_httpListener.IsListening)
                Console.WriteLine("Started listening...");
            else
            {
                Console.WriteLine("Failed to start listening.");
                return;
            }

            _isRunning = true;

            _ = _httpListener.BeginGetContext(_asyncCallback, this);
        }

        public void Dispose()
        {
            _isRunning = false;
            _httpListener?.Abort();
            Console.WriteLine("Server shutdown.");
        }

        
        static void OnClientRequest(IAsyncResult result)
        {
            var server = (Server)result.AsyncState;

            if (!server._isRunning)
                return;


            Console.WriteLine("Client request! Sending response...");

            var listener = server._httpListener;

            var context = listener.EndGetContext(result);
            var request = context.Request;
            var response = context.Response;
            var output = response.OutputStream;

            string responseString = "<HTML><BODY> Hello world!</BODY></HTML>";

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);

            response.ContentLength64 = buffer.Length;

            output.Write(buffer, 0, buffer.Length);

            response.Close();

            _ = listener.BeginGetContext(OnClientRequest, server);
        }
    }

}
