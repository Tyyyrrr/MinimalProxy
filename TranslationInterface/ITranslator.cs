namespace TranslationInterface;

using System.Text;

public interface ITranslator
{
    bool TryTranslateRequest(string httpMethod, Stream input, Stream output, Encoding encoding);

    bool TryTranslateResponse(string httpMethod, Stream input, Stream output, Encoding encoding);
}