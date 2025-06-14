# TranslationExample

## 🧾 Description

This is a brief example of how to implement custom translation logic for client-server HTTP communication using a proxy layer.

The translator accepts a plain text POST message over **SSL**, embeds it into a JSON template, and forwards it to the Ollama generative API **without** encryption. 
Upon receiving the response, it extracts only the **actual** LLM-generated reply and sends it back to the client as plain text.

- [1. Build Core Dependencies](#1-build-core-dependencies)
- [2. Build the Example](#2-build-the-example)
- [3. Run Proxy with Args](#3-run-proxy-with-args)
- [4. Test with curl](#4-test-with-curl)
- [Note on Template File](#note-on-template-file)


## 🛠️ Prerequisites

- [Ollama](https://ollama.com/) installed and running on `localhost:11434` (default) 
- `gemma3` LLM available in your Ollama environment


---

## 🚀 Usage Guide

### 1. Build Core Dependencies

Follow the README instructions for:

- [MinimalProxy](../README.md)  
- [TranslationInterface](../TranslationInterface/README.md)

---

### 2. Build the example

```sh
cd ~path/to/your/directory/MinimalProxy/TranslationExample
dotnet build
```

---

### 3. Run proxy with args
```sh
cd ~path/to/your/directory/MinimalProxy
dotnet run -- -host 127.0.0.1 -port 8443 -url http://127.0.0.1:11434 -timeout 60 -lib TranslationExample/bin/Debug/net9.0/TranslationExample.dll
```

>If successful, your console log should now look like this:

```
MinimalProxy is up.

Configuration:
Server URL: https://127.0.0.1:8443/
Target URL: http://127.0.0.1:11434/api/generate
Maximum requests: 1
Timeout: 60s

User library: TranslationExample

```

---

### 4. Test with **curl**

>Run this command to see how translation works:

```sh
curl -X POST https://127.0.0.1:8443 -d "Hello!"
```

>If there is Ollama server reachable, you should get a similar response after few seconds:

```
Hello there! How can I help you today? 😊

Do you want to:

*   Chat about something?
*   Ask me a question?
*   Play a game?
*   Get some information?

Let me know what you're thinking!
```

>Alternatively, you can run proxy without specifying the dll to see what the non-translated response would look like.

```sh
dotnet run -- -host 127.0.0.1 -port 8443 -url http://127.0.0.1:11434 -timeout 60
```

>However, without translation you will also need to modify the curl command

```
curl -X POST https://127.0.0.1:8443 -H "Content-Type: application/json" -d "{\"model\":\"gemma3\",\"prompt\":\"Hello!\",\"stream\":false}"
```

>You'll now receive the full JSON response from Ollama.

---

#### Note on template file

If you're using a custom output directory, make sure the prompt_template.json file is located in the same folder as the .dll specified with -lib. 
Path resolution is always relative to the location of TranslationExample.dll.