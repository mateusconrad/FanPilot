# Changelog

Mudanças visíveis do FanPilot, da mais nova para a mais antiga. O formato segue o
[Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e as versões seguem o
[Versionamento Semântico](https://semver.org/lang/pt-BR/). Enquanto a versão começar com `0.`, cada release sai como
pre-release.

Todo commit que muda algo visível acrescenta uma linha em **Não lançado**. Na release, essa seção ganha o número e a
data (`## [0.1.0] - AAAA-MM-DD`), e as notas da release no GitHub são tiradas dela.

## [Não lançado]

Primeira versão publicada (será a 0.1.0).

### Adicionado

- Painel web servido pelo próprio PC, feito para o celular: temperaturas, potência, clocks, gráfico dos últimos
  10 minutos, editor de curvas dos fans da placa-mãe (arrastando os pontos), modo fixo, Turbo e o fan da GPU.
- Acesso pela rede só depois de parear o aparelho com um QR code exibido no PC.
- Placas Gigabyte pelo Gigabyte Control Center, com as curvas gravadas no chip; as outras placas (ASUS, MSI,
  ASRock…) pelo LibreHardwareMonitor e o driver PawnIO, que o FanPilot oferece instalar pelo `winget`.
- GPU NVIDIA pelo driver (NVML); AMD e Intel Arc pelo LibreHardwareMonitor.
- `FanPilot.exe --diagnose`: relatório só de leitura para placas ainda não testadas, com a versão do GCC e se ela é
  compatível.
- `FanPilot.exe --hardware-test [--fan N]`: roteiro automático de verificação na placa real, que grava
  `data\hwtest-<data>.md` e devolve tudo como estava.
- `FanPilot.exe --version`; a versão também aparece na primeira linha do log, no `--diagnose` e no rodapé do painel.
- Releases no GitHub com o zip, o `SHA256SUMS.txt` e estas notas; CI compilando e testando cada push.

### Segurança

- O header `Host` é validado: só são atendidas requisições endereçadas a este PC (as outras recebem `421`), o que
  impede que uma página web alcance a API local por DNS rebinding. Só `localhost` e `127.0.0.1` contam como o
  próprio PC.

### Confiabilidade

- Os arquivos de dados são gravados de uma vez só, com a versão anterior em `.bak`; um arquivo ilegível é guardado
  como `.corrompido-<data>` e o painel avisa.
- As curvas são guardadas pela identidade de cada fan, não pela posição: se a ordem dos fans mudar, cada curva
  continua no seu, e a de um fan que sumiu não vai para outro (o painel oferece descartá-la). Os arquivos do formato
  antigo são convertidos sozinhos e ficam como `.v1`.
- Um FanPilot por vez; se a porta estiver ocupada, ele avisa e fecha antes de mexer em qualquer fan.
- A proteção da GPU e a transmissão ao vivo do painel não dependem mais do laço de leitura dos sensores, e o painel
  avisa quando os dados estão atrasados.
- Log com rotação a cada 5 MB (até 3 arquivos antigos) e sem a mesma mensagem repetida milhares de vezes.
- Turbo e edição de fans não se atropelam mais em toques rápidos; o painel diz se a curva foi gravada no chip ou é
  executada pelo FanPilot; erros do pareamento aparecem na página.

### Para quem compila

- Projeto no formato SDK (`dotnet build`, .NET SDK 8 fixado pelo `global.json`), ainda rodando no .NET Framework
  4.7.2 do Windows, com os pacotes do NuGet fixados por `packages.lock.json`.
- O build não depende mais do Gigabyte Control Center: as DLLs do GCC são carregadas só na hora de rodar, e um mesmo
  zip atende todas as placas.
- Testes automatizados (`dotnet test`, `node --test`) e uma placa falsa para rodar o FanPilot inteiro sem hardware
  (`--fake-board`, builds Debug).
