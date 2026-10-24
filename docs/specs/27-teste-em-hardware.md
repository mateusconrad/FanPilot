# Teste automatizado na placa real

- **Item:** 27
- **Onda:** 2 · Entrega
- **Tipo:** infra, segurança
- **Esforço:** P–M
- **Depende de:** 08

## Problema

Todo item que mexe em fans, GPU, ponte da placa ou build exige o roteiro de verificação em hardware (`docs/ROADMAP.md`), que hoje é manual: 9 passos repetidos a cada PR, fáceis de esquecer e sem um registro comparável entre versões. O `tools/Probe4.cs` já mostrou que dá para automatizar um teste de escrita com restauração.

## Objetivo

Um comando que executa o roteiro na placa real, com segurança, e gera um relatório pronto para anexar ao PR.

## Solução

1. **`FanPilot.exe --hardware-test --fan <índice>`** roda dentro do próprio processo (ponte da placa, `FanManager` e GPU, sem o servidor web) e exige que nenhuma outra instância esteja aberta (mutex do item 05). Passos:
   - leitura: backend, placa, versão do GCC, sensores com valores plausíveis e RPM dos fans;
   - fan escolhido em fixo 40%: espera até 15 s por uma queda de RPM de pelo menos 15%; depois restaura e espera a volta;
   - curva "Padrão" no mesmo fan: 60 s sem reaplicação do watchdog;
   - Turbo: ao ligar, a velocidade comandada vai a 100% e nenhum RPM cai; ao desligar, cada fan volta ao que estava;
   - GPU, se houver controle: fixo 50% e depois automático;
   - encerramento: chama a mesma limpeza da saída normal e abre a ponte de novo para conferir o resultado (na Gigabyte, as curvas continuam no chip; no LHM, os fans voltam à BIOS).
2. **Segurança:**
   - só mexe no fan indicado e no Turbo (que vai a 100%), e nunca abaixo de 40%;
   - recusa por padrão o header da CPU e os marcados como bomba (item 17, quando existir);
   - temperatura de CPU ou GPU acima de 80 °C aborta e restaura;
   - tudo é restaurado num `finally`, inclusive com Ctrl+C.
3. **Relatório:** `data\hwtest-<data>.md` com versão, placa e cada passo (ok ou falhou, valores medidos e tempos), pronto para colar no PR.
4. Código de saída diferente de 0 quando algum passo falha, para uso em script.
5. Substitui o roteiro manual em "Como cada item é entregue". O roteiro manual continua só para o que não dá para medir, como comparar com o HWiNFO e conferir no celular.

## Critérios de aceite

- [ ] Na B760M, o teste passa e gera o relatório em menos de 3 min.
- [ ] Interromper no meio (Ctrl+C) restaura o fan e desliga o Turbo.
- [ ] Com uma sobrescrita da curva simulada, o passo do watchdog falha.

## Testes

- Automatizados: a sequência e as regras de segurança do `--hardware-test` rodam na CI contra a placa falsa (item 08), inclusive o aborto por temperatura e a restauração no Ctrl+C.
- Manuais: a primeira execução na B760M, acompanhada.
