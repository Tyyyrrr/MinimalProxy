using System.Reflection;

namespace TranslationInterface
{
    public static class TranslatorImplementationProvider
    {
        private static string _lastErrorMessage = "Everything is fine";

        public static string GetLastErrorMessage() => _lastErrorMessage;


        public static bool TryGetTranslatorInstance(Assembly assembly, ref ITranslator instance)
        {
            var type = assembly.GetTypes().FirstOrDefault(t=>typeof(ITranslator).IsAssignableFrom(t));

            if (type == null)
            {
                _lastErrorMessage = $"Type implementing {nameof(ITranslator)} interface has not been found in the assembly";
                return false;
            }

            object? inst;
            try
            {
                inst = Activator.CreateInstance(type);
            }
            catch(Exception ex)
            {
                _lastErrorMessage = $"{ex.GetType()} ({ex.Message})";
                return false;
            }

            if (inst == null)
            {
                _lastErrorMessage = $"Activator returned null instance of type {type}";
                return false;
            }

            instance = (ITranslator)inst;

            return true;
        }


    }
}
