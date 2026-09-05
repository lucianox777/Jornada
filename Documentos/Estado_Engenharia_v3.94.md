# Estado de Engenharia — v3.94

- Base Normativa: **v3.62**
- SolutionSchema: **v3.68**
- Solution Engenharia: **v3.94**
- Predecessora: **v3.93**
- Estado: **quatro resíduos Integration tratados; reexecução pendente**

## Evidência v3.93

A execução real confirmou limpeza, restores `--locked-mode`, build Release com 0 warnings/0 erros e Unit 153/153. Os 58 Integration executaram: **54 passaram e 4 falharam**.

O bloqueador comum da v3.92 (`ck_vinculo_metodo` / SQL 547) não reapareceu. Restaram quatro sintomas independentes: um `null` transitório na disputa READPAST; contagens contaminadas por estado compartilhado do fixture; `COMPROVADO` inválido chegando à constraint Silver antes da validação de aplicação; e seed/projeção individual com pressupostos não convergentes após cenários de identidade.

## Correções v3.94

- validação `COMPROVADO` + `verificadoEm` é aplicada em `PersistPersonAsync` antes do INSERT do atributo, dentro da transação;
- o teste concorrente mantém a prova de exclusão mútua e acrescenta poll de progresso para o worker que recebeu `null` transitório;
- o teste de sucesso conta somente os identificadores criados pelo próprio cenário;
- `Jornada_Seed_Dev.sql` restaura avaliações canônicas de possibilidade por linha;
- o teste canônico do seed alinha `serving.v_registros_pessoa` ao subconjunto `VIGENTE` + `ATRIBUIDA` de `serving.registro_integrado`.

## Pendência

Executar `scripts/local-clean.ps1` e `scripts/local-validate-release.ps1`. Não há alegação de 58/58 para v3.94 antes dessa execução real.
