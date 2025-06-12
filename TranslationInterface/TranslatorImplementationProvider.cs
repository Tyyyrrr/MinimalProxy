using System.Reflection;

namespace TranslationInterface
{
    public static class TranslatorImplementationProvider
    {
        public static bool TryGetTranslatorInstance(Assembly assembly, ref ITranslator instance)
        {
            var type = assembly.GetTypes().FirstOrDefault(t=>typeof(ITranslator).IsAssignableFrom(t));

            if (type == null) 
                return false;

            var inst = Activator.CreateInstance(type);

            if(inst == null)
                return false;

            instance = (ITranslator)inst;
            return true;
        }
    }
}
