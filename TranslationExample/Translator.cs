using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TranslationInterface;

namespace TranslationExample;


public class Translator : ITranslator
{
    const string _regexp = @"<USER_PROMPT>";
    const string _templateFileName = "prompt_template.json";

    static readonly string _jsonTemplate;
    static Translator()
    {
        _jsonTemplate = File.ReadAllText(_templateFileName);
    }

    public bool TryTranslateRequest(HttpListenerRequest originalRequest, out HttpRequestMessage translatedRequest)
    {
        translatedRequest = null;

        if(originalRequest.HttpMethod != HttpMethod.Post.Method)
            return false;

        using var sr = new StreamReader(originalRequest.InputStream, originalRequest.ContentEncoding, leaveOpen: true);

        string str = sr.ReadToEnd();

        str = Regex.Replace(_jsonTemplate, _regexp, str);
#if DEBUG
        Console.WriteLine("Produced json prompt for Ollama: " + str);
#endif
        translatedRequest = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = new StringContent(str)
        };

        return true;
    }

    public bool TryTranslateResponse(HttpResponseMessage originalResponse, out HttpResponseMessage translatedResponse)
    {
        translatedResponse = null;

        if (!originalResponse.IsSuccessStatusCode)
        {
#if DEBUG
            Console.WriteLine("Incorrect response. Reason: " + originalResponse.ReasonPhrase);
#endif
            return false;
        }

        using var sr = new StreamReader(originalResponse.Content.ReadAsStream(),Encoding.UTF8, leaveOpen: true);

        string str = sr.ReadToEnd();

#if DEBUG
        Console.WriteLine("Ollama says: " + str);
#endif

        using var doc = JsonDocument.Parse(str);

        if (doc.RootElement.TryGetProperty("response", out var value))
        {
            str = value.GetString();
        }
        else
        {
#if DEBUG
            Console.WriteLine("Incorrect response format");
#endif
            return false;
        }

        if(str == string.Empty)
        {
#if DEBUG
            Console.WriteLine("Response is empty.");
#endif
            return false;
        }

        translatedResponse = new(HttpStatusCode.OK)
        {
            Content = new StringContent(str)
        };

        return true;
    }


}
