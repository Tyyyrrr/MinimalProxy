namespace TranslationInterface;

using System.Net;

/// <summary>
/// Defines methods for manipulating HTTP requests between client and server
/// </summary>
public interface ITranslator
{
    /// <param name="originalRequest">The request sent by the client</param>
    /// <param name="translatedRequest">The request to send to the server</param>
    /// <returns>True if conversion has succeeded.</returns>
    bool TryTranslateRequest(HttpListenerRequest originalRequest, out HttpRequestMessage translatedRequest);

    /// <param name="originalResponse">The response sent by the server</param>
    /// <param name="translatedResponse">The response to send to the client</param>
    /// <returns>True if conversion has succeeded.</returns>
    bool TryTranslateResponse(HttpResponseMessage originalResponse, out HttpResponseMessage translatedResponse);
}