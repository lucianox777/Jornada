# Evidência Runtime — SQL Database in Microsoft Fabric

**Data da execução:** 03/09/2026
**Base de código:** Solution Engenharia v4.00 com patch de compatibilidade r4; correção r5 posterior afeta apenas a interpretação do `ExitCode` do processo PowerShell após o TRX já aprovado.
**Alvo:** SQL Database in Microsoft Fabric
**Autenticação:** Microsoft Entra Device Code Flow

## Resultado

A execução externa reportada pelo operador concluiu:

```text
Passed!  - Failed:     0, Passed:    58, Skipped:     0, Total:    58, Duration: 4 m 48 s
Fabric Integration: total=58, passed=58, failed=0, notExecuted=0
```

O arquivo TRX recebido confirma 58 testes executados, 58 aprovados, 0 falhas e 0 não executados. O teste concorrente `Eight_simultaneous_processor_contenders_have_exactly_one_winner_and_gate_recovers` também foi aprovado.

**SHA-256 do TRX recebido:** `96ff1494b8e5ebb2afecc4d03be7a72e3b3617c8629160185cfff3e94b82e0e2`

## Interpretação

A evidência comprova compatibilidade funcional da implementação atual com SQL Database in Microsoft Fabric usando o mesmo Adapter/DDL da baseline Microsoft SQL. Não comprova equivalência de desempenho, custo ou comportamento sob carga de produção.

A v4.01 consolida o harness corrigido e mantém SQL Server 2022 local/Testcontainers como baseline obrigatória de desenvolvimento e validação ordinária. Fabric permanece alvo adicional de homologação, condicionado à disponibilidade de ambiente específico.

O TRX bruto não é incorporado ao pacote porque contém dados operacionais efêmeros do fluxo de autenticação e caminhos locais da estação de teste; o hash acima preserva a rastreabilidade da evidência recebida.
