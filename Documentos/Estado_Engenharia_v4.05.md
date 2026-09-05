# Estado de Engenharia - v4.05

**Base Normativa:** v3.64  
**Solution Engenharia:** v4.05  
**SolutionSchema:** 3.69  
**Data:** 04/09/2026

## Correção de runtime

A v4.05 corrige exclusivamente a engenharia da v4.04. A migração de vigência de Benefício Concedido usava SQL dinâmico com literais sobre-escapados, fazendo o SQL executado conter aspas duplicadas e falhar com Msg 102 próximo a `BENEFICIO` e `ENCERRADA`. Os literais do bloco dinâmico foram corrigidos sem alterar `situacaoVigencia`, `situacaoVigenciaDesde`, `motivoEncerramento`, Base Normativa ou SolutionSchema.

## Validação local canônica

`scripts/local-validate-release.ps1` mantém o nome canônico e passa a executar `scripts/local-clean.ps1` sempre no início, antes de qualquer restore/build/test. A validação gera evidência VSTest em `TestResults/Release/Jornada.Unit.Release.trx` e `TestResults/Release/Jornada.Integration.Release.trx`. Em falha de teste, o script informa o caminho da evidência antes de encerrar.

Os dois scripts PowerShell são distribuídos com BOM UTF-8 para compatibilidade com Windows PowerShell 5.1 e correta exibição dos textos em português.

## Evidência que motivou a correção

Na execução externa da v4.04, Unit concluiu 158/158 PASS. Integration concluiu 13 PASS e 45 FAIL; todos os 45 failures apresentaram o mesmo erro de sintaxe SQL (`BENEFICIO` / `ENCERRADA`) durante a execução do DDL. Por isso não foi identificado um conjunto de 45 defeitos funcionais independentes, mas uma falha única e transversal de bootstrap.

A v4.05 ainda requer reexecução da validação local completa no ambiente Windows/.NET/Docker/SQL Server do operador.
