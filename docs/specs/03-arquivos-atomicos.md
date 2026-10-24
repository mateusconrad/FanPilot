# Leitura tolerante e gravação atômica dos arquivos de dados

- **Item:** 03
- **Onda:** 1 · Estabilidade e testes
- **Tipo:** bug, confiabilidade
- **Esforço:** P
- **Depende de:** —

## Problema

- O README orienta editar o `config.json` à mão, mas um JSON inválido (uma vírgula sobrando) faz `AppConfig.Load` lançar exceção sem tratamento (`src/Config.cs:32`), e o FanPilot fecha ao abrir. Provavelmente explica parte do sintoma "FanPilot fecha logo ao abrir" da tabela de problemas comuns.
- O mesmo acontece com `desired-curves.json` e `turbo.json` (`src/FanManager.cs:37-48`) e com `machine.json` (`src/MachineData.cs:26`).
- Todas as gravações usam `File.WriteAllText` direto (`src/FanManager.cs:100`, `:135`, `:282`; `src/Auth.cs:197`; `src/MachineData.cs:39`; `src/Config.cs:45`). Um desligamento no meio da escrita deixa o arquivo truncado, e o arquivo truncado derruba a inicialização seguinte.
- Caso relacionado: se o `backup-curves.json` sumir ou corromper enquanto há curvas do FanPilot gravadas no chip (Gigabyte), `EnsureBackup` (`src/FanManager.cs:118-140`) captura as curvas atuais e as grava como "originais". Daí em diante, "Restaurar original" devolve a curva do próprio FanPilot.

## Objetivo

Nenhum arquivo de dados derruba o FanPilot, e nenhuma gravação deixa um arquivo pela metade.

## Solução

1. **`DataFile`** (novo, `src/DataFile.cs`):
   - `WriteAtomic(path, value)`: serializa, grava em `path.tmp` com `FileOptions.WriteThrough` e `Flush(true)`, e troca com `File.Replace(tmp, path, path + ".bak")` (ou `File.Move`, se o destino ainda não existir). Sempre fica uma geração anterior em `.bak`.
   - `TryRead(path, out value, out problem)`: se o principal estiver ilegível, tenta o `.bak`. Se os dois falharem, move o principal para `path.corrompido-AAAAMMDD-HHMMSS` e devolve "vazio" com a descrição do problema.
2. **`config.json` inválido** não é movido nem sobrescrito, porque é edição do usuário. O FanPilot usa os padrões, registra o erro com o motivo e o painel mostra um aviso ("config.json inválido, usando valores padrão: <motivo>").
3. **Arquivos de estado** (`desired-curves`, `turbo`, `sessions`, `machine`, `backup-curves`) passam a ler e gravar só pelo `DataFile`.
4. **`machine.json` ilegível** é tratado como "veio de outro PC": o estado é arquivado, como já acontece na troca de placa. É o caminho conservador.
5. **Backup das curvas originais:** com o `backup-curves.json` ausente ou ilegível e sem `.bak`, a recaptura vale só para os fans que o FanPilot **não** gerencia (sem entrada em `desired-curves`). Os gerenciados ficam "sem backup", com o botão Restaurar desabilitado e um aviso no log. A curva do FanPilot nunca é registrada como original.

## Fora de escopo

- Validar cada campo do `config.json` além dos limites que já existem (`src/Config.cs:47-53`).

## Critérios de aceite

- [ ] `config.json` com vírgula sobrando: o FanPilot abre com os padrões, log e painel explicam o motivo, e o arquivo continua intacto.
- [ ] `desired-curves.json` truncado: o FanPilot abre usando o `.bak`. Sem `.bak`, abre sem curvas gerenciadas e preserva o arquivo ruim como `.corrompido-…`.
- [ ] `backup-curves.json` apagado com uma curva do FanPilot ativa: o fan gerenciado fica "sem backup" e os demais são recapturados.
- [ ] Não sobra nenhum `File.WriteAllText` direto em `src/` para arquivos de estado.

## Testes

- Automatizados: `DataFile` (substituição, primeiro arquivo, recuperação pelo `.bak`, arquivo corrompido preservado); `FanManager` e `AuthManager` iniciando com arquivos corrompidos; regra de recaptura do backup.
- Manuais: os critérios acima, no PC de teste.
