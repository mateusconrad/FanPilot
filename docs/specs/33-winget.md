# Publicação no winget

- **Item:** 33
- **Onda:** 2 · Entrega
- **Tipo:** infra
- **Esforço:** P (mais a revisão do winget-pkgs)
- **Depende de:** 11, 14c
- **Integra com:** 12

## Problema

O SignPath Foundation recusou a assinatura (item 12), e sem ela quem baixa o instalador pelo navegador vê o aviso do
SmartScreen ("O Windows protegeu o computador"). O instalador também só se acha na página de Releases.

## Objetivo

`winget install MateusConrad.FanPilot` instala a versão mais nova, e cada tag nova chega ao winget sem trabalho manual.
O que o winget baixa não ganha a marca de "baixado da internet", então o SmartScreen não interrompe a instalação. O
antivírus continua olhando o arquivo, e a assinatura (item 12) continua desejável.

## Solução

1. **Manifestos** em [`packaging/winget/0.1.0/`](../../packaging/winget/0.1.0/), no formato 1.10 do winget-pkgs:
   instalador `inno`, escopo `machine`, `ElevationRequirement: elevatesSelf` (o próprio setup pede o UAC), o
   `ProductCode` do Inno (`{AppId}_is1`) e o `Publisher` "FanPilot" do Adicionar ou Remover Programas, para o winget
   reconhecer o que já está instalado (também por um setup baixado à mão) e fazer o `upgrade`. Textos em
   inglês (`en-US`, o padrão) e português (`pt-BR`). O `winget validate` passa neles.
2. **Primeira versão à mão:** um PR no [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) com esses
   arquivos em `manifests/m/MateusConrad/FanPilot/0.1.0/`. A pasta `packaging/winget/0.1.0/` fica como registro;
   as próximas versões não passam por ela.
3. **Próximas versões pela CI:** o job `winget` do `release.yml`, depois do `release`, só em tag, roda a
   [WinGet Releaser](https://github.com/vedantmgoyal9/winget-releaser) (Komac), pinada por SHA porque recebe um token
   com escrita. Ela copia o manifesto da versão anterior, troca a URL e o hash pelo `-win-x64-setup.exe` da release e
   abre o PR no winget-pkgs a partir do fork do dono. Sem o segredo `WINGET_TOKEN`, o job só deixa um aviso: a
   release não depende do winget.

### Instalação silenciosa

O winget roda o Inno com `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`. Nesse modo:

- A tarefa do PawnIO fica como no assistente: marcada sem o Gigabyte Control Center, então o setup chama
  `winget install namazso.PawnIO` por conta própria.
- A página de pareamento não abre (`skipifsilent`), e a mensagem de erro do registro do serviço vai só para o
  `install.log`.
- A desinstalação silenciosa (`winget uninstall`) mantém os dados em `%ProgramData%\FanPilot`, como uma atualização.

## Ações do dono do repositório

- Criar um fork de `microsoft/winget-pkgs` na conta `mateusconrad`.
- Abrir o PR da primeira versão (passo 2) e acompanhar a revisão (validação automática, depois um moderador).
- Criar um token clássico com o escopo `public_repo` e salvá-lo como segredo `WINGET_TOKEN` do repositório.

## Critérios de aceite

- [x] `winget validate --manifest packaging/winget/0.1.0` passa.
- [ ] O PR da 0.1.0 é aceito e `winget install MateusConrad.FanPilot` instala o serviço, a bandeja e o atalho.
- [ ] `winget upgrade MateusConrad.FanPilot` reconhece uma instalação feita pelo setup baixado à mão.
- [ ] A tag seguinte abre sozinha o PR da versão nova no winget-pkgs.

## Testes

- A validação do manifesto (`winget validate`) e a do winget-pkgs, que instala o pacote numa máquina limpa e roda os
  antivírus antes do moderador.
- A instalação silenciosa já é testada pela CI a cada push (item 14c), com os mesmos parâmetros que o winget usa, mais
  `/MERGETASKS="!pawnio"`: a chamada aninhada ao winget para o PawnIO só é exercitada pela validação do winget-pkgs.
