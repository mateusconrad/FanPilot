# Assinatura de código

- **Item:** 12
- **Onda:** 2 · Entrega
- **Tipo:** infra
- **Esforço:** P (mais o tempo de aprovação externa)
- **Depende de:** 11

## Problema

O FanPilot é um exe sem assinatura que pede Administrador, instala um driver de kernel (PawnIO, via winget) e abre uma porta no firewall. É exatamente o perfil que o SmartScreen ("O Windows protegeu o computador") e as heurísticas de antivírus bloqueiam, e o usuário não tem como conferir quem publicou o arquivo.

## Objetivo

Releases assinadas, com editor verificável. A reputação no SmartScreen continua sendo construída com os downloads, mas passa a partir de um arquivo assinado.

## Opções

Condições e preços mudam; confirmar antes de decidir.

| Opção | Custo | Observações |
|---|---|---|
| **SignPath Foundation** (recomendada) | gratuito para open source | Exige inscrição e aprovação do projeto; a assinatura é feita pela CI (integração com o GitHub Actions); o certificado sai em nome da fundação. |
| Azure Trusted Signing | ~US$ 10/mês | Para pessoa física, a elegibilidade depende do país. |
| Certificado OV tradicional | ~US$ 200–400/ano | Chave em token físico ou HSM na nuvem; mais trabalho na CI. |

## Solução

1. Inscrever o projeto no SignPath Foundation (ação do dono do repositório).
2. Integrar a action de assinatura do SignPath ao `release.yml` (item 11): artefato sem assinatura → pedido de assinatura → artefato assinado → release.
3. Assinar o `FanPilot.exe` e, quando existirem, o `FanPilotTray.exe` e o instalador (item 14). DLLs de terceiros ficam como vêm.
4. Até a aprovação: releases com `SHA256SUMS.txt` e uma seção no README, "Por que o Windows avisa ao abrir", explicando como conferir o hash (`Get-FileHash`).

## Ações do dono do repositório

- Preencher a inscrição do SignPath Foundation.
- Criar os segredos no GitHub (`SIGNPATH_API_TOKEN` e os identificadores da organização e do projeto).

## Critérios de aceite

- [ ] `Get-AuthenticodeSignature FanPilot.exe` retorna `Valid` nos artefatos da release.
- [ ] A assinatura acontece só na CI, nunca numa máquina local.

## Testes

- O workflow de release confere `Get-AuthenticodeSignature` em cada arquivo assinado e falha se algum não estiver `Valid`.
