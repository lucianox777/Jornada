# K4.1 — CNS opcional da SMS: implementação em etapas

## Etapa K4.1a — validador independente e testes (esta PR)

`src/Jornada.Processor.Worker/CnsRules.cs` implementa dois caminhos estritos para números de 15 dígitos ASCII: prefixos 1/2 com DV da base de 11 dígitos e sufixos 000/001; prefixos 7/8/9 com soma ponderada 15→1 divisível por 11. `null` é ausência legítima; string vazia, máscara, números incompatíveis e outros prefixos falham. Os testes usam somente valores sintéticos construídos para os algoritmos.

Referência primária: Agência Nacional de Saúde Suplementar, [Algoritmos do Aplicativo de Carga, seção 4 — CNS](https://www.gov.br/ans/pt-br/centrais-de-conteudo/manuais-do-portal-operadoras/sib-manual-de-instalacao-historico-de-versao-e-outros-arquivos/manual/algoritmos-do-aplicativo-de-carga).

A checagem matemática **não** verifica registro no CADSUS, titularidade, vínculo entre registros nem autoriza resolver identidade por CNS.

## Próxima etapa K4.1b — contrato, ingestão e persistência

Esta etapa **não** altera schemas ou runtime de ingestão. Antes de declarar K4.1 completo:

1. Adicionar `cns` opcional à raiz de `config/contracts/gestores/SMS/pessoa/v1/pessoa.schema.json`, com string estritamente de 15 dígitos e DV validado pelo Processor.
2. **Somente para SMS**, retirar `CNS` do enum `identificadores[].tipo` e eliminar a condição `if/then` inatingível desse tipo. Outros Gestores conservam seus enums, aguardando decisão D4.
3. Integrar validação antes de persistir a observação; preservar o campo na Bronze original e registrar de modo tipado/consultável na Silver, sem produzir um segundo identificador concorrente. Não usar CNS como âncora, regra de igualdade determinística ou novo sinal probabilístico sem decisão de modelo.
4. Atualizar SHA-256 no inventário `config/governance/schema-approvals.json` apenas como evidência de integridade, mantendo status `PENDENTE` e `approval: null`.
5. Provar com teste real em banco descartável que dois CPFs distintos com CNS comum não geram falso vínculo, e validar rollback/migração de esquema. Proteger as outras Secretarias contra regressões.

Nunca executar reset/clean ou migração sobre `JornadaLocal`, Gold histórica ou referência IBGE. HML exige confirmação institucional antes do congelamento.
