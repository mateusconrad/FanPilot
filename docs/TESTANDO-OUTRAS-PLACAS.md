# Testando o FanPilot numa placa que não é Gigabyte (ASUS, MSI, ASRock…)

[English](TESTING-OTHER-BOARDS.md) · **Português**

Nessas placas o FanPilot usa o [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
e executa as curvas ele mesmo (veja "Placas sem GCC" no [README](../README.pt-BR.md#placas-sem-gcc-curvas-executadas-pelo-fanpilot)).
Ainda não há placa confirmada, por isso o teste vai em etapas, da mais segura para a menos segura. **Pare na
primeira etapa que der algo estranho** e mande o que tiver.

## 0. Antes de começar

- Use o zip da release (`FanPilot-….zip`) e extraia numa pasta sua, por exemplo `C:\FanPilot`.
- Driver **PawnIO**: na primeira vez que abrir o `FanPilot.exe`, se ele faltar, a janela pergunta se pode instalar
  pelo `winget`; responda `S`. Sem winget, instale pelo site oficial: https://pawnio.eu. Reinicie o PC se pedir.
- **Desative o controle de fans do fabricante** enquanto testa: na ASUS, no Armoury Crate / AI Suite, deixe o
  Fan Xpert sem perfil ativo (ou feche-o); na MSI, o MSI Center. Se os dois controlarem ao mesmo tempo, o teste
  não vale.
- Anote o **modelo exato da placa-mãe** (está no `--diagnose`, mas confira na caixa ou na BIOS).

## 1. Diagnóstico (só leitura, não mexe em nenhum fan)

Abra um terminal na pasta e rode:

```powershell
.\FanPilot.exe --diagnose
```

O diagnóstico não instala nada: se o PawnIO faltar, abra o `FanPilot.exe` uma vez antes (etapa 0). Ele pede
Administrador, salva `data\diagnostico-<data>.txt` e abre o arquivo no Bloco de Notas. O arquivo sai sem números de
série nem UUIDs (são do seu PC, não do modelo da placa) e começa com o link do formulário de relato já preenchido.
No Windows em português o arquivo sai em português.
Confira nele:

- `PawnIO: instalado` — se aparecer "NÃO instalado", volte à etapa 0.
- Em **Fans controláveis**, a lista dos headers (ex.: `CPU Fan`, `Chassis Fan #1`, `AIO Pump`).
- Em **Sensores de referência**, temperaturas plausíveis (CPU entre 30 e 70 °C em repouso).

Se a lista de fans vier vazia, pare aqui: o LibreHardwareMonitor não controla essa placa, e o arquivo já ajuda a
entender o porquê.

**No Linux** (em teste), a partir do pacote extraído: primeiro o `sensors` (lm-sensors) deve listar o chip da placa
com fans e temperaturas; depois, `sudo ./FanPilot --diagnose` salva `data/diagnostico-<data>.txt` ao lado do programa.
Além das mesmas listas, ele traz cada chip hwmon com os seus arquivos e os drivers de placa carregados: se nenhum chip
aparecer, o arquivo diz qual driver falta ou está preso pela BIOS. As etapas seguintes são as mesmas, com
`sudo ./FanPilot`, e o caminho no formulário é "Linux hwmon".

## 2. Monitoramento

Rode `.\FanPilot.exe` (pede Administrador e abre `http://localhost:8787/pair` no navegador). Abra o painel no
próprio PC ("Abrir painel aqui") e confira se temperaturas, RPM e GPU batem com o que o Armoury Crate/HWiNFO
mostram. Ainda não altere nada.

## 3. Um fan de gabinete, velocidade fixa

Escolha um **fan de gabinete** (`Chassis Fan`). **Não comece pelo fan do processador nem pela bomba
(`AIO Pump`, `W_PUMP`)**.

1. No painel, toque nele › **Fixo** › 40% › Aplicar. Em poucos segundos o RPM deve cair.
2. Toque de novo › **Devolver à BIOS**. O RPM deve voltar ao de antes.
3. Coloque em 40% outra vez e **feche a janela do FanPilot** (no X). O RPM deve voltar ao de antes:
   é a proteção de devolver os fans à BIOS ao fechar.

## 4. Curva

No mesmo fan de gabinete, escolha uma curva (ex.: "Padrão"), aplique e deixe alguns minutos com o PC em uso
(um jogo ou um teste de CPU). O RPM deve acompanhar a temperatura do sensor escolhido. Depois, "Devolver à BIOS".

## 5. O que mandar

Abra o link do começo do arquivo do diagnóstico (ou o
[formulário de compatibilidade](https://github.com/mateusconrad/FanPilot/issues/new?template=compatibilidade.yml)
direto). Ele já vem com o modelo da placa e a versão do FanPilot e pede:

- a versão da BIOS e o caminho que o FanPilot usou;
- o que funcionou em cada etapa (o RPM mudou? voltou ao fechar?) e o resultado;
- o arquivo `diagnostico-….txt` e o `data\fanpilot.log`, arrastados para o formulário.

As placas já relatadas ficam em [COMPATIBILITY.md](COMPATIBILITY.md).

## Se algo der errado

- Fan não volta ao normal: abra e feche o FanPilot de novo, ou reinicie o PC — a BIOS reassume os fans no boot.
- Para remover tudo: apague a pasta e, se quiser, desinstale o PawnIO pelo "Adicionar ou remover programas".
