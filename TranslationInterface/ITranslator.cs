namespace TranslationInterface;

using System.Net;

/// <summary>
/// Provides translation methods for HTTP messages within a proxy context.
/// </summary>
/// <remarks>
/// Note: This interface does not provide access to <see cref="HttpListenerContext"/>.
/// When constructing responses, the <see cref="Uri"/> parameter should remain empty.
/// </remarks>
public interface ITranslator
{
    /// <summary>
    /// Converts an incoming <see cref="HttpListenerRequest"/> to a <see cref="HttpRequestMessage"/> for the target server.
    /// </summary>
    /// <param name="originalRequest">The client’s original HTTP request.</param>
    /// <param name="translatedRequest">The translated request to send to the upstream server.</param>
    /// <returns><c>true</c> if the translation succeeded; otherwise, <c>false</c>.</returns>
    bool TryTranslateRequest(HttpListenerRequest originalRequest, out HttpRequestMessage translatedRequest);

    /// <summary>
    /// Converts a <see cref="HttpResponseMessage"/> from the upstream server to a format suitable for the client.
    /// </summary>
    /// <param name="originalResponse">The original HTTP response from the server.</param>
    /// <param name="translatedResponse">The translated response to return to the client.</param>
    /// <returns><c>true</c> if the translation succeeded; otherwise, <c>false</c>.</returns>
    bool TryTranslateResponse(HttpResponseMessage originalResponse, out HttpResponseMessage translatedResponse);
}