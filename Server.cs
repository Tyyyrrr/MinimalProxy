using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Reflection;

using TranslationInterface;


namespace MinimalProxy;


internal sealed class Server : IDisposable
{

    private readonly int _maxConcurrentRequests;

    private int _concurrentRequestsCount = 0;


    private readonly HttpListener _httpListener;

#if DEBUG
    private readonly EndPoint _ep;      // need to be cached only for debugging
    private readonly Assembly _usrLib;  //
#endif


    private readonly HttpClient _httpClient;


    private readonly string _dllPath;

    private readonly ITranslator _translator;

    private readonly CancellationTokenSource _cts = new();


    private enum Arg { HELP, HOST, PORT, URL, SSL, LIB, LIMIT, TIMEOUT, COUNT };

    private const string _helpContents = "\nConfiguration options:" +
        "\n\n-host [string] // proxy's IPV4 address" +
        "\n\n-port [integer] // listening port" +
        "\n\n-url [string]  // target url address" +
        "\n\n-ssl [t/f/y/n/yes/no/enable/disable/true/false] (optional, default: true)  // toggle encryption" +
        "\n\n-lib [string] (optional, default: empty) // absolute or relative path to a custom .dll library for translating requests." +
        "\n                                         // Requests will be passed as-is, if this argument is not provided." +
        "\n\n-limit [integer] (optional, default: 1) // Maximum concurrent client requests that can be processed at the same time" +
        "\n\n-timeout [integer] (optional, default: infinite) // Time (in seconds) to wait for the response from target server."
        ;

    const string _boolReplacePatternTrue = @"(?i)\b(y|yes|t|true|enable)\b";
    const string _boolReplacePatternFalse = @"(?i)\b(n|no|f|false|disable)\b";


    static readonly Dictionary<Arg, string> _regexPatterns = new(){
            {Arg.HELP, @"^-{1,2}help(\s|$)" },
            {Arg.HOST, @"(?!.*-host\s.*-host\s)-host\s(?<HOST>\d{1,3}(\.\d{1,3}){3})" },
            {Arg.PORT, @"(?!.*-port\s.*-port\s)-port\s(?<PORT>\d+)" },
            {Arg.URL, @"(?!.*-url\s.*-url\s)-url\s(?<URL>(?i)\S*)" },
            {Arg.SSL, @"(?!.*-ssl\s.*-ssl\s)-ssl\s(?<SSL>(?i)\b(y|yes|t|true|enable|n|no|f|false|disable)\b)" },
            {Arg.LIB, @"(?!.*-lib\s.*-lib\s)-lib\s(?<LIB>(?i)\S*)" },
            {Arg.LIMIT, @"(?!.*-limit\s.*-limit\s)-limit\s(?<LIMIT>\d+)" },
            {Arg.TIMEOUT, @"(?!.*-timeout\s.*-timeout\s)-timeout\s(?<TIMEOUT>\d+)" }
        };



    private readonly struct EndPoint
    {
        public string Host { get; init; }
        public int Port { get; init; }
        public bool SSL { get; init; }

        public bool IsValid => Host != string.Empty && Port > 0;


        public static EndPoint FromArgs(string argStr)
        {
            string host = Regex.Match(argStr, _regexPatterns[Arg.HOST]).Groups[Arg.HOST.ToString()].Value;

            ArgumentException.ThrowIfNullOrEmpty(host);

            int port = int.TryParse(Regex.Match(argStr, _regexPatterns[Arg.PORT]).Groups[Arg.PORT.ToString()].Value, out port) ? port : -1;

            bool ssl = !bool.TryParse(Regex.Match(argStr, _regexPatterns[Arg.SSL]).Groups[Arg.SSL.ToString()].Value, out ssl) || ssl;

            var ep = new EndPoint()
            {
                Host = host,
                Port = port,
                SSL = ssl,
            };

            if(!ep.IsValid) throw new ArgumentException("Invalid endpoint configuration: " + ep.ToString());
            
            return ep;
        }


        public override string ToString()
        {
            string output = SSL ? "https://" : "http://";
            output += Host + ":" + Port.ToString() + "/";
            return output;
        }

    }


    public Server(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException(nameof(Server) + " can not run without arguments.");

        string argStr = string.Join(" ", args);

        // handle --help command if present
        if (Regex.Match(argStr, _regexPatterns[Arg.HELP]).Success)
        {
            Console.WriteLine(_helpContents);
            argStr = Console.ReadLine();
        }


        argStr = Regex.Replace(argStr, _boolReplacePatternTrue, "true");
        argStr = Regex.Replace(argStr, _boolReplacePatternFalse, "false");


        // parse the -limit command if present
        string limit = Regex.Match(argStr, _regexPatterns[Arg.LIMIT]).Groups[Arg.LIMIT.ToString()].Value;
        
        if(limit != string.Empty)
        {
            if(!int.TryParse(limit, out _maxConcurrentRequests) || _maxConcurrentRequests < 1)
                throw new ArgumentException("Maximum concurrent requests limit should be a positive integer");
        }
        else { _maxConcurrentRequests = 1; }        


        // parse the -timeout command if present
        string timeout = Regex.Match(argStr, _regexPatterns[Arg.TIMEOUT]).Groups[Arg.TIMEOUT.ToString()].Value;

        TimeSpan timeoutSpan = Timeout.InfiniteTimeSpan;

        if (timeout != string.Empty)
        {
            if (!int.TryParse(timeout, out int timeoutInt) || timeoutInt < 1)
                throw new ArgumentException("Timeout should be a positive integer");

            timeoutSpan = TimeSpan.FromSeconds(timeoutInt);
        }


        // load dll from the path specified in -lib command if present
        string lib = Regex.Match(argStr, _regexPatterns[Arg.LIB]).Groups[Arg.LIB.ToString()].Value;

        if (lib != string.Empty)
        {
            Console.WriteLine("Loading dll: " + lib);

            var relativePath = Path.Combine(Directory.GetCurrentDirectory(), lib);

            Assembly usrLib;

            if (File.Exists(relativePath)) 
                usrLib = Assembly.LoadFile(relativePath);

            else if (File.Exists(lib)) 
                usrLib = Assembly.LoadFile(lib);

            else throw new FileNotFoundException("File not found at path: " + lib);
            

            if(!TranslatorImplementationProvider.TryGetTranslatorInstance(usrLib, ref _translator))
            {
                throw new Exception($"Failed to instantiate translator from user's library. " + TranslatorImplementationProvider.GetLastErrorMessage());
            }

#if DEBUG
            _usrLib = usrLib;
#endif

        }
        else _dllPath = string.Empty;


        // HttpClient setup
        string url = Regex.Match(argStr, _regexPatterns[Arg.URL]).Groups[Arg.URL.ToString()].Value;

        ArgumentException.ThrowIfNullOrEmpty(url);

        _httpClient = new()
        {
            BaseAddress = new Uri(url),
            Timeout = timeoutSpan
        };


        // HttpListener setup

        EndPoint ep = EndPoint.FromArgs(argStr);

        if (!ep.IsValid) return;

#if DEBUG
        _ep = ep;
#endif


        _httpListener = new();

        _httpListener.Prefixes.Add(ep.ToString());

        _httpListener.Start();


        Console.WriteLine($"\n{nameof(MinimalProxy)} is up.\n");
#if DEBUG
        Console.WriteLine(GetConfigString());
#endif
        // start listening
        _ = _httpListener.BeginGetContext(OnClientRequest, this);
    }

#if DEBUG
    string GetConfigString()
    {
        var sb = new StringBuilder();

        string secTimeoutStr = _httpClient.Timeout == Timeout.InfiniteTimeSpan ? "Infinite" : _httpClient.Timeout.Seconds.ToString() + "s";

        sb.
            AppendLine("Configuration:").
            Append("Server URL: ").AppendLine(_ep.ToString()).
            Append("Target URL: ").AppendLine(_httpClient.BaseAddress.OriginalString).
            Append("Maximum requests: ").AppendLine(_maxConcurrentRequests.ToString()).
            Append("Timeout: ").AppendLine(secTimeoutStr);

        if (_dllPath != string.Empty)
            sb.AppendLine().Append("User library: " + _usrLib.GetName().Name);


        return sb.ToString();
    }
#endif


    public void Dispose()
    {
        _cts.Cancel();

        _httpListener?.Abort();

        _httpClient?.Dispose();

        Console.WriteLine($"\n{nameof(MinimalProxy)} is down.\n");
    }


    static void OnClientRequest(IAsyncResult result)
    {
        var server = (Server)result.AsyncState;

        if (server._cts.Token.IsCancellationRequested)
            return;

        HttpListenerContext context = server._httpListener.EndGetContext(result);

        context.Response.ContentType = context.Request.ContentType;
        context.Response.ContentEncoding = context.Request.ContentEncoding;

        // refuse to pass new request further if there are too many concurrent tasks running
        if (Interlocked.Increment(ref server._concurrentRequestsCount) > server._maxConcurrentRequests)
        {
#if DEBUG
            Console.WriteLine("Request rejected.");
#endif
            byte[] refuseClientMessage = context.Request.ContentEncoding.GetBytes($"{nameof(MinimalProxy)} refused to process your data, because there are already too many pending requests.");

            context.Response.ContentLength64 = refuseClientMessage.Length;

            context.Response.OutputStream.Write(refuseClientMessage, 0, refuseClientMessage.Length);

            server.FinalizeProcessingRequest(context);

            _ = server._httpListener.BeginGetContext(OnClientRequest, server);

            return;
        }


        // otherwise start processing the current and listening to the next client

#if DEBUG
        Console.WriteLine($"Pending requests: {server._concurrentRequestsCount}/{server._maxConcurrentRequests}");
#endif
        _ = server.SendTargetRequest(context); // async, fire and forget

        _ = server._httpListener.BeginGetContext(OnClientRequest, server);
    }


    async Task SendTargetRequest(HttpListenerContext context)
    {
        using var targetRequest = new HttpRequestMessage(HttpMethod.Parse(context.Request.HttpMethod), _httpClient.BaseAddress);

        // if the path to .dll library has been specified, translate 'POST' requests before passing them to the target
        if (_dllPath != string.Empty && context.Request.HttpMethod == HttpMethod.Post.Method)
        {
            using var ms = new MemoryStream();

            // try to translate data sent by the client
            if (_translator.TryTranslateRequest(context.Request.InputStream, ms, context.Request.ContentEncoding))
                targetRequest.Content = new StreamContent(ms);
            
            else // on failure: notify the client and early return
            {
                targetRequest.Dispose();
#if DEBUG
                Console.WriteLine("Failed to translate request");
#endif
                byte[] errMsg = context.Request.ContentEncoding.GetBytes($"{nameof(MinimalProxy)} failed to translate your request.");

                context.Response.ContentLength64 = errMsg.Length;

                try { await context.Response.OutputStream.WriteAsync(errMsg, _cts.Token); }
                catch (OperationCanceledException) { }

                FinalizeProcessingRequest(context);

                return;
            }
        }
        // otherwise simply pass the request further
        else targetRequest.Content = new StreamContent(context.Request.InputStream);


        HttpResponseMessage targetResponse;
        try
        {
            targetResponse = await _httpClient.SendAsync(targetRequest, HttpCompletionOption.ResponseContentRead, _cts.Token);
        }
        catch (Exception ex) 
        {
            if (ex is OperationCanceledException && _cts.IsCancellationRequested) // this state indicates that the proxy server is shutting down
                return;


            Console.WriteLine($"Target not responding or unreachable. {ex.Message}");

            byte[] errMsg = context.Request.ContentEncoding.GetBytes($"{nameof(MinimalProxy)} could not reach the target url.");

            context.Response.ContentLength64 = errMsg.Length;

            try { await context.Response.OutputStream.WriteAsync(errMsg, _cts.Token); }
            catch (Exception) { }

            FinalizeProcessingRequest(context); 
            return;
        }

        try
        {
            await ProcessTargetResponse(context, targetResponse);
        }
        catch(Exception ex)
        {
            if (ex is OperationCanceledException && _cts.IsCancellationRequested)
                return;

            Console.WriteLine($"Processing failed. {ex.Message}");

            byte[] errMsg = context.Request.ContentEncoding.GetBytes($"{nameof(MinimalProxy)} failed to provide response.");

            context.Response.ContentLength64 = errMsg.Length;

            try { await context.Response.OutputStream.WriteAsync(errMsg, _cts.Token); }
            catch (Exception) { }
        }

        FinalizeProcessingRequest(context);
    }


    async Task ProcessTargetResponse(HttpListenerContext context, HttpResponseMessage response)
    {
        if (_cts.IsCancellationRequested)
            return;


        if (!response.IsSuccessStatusCode)
        {
#if DEBUG
            Console.WriteLine("Target failed to respond. Reason: " + response.ReasonPhrase);
#endif
            byte[] errMsg = context.Request.ContentEncoding.GetBytes($"{nameof(MinimalProxy)} failed to get the response from the target. Reason: {response.ReasonPhrase}");

            response.Dispose();

            context.Response.ContentLength64 = errMsg.Length;

            await context.Response.OutputStream.WriteAsync(errMsg, _cts.Token);

            return;
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(_cts.Token);

        response.Dispose();

        if (_dllPath != string.Empty && context.Request.HttpMethod == HttpMethod.Post.Method)
        {
            if (!_translator.TryTranslateResponse(responseStream, context.Response.OutputStream, context.Request.ContentEncoding))
            {
#if DEBUG
                Console.WriteLine("Failed to translate response");
#endif
                byte[] errMsg = context.Request.ContentEncoding.GetBytes($"{nameof(MinimalProxy)} failed to translate target's response to your request.");

                context.Response.ContentLength64 = errMsg.Length;

                await context.Response.OutputStream.WriteAsync(errMsg, _cts.Token);
            }
        }
        else
        {
            await responseStream.CopyToAsync(context.Response.OutputStream, _cts.Token);
        }
    }

    void FinalizeProcessingRequest(HttpListenerContext context)
    {
        if (_cts.IsCancellationRequested)
            return;

        context.Response.Close();

        _ = Interlocked.Decrement(ref _concurrentRequestsCount);

#if DEBUG
        Console.WriteLine($"Pending requests: { _concurrentRequestsCount}/{_maxConcurrentRequests}");
#endif
    }

}
