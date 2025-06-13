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


    bool ShouldTranslate() => _dllPath.Length > 0;

    bool ShouldTerminate() => _cts.IsCancellationRequested;



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

        if (server.ShouldTerminate())
            return;


        HttpListenerContext context = server._httpListener.EndGetContext(result);

        // refuse to pass new request further if there are too many concurrent tasks running
        if (Interlocked.Increment(ref server._concurrentRequestsCount) > server._maxConcurrentRequests)
        {
#if DEBUG
            Console.WriteLine("Request rejected.");
#endif

            server.FinalizeProcessingRequest(context);

            _ = server._httpListener.BeginGetContext(OnClientRequest, server);

            return;
        }


        // otherwise start processing the current and listening to the next client
#if DEBUG
        Console.WriteLine($"Pending requests: {server._concurrentRequestsCount}/{server._maxConcurrentRequests}");
#endif
        _ = server.HandleClientAsync(context);

        _ = server._httpListener.BeginGetContext(OnClientRequest, server);
    }


    async Task HandleClientAsync(HttpListenerContext context)
    {
        if(ShouldTranslate())
            await HandleClientWithTranslationAsync(context);
        else
            await HandleClientWithoutTranslationAsync(context);

        context.Response.ContentEncoding = context.Request.ContentEncoding;

        FinalizeProcessingRequest(context);
    }


    async Task HandleClientWithTranslationAsync(HttpListenerContext context)
    {
        if(!_translator.TryTranslateRequest(context.Request, out var translatedRequest))
        {
#if DEBUG
            Console.WriteLine("Failed to translate request.");
#endif
            return;
        }

        HttpResponseMessage response;

        try
        {
            response = await GetResponseAsync(translatedRequest);
        }
        catch (Exception ex)
        {
#if DEBUG
            if(!ShouldTerminate())
                Console.WriteLine("Target server unreachable or not responding: " + ex.Message);
#endif
            return;
        }
        finally
        {
            translatedRequest.Dispose();
        }


        if(!_translator.TryTranslateResponse(response, out var translatedResponse))
        {
#if DEBUG
            Console.WriteLine("Failed to translate response.");
#endif
            response.Dispose();
            return;
        }


        try
        {
            await SendResponseAsync(translatedResponse, context.Response);
        }
        catch (Exception ex)
        {
#if DEBUG
            if (!ShouldTerminate())
                Console.WriteLine("Failed to respond to the client: " + ex.Message);
#endif
            return;
        }
        finally
        {
            response.Dispose();
        }

        translatedResponse.Dispose();
    }


    async Task HandleClientWithoutTranslationAsync(HttpListenerContext context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Parse(context.Request.HttpMethod), _httpClient.BaseAddress);

        if (context.Request.HasEntityBody)
        {
            request.Content = new StreamContent(context.Request.InputStream);

            if (!request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.Headers["Content-Type"]))
            {
#if DEBUG
                Console.WriteLine("Failed to add content-type header");
#endif
                request.Dispose();
                return;
            }
        }

        HttpResponseMessage response;

        try
        {
            response = await GetResponseAsync(request);
        }
        catch (Exception ex)
        {
#if DEBUG
            if (!ShouldTerminate())
                Console.WriteLine("Target server unreachable or not responding: " + ex.Message);
#endif
            return;
        }
        finally
        {
            request.Dispose();
        }

        try
        {
            await SendResponseAsync(response, context.Response);
        }
        catch (Exception ex)
        {
#if DEBUG
            if (!ShouldTerminate())
                Console.WriteLine("Failed to respond to the client: " + ex.Message);
#endif
            return;
        }
        finally
        {
            response.Dispose();
        }
    }


    async Task<HttpResponseMessage> GetResponseAsync(HttpRequestMessage request)
    {
        HttpResponseMessage response;

        if (request.Method == HttpMethod.Get)
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, _cts.Token);

        else if (request.Method == HttpMethod.Post)
            response = await _httpClient.PostAsync(_httpClient.BaseAddress, request.Content, _cts.Token);

        else if (request.Method == HttpMethod.Put)
            response = await _httpClient.PutAsync(_httpClient.BaseAddress, request.Content, _cts.Token);

        else if (request.Method == HttpMethod.Delete)
            response = await _httpClient.DeleteAsync(_httpClient.BaseAddress, _cts.Token);

        else if (request.Method == HttpMethod.Patch)
            response = await _httpClient.PatchAsync(_httpClient.BaseAddress, request.Content, _cts.Token);

        else if (
            request.Method == HttpMethod.Head ||
            request.Method == HttpMethod.Options ||
            request.Method == HttpMethod.Trace
            )
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
        

        else throw new HttpRequestException($"HTTP Method not supported: {request.Method}");
                                        
        return response;
    }

    async Task SendResponseAsync(HttpResponseMessage response, HttpListenerResponse destination)
    {
        response.EnsureSuccessStatusCode();

        destination.StatusCode = (int)response.StatusCode;

        if(response.Content.Headers.ContentLength.HasValue)
            destination.ContentLength64 = response.Content.Headers.ContentLength.Value;

        if (response.Content.Headers.ContentType != null)
            destination.ContentType = response.Content.Headers.ContentType.ToString();

        if (response.Content.Headers.ContentEncoding != null)
        {
            foreach (var encoding in response.Content.Headers.ContentEncoding)
                destination.AddHeader("Content-Encoding", encoding);
        }

        await (await response.Content.ReadAsStreamAsync(_cts.Token)).CopyToAsync(destination.OutputStream, _cts.Token);
    }


    void FinalizeProcessingRequest(HttpListenerContext context)
    {
        if (ShouldTerminate())
            return;

        context.Response.Close();

        _ = Interlocked.Decrement(ref _concurrentRequestsCount);

#if DEBUG
        Console.WriteLine($"Pending requests: { _concurrentRequestsCount}/{_maxConcurrentRequests}");
#endif
    }

}
