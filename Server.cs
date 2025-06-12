using System.Net;
using System.Text.RegularExpressions;
using System.Reflection;

using TranslationInterface;


namespace MinimalProxy;


internal class Server : IDisposable
{

    const int MaxConcurrentRequests = 3; // make it cmd arg

    private int _concurrentRequestsCount = 0;


    private readonly HttpListener _httpListener;

    private readonly HttpClient _httpClient;


    private readonly string _dllPath;

    private readonly ITranslator _translator;


    private readonly CancellationTokenSource _cts = new();


    private enum Arg { HELP, HOST, PORT, URL, SSL, LIB, COUNT };

    private const string _helpContents = "Configuration options:" +
        "\n-host [string] // proxy's IPV4 address" +
        "\n-port [integer] // listening port" +
        "\n-url [string]  // target url address" +
        "\n-ssl [t/f/y/n/yes/no/enable/disable/true/false] (optional, default: true)  // toggle encryption" +
        "\n-lib [string] (optional, default: empty) // absolute or relative path to a custom .dll library for translating requests." +
        "\n                                         // Requests will be passed as-is, if this argument is not provided."
        ;

    const string _boolReplacePatternTrue = @"(?i)\b(y|yes|t|true|enable)\b";
    const string _boolReplacePatternFalse = @"(?i)\b(n|no|f|false|disable)\b";


    static readonly Dictionary<Arg, string> _regexPatterns = new(){
            {Arg.HELP, @"^-{1,2}help(\s|$)" },
            {Arg.HOST, @"(?!.*-host\s.*-host\s)-host\s(?<HOST>\d{1,3}(\.\d{1,3}){3})" },
            {Arg.PORT, @"(?!.*-port\s.*-port\s)-port\s(?<PORT>\d+)" },
            {Arg.URL, @"(?!.*-url\s.*-url\s)-url\s(?<URL>(?i)\S*)" },
            {Arg.SSL, @"(?!.*-ssl\s.*-ssl\s)-ssl\s(?<SSL>(?i)\b(y|yes|t|true|enable|n|no|f|false|disable)\b)" },
            {Arg.LIB, @"(?!.*-lib\s.*-lib\s)-lib\s(?<LIB>(?i)\S*)" }
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
            throw new ArgumentException(nameof(MinimalProxy) + " can not run without arguments.");

        string argStr = string.Join(" ", args);

        if (Regex.Match(argStr, _regexPatterns[Arg.HELP]).Success)
        {
            Console.WriteLine(_helpContents);
            return;
        }

        argStr = Regex.Replace(argStr, _boolReplacePatternTrue, "true");
        argStr = Regex.Replace(argStr, _boolReplacePatternFalse, "false");

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

            else throw new FileNotFoundException(lib);
            

            if(!TranslatorImplementationProvider.TryGetTranslatorInstance(usrLib, ref _translator))
            {
                throw new Exception($"Failed to instantiate translator from user's library. Ensure there is a public class implementing {nameof(TranslationInterface)}.{nameof(ITranslator)}, and it has a parameterless constructor.");
            }

        }
        else _dllPath = string.Empty;


        string url = Regex.Match(argStr, _regexPatterns[Arg.URL]).Groups[Arg.URL.ToString()].Value;

        ArgumentException.ThrowIfNullOrEmpty(url);

        _httpClient = new()
        {
            BaseAddress = new Uri(url)
        };


        EndPoint ep;

        ep = EndPoint.FromArgs(argStr);

        if (!ep.IsValid) return;

        _httpListener = new();

        _httpListener.Prefixes.Add(ep.ToString());

        _httpListener.Start();


        Console.WriteLine(nameof(MinimalProxy) + " is up.");

        _ = _httpListener.BeginGetContext(OnClientRequest, this);
    }

    public void Dispose()
    {
        _cts.Cancel();

        _httpListener?.Abort();

        _httpClient?.Dispose();

        Console.WriteLine(nameof(MinimalProxy) + " is down.");
    }


    static void OnClientRequest(IAsyncResult result)
    {
        var server = (Server)result.AsyncState;

        if (server._cts.Token.IsCancellationRequested)
            return;

        HttpListenerContext context = server._httpListener.EndGetContext(result);


        // refuse if there are too many pending requests
        if (Interlocked.Increment(ref server._concurrentRequestsCount) > MaxConcurrentRequests)
        {
            Interlocked.Decrement(ref server._concurrentRequestsCount);

            byte[] refuseClientMessage = context.Request.ContentEncoding.GetBytes("Your request has been refused by the proxy, because there are already too many pending requests.");

            context.Response.ContentEncoding = context.Request.ContentEncoding;
            context.Response.ContentLength64 = refuseClientMessage.Length;

            context.Response.OutputStream.Write(refuseClientMessage, 0, refuseClientMessage.Length);

            context.Response.Close();

        }
        else // otherwise start processing the current and listening to the next client
        {
            _ = server.SendTargetRequest(context); // start processing this client asynchronously (fire and forget)

            _ = server._httpListener.BeginGetContext(OnClientRequest, server);
        }

    }


    async Task SendTargetRequest(HttpListenerContext context, CancellationToken token)
    {
        var targetRequest = new HttpRequestMessage(HttpMethod.Parse(context.Request.HttpMethod), _httpClient.BaseAddress);


        // if the path to .dll library has been specified, translate request before passing it to the target
        if (_dllPath != string.Empty)
        {
            using var ms = new MemoryStream();

            if (_translator.TryTranslateRequest(context.Request.InputStream, ms))
                targetRequest.Content = new StreamContent(ms);
        }
        // otherwise simply pass the request further
        else targetRequest.Content = new StreamContent(context.Request.InputStream);

        HttpResponseMessage targetResponse;
        try
        {
            targetResponse = await _httpClient.SendAsync(targetRequest, token);
        }
        catch (OperationCanceledException) { return; }

        if (targetResponse.IsSuccessStatusCode)
            _ = _translator.TryTranslateResponse(targetResponse.Content.ReadAsStream(), context.Response.OutputStream);

        context.Response.Close();

        if (Interlocked.Decrement(ref _concurrentRequestsCount) == MaxConcurrentRequests - 1)
            _ = _httpListener.BeginGetContext(OnClientRequest, this);
    }

}
