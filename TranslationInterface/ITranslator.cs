namespace TranslationInterface;

using System.Text;

public interface ITranslator
{
    bool TryTranslateRequest(Stream input, Stream output, Encoding encoding);

    bool TryTranslateResponse(Stream input, Stream output, Encoding encoding);
}