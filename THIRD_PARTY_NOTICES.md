# Componentes de terceiros

## qrcode-generator — `wwwroot/vendor/qrcode.min.js`

https://github.com/kazuhikoarase/qrcode-generator

```
MIT License

Copyright (c) 2009 Kazuhiko Arase

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## LibreHardwareMonitor e dependências (distribuídos junto com o `FanPilot.exe`)

Baixados do nuget.org no build, em versões fixas (com hash conferido pelo `src/packages.lock.json`), e copiados sem
modificação para a pasta do programa. O código-fonte de cada um está no endereço indicado.

| Componente | Versão | Licença | Código-fonte |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | [MPL-2.0](https://www.mozilla.org/MPL/2.0/) | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| DiskInfoToolkit | 1.1.2 | [MPL-2.0](https://www.mozilla.org/MPL/2.0/) | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | [MPL-2.0](https://www.mozilla.org/MPL/2.0/) | https://github.com/Blacktempel/RAMSPDToolkit |
| BlackSharp.Core | 1.0.7 | [MPL-2.0](https://www.mozilla.org/MPL/2.0/) | https://github.com/Blacktempel/BlackSharp |
| HidSharp | 2.6.4 | [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0) — Copyright 2010-2025 James F. Bellinger | https://software.seekye.com/hidsharp |
| System.Memory | 4.6.3 | MIT — © Microsoft | https://github.com/dotnet/maintenance-packages |
| System.Buffers | 4.6.1 | MIT — © Microsoft | https://github.com/dotnet/maintenance-packages |
| System.Numerics.Vectors | 4.6.1 | MIT — © Microsoft | https://github.com/dotnet/maintenance-packages |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT — © Microsoft | https://github.com/dotnet/maintenance-packages |
| System.Threading.AccessControl | 10.0.3 | MIT — © Microsoft | https://github.com/dotnet/runtime |
| System.Security.AccessControl | 6.0.0 | MIT — © Microsoft | https://github.com/dotnet/runtime |
| System.Security.Principal.Windows | 5.0.0 | MIT — © Microsoft | https://github.com/dotnet/runtime |
| System.CodeDom | 10.0.2 | MIT — © Microsoft | https://github.com/dotnet/runtime |

## MQTTnet (distribuído junto com o `FanPilot.exe`)

O cliente MQTT das integrações com o Home Assistant. Baixado do nuget.org no build, em versão fixa (com hash
conferido pelo `src/packages.lock.json`), e copiado sem modificação para a pasta do programa.

| Componente | Versão | Licença | Código-fonte |
|---|---|---|---|
| MQTTnet | 4.3.7.1207 | [MIT](https://licenses.nuget.org/MIT) — Copyright (c) .NET Foundation and Contributors | https://github.com/dotnet/MQTTnet |

## .NET (distribuído dentro do `FanPilot` do pacote do Linux)

O pacote do Linux (`FanPilot-<versão>-linux-x64.tar.gz`) é publicado self-contained: o runtime do .NET 10 vai dentro do
executável, sem modificação. Licença [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) — Copyright (c)
.NET Foundation and Contributors. Código-fonte e os avisos dos componentes que ele inclui:
https://github.com/dotnet/runtime (`THIRD-PARTY-NOTICES.TXT`). O LibreHardwareMonitor e as suas dependências não vão
no pacote do Linux.

## PawnIO

Driver usado pelo LibreHardwareMonitor para acessar o chip da placa-mãe. **Não é distribuído** com o FanPilot:
o usuário o instala a partir do site oficial, https://pawnio.eu.

## Gigabyte Control Center

O FanPilot **não inclui** nenhum arquivo da Gigabyte. As DLLs cliente do GCC são carregadas em tempo de execução
a partir da instalação do próprio usuário.
