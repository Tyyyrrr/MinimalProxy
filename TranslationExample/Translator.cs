using System.IO;
using System.Text;
using TranslationInterface;

namespace TranslationExample;

public class Translator : ITranslator
{
    public bool TryTranslateRequest(Stream input, Stream output, Encoding encoding)
    {
        throw new System.NotImplementedException();
    }

    public bool TryTranslateResponse(Stream input, Stream output, Encoding encoding)
    {
        throw new System.NotImplementedException();
    }
}
