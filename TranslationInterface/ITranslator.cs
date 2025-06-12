namespace TranslationInterface;

using System.Text;

public interface ITranslator
{
    /// <param name="encoding"><see cref="Encoding"/> of the <see href="input"/></param>
    /// <returns><see href="true"/> if written to <see href="output"/></returns>
    bool TryTranslateRequest(Stream input, Stream output, Encoding encoding);

    /// <param name="encoding"><see cref="Encoding"/> for the <see href="output"/></param>
    /// <returns><see href="true"/> if written to <see href="output"/></returns>
    bool TryTranslateResponse(Stream input, Stream output, Encoding encoding);
}