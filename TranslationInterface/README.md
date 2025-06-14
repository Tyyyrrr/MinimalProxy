## Usage Guide

This library provides a single ITranslator interface with two methods for converting HttpRequest and HttpResponse objects at the proxy layer.
Keeping this project separate from the main repository ensures true loose coupling and avoids circular dependencies when loading custom .dll implementations into a proxy server process. 
It also enables more efficient development: you can build only the TranslationInterface.csproj and link against the output in your own projects—no need to compile the entire codebase.

```sh
cd ~/path/to/your/directory/MinimalProxy/TranslationInterface
dotnet build
cp bin/Debug/net9.0/TranslationInterface.dll ~/path/to/your/project
cp bin/Debug/net9.0/TranslationInterface.xml ~/path/to/your/project
```
>You can now link against the TranslationInterface.dll and implement the ITranslator interface in your custom class.