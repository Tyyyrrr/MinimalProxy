<h1 align="center">Minimal Proxy</h1>
<p>
  <img alt="Version" src="https://img.shields.io/badge/version-0.1.0--alpha-blue.svg?cacheSeconds=2592000" />
  <a href="LICENSE.md" target="_blank">
    <img alt="License: MIT" src="https://img.shields.io/badge/License-MIT-yellow.svg" />
  </a>
</p>

> Simplistic reverse-proxy application created as side-effect of local development.
At this point there are no plans for adding new features, but feel free to post issues.

## Features
- Add or remove SSL encryption from client-server traffic
- Control the timeout of obtaining the response from server
- Control how many client requests can be processed simultaneously
- Process requests/responses through additional layer by using custom .dll translation library and the public interface.


## Prerequisites

- Windows OS (not tested in UNIX or MacOS environments)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- [OpenSSL](https://www.openssl.org/) – optional tool for generating local certificates for development purposes (included with [Git for Windows](https://gitforwindows.org)).

## Install

```sh
cd ~/path/to/your/directory
git clone git@github.com:Tyyyrrr/MinimalProxy.git
cd MinimalProxy
dotnet build
```

> Then, if you want to use SSL encryption, you’ll need to bind the application’s AppID (GUID) to a certificate signed by a trusted authority (or self-signed for localhost development).
> The AppID can typically be found in the project’s <a href="https://github.com/Tyyyrrr/MinimalProxy/blob/master/AssemblyInfo.cs" target="_blank">AssemblyInfo.cs</a> file or defined programmatically.

Example:
```sh
netsh http add sslcert ipport=0.0.0.0:8443 certhash=<certificate_thumbprint> appid={<app_guid>}
```

## Usage

>Run with --help argument to see available options. (you dont need to exit the program once help string is displayed. Simply enter configuration arguments right after)
```sh
cd ~/path/to/your/directory/MinimalProxy
dotnet run -- --help
```
>...or skip display of help text and run directly, for example:
```sh
dotnet run -- -host 127.0.0.1 -port 8080 -url http://my_website.com/index -ssl n -limit 1000 -timeout 20
```

>If service starts without error, you should be able to see applied configuration summary on the console log:

```
MinimalProxy is up.

Configuration:
Server URL: http://127.0.0.1:8080/
Target URL: http://my_website.com/index
Maximum requests: 1000
Timeout: 20s

```

>Press ENTER key to terminate



## Author

👤 **Michał Kuglin**

* Github: [@Tyyyrrr](https://github.com/Tyyyrrr)

## Show your support

Give a ⭐️ if this project helped you!

## 📝 License

Copyright © 2025 [Michał Kuglin](https://github.com/Tyyyrrr).<br />
This project is [MIT](LICENSE) licensed.

***
_This README was generated with ❤️ by [readme-md-generator](https://github.com/kefranabg/readme-md-generator)_