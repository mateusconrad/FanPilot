# Teste automatizado na placa real

- **Item:** 27
- **Onda:** 2 · Entrega
- **Tipo:** infra, segurança
- **Esforço:** P–M
- **Depende de:** 08

## Problema

Todo item que mexe em fans, GPU, ponte da placa ou build exige o roteiro de verificação em hardware (`docs/ROADMAP.md`), que hoje é manual: 9 passos repetidos a cada item, fáceis de esquecer e sem um registro comparável entre versões. O `tools/Probe4.cs` já mostrou que dá para automatizar um teste de escrita com restauração.

## Objetivo

Um comando que executa o roteiro na placa real, com segurança, e gera um relatório pronto para registrar na issue.

## Solução

1. **`FanPilot.exe --hardware-test --fan <índice>`** roda dentro do próprio processo (ponte da placa, `FanManager` e GPU, sem o servidor web) e exige que nenhuma outra instância esteja aberta (mutex do item 05). Passos:
   - leitura: backend, placa, versão do GCC, sensores com valores plausíveis e RPM dos fans;
   - fan escolhido em fixo 40% e depois em 100%: a placa precisa ficar com o que foi gravado, e o RPM a 100% precisa ser pelo menos 15% maior que a 40% (um fan que já rodava abaixo de 40% subiria, então o teste mede a reação, não uma queda). Um fan sem tacômetro só tem a gravação conferida;
   - uma curva de 40% a 100% no mesmo fan (a "Padrão" do painel desce a 30%): 60 s com o watchdog conferindo a cada 15 s, sem precisar reaplicar;
   - Turbo: ao ligar, todos os fans vão a 100% e nenhum RPM cai; ao desligar, cada fan volta ao que estava;
   - GPU, se houver controle: fixo 50% e depois automático;
   - encerramento: grava fixo 40%, encerra a ponte como na saída normal e abre de novo para conferir (na Gigabyte, a curva continua no chip; no LHM, o fan volta à BIOS).
2. **Segurança:**
   - só mexe no fan indicado e no Turbo (que vai a 100%), e nunca abaixo de 40%;
   - recusa o header da CPU e as bombas (pelo nome, até o item 17 dar um papel a cada fan), e um fan que não teria como devolver (sem configuração do usuário e sem curva original salva);
   - temperatura de CPU ou GPU acima de 80 °C aborta;
   - no fim, ou num aborto, devolve exatamente o que havia: a configuração do usuário, se o fan tinha uma, senão a curva original; a GPU volta ao modo em que estava. Ctrl+C é interceptado; fechar a janela no meio do teste, não.
3. **Relatório:** `data\hwtest-<data>.md` com versão, placa e cada passo (ok ou falhou, valores medidos e tempos), pronto para colar na issue.
4. Código de saída para uso em script: 0 passou, 6 falhou, 7 interrompido, 8 não executado (recusado ou placa sem resposta).
5. Substitui o roteiro manual em "Como cada item é entregue". O roteiro manual continua só para o que não dá para medir, como comparar com o HWiNFO e conferir no celular.

## Critérios de aceite

- [ ] Na B760M, o teste passa e gera o relatório em menos de 3 min.
- [ ] Interromper no meio (Ctrl+C) restaura o fan e desliga o Turbo.
- [ ] Com uma sobrescrita da curva simulada, o passo do watchdog falha.

## Testes

- Automatizados: a sequência e as regras de segurança do `--hardware-test` rodam na CI contra a placa falsa (item 08), inclusive o aborto por temperatura e a restauração no Ctrl+C.
- Manuais: a primeira execução na B760M, acompanhada.
