# GOV-V9-GERADOR-CANONICO-20261125 — gerador sintético canônico evolutivo

**Data:** 25/11/2026. **Autoridade:** Product Owner. **Estado:** DECIDIDO; implementação inicial submetida, não validada nem integrada. **Política:** GOV-DOC-01.

## Decisão e contexto

Após avaliação das diferenças entre os executáveis do Calibrador V9 e do ensaio DEV V9, o PO declarou “Eu concordo, pode implementar.” Adotar um único gerador evolutivo compartilhado pelos dois caminhos, com perfis configuráveis e versões históricas preservadas para reprodução. A aprovação refere-se à arquitetura e à possibilidade de evolução familiar compartilhada, não à ratificação de prevalências, tetos, critérios ou quotas propostos na avaliação anterior do perfil família.

O Standard Linear da campanha #38 (SHA 30c71a0db1bdeac6ba536db5e428ade4d5c70c9c) executou o projeto `Jornada.Linkage.SyntheticCorpus.Parametrized` para as três coortes independentes. Os ensaios DEV V9 chamavam o projeto histórico `Jornada.Linkage.SyntheticCorpus` para ZIPs de ingestão. Ambos compartilham fontes, IBGE e distribuição diária de nascimento, mas Program, Generator, Materializer e Models diferem. A separação foi introduzida na #1769 para recuperar os digests LOCKED (#1734); não revertê-la.

## Implementação inicial e limites

- Tornar `Solution/src/Jornada.Linkage.SyntheticCorpus.Parametrized` o executável canônico evolutivo; nenhum arquivo da árvore histórica é alterado. Não criar terceiro gerador.
- Redirecionar `v9-e2e-1k-prepare-waves.sh` e `v9-e2e-ibge-http.sh` à mesma CLI utilizada pelo Calibrador. Como o projeto não integra Jornada.sln, restaurar explicitamente em locked mode e compilar Release antes da execução. Fixar 60/20/20 nos comandos DEV para preservar os defaults históricos. Manter seed, população, perfil, referências, gestores, ondas, pseudonimização, proteção da verdade e preflight. O workflow científico não muda.
- Não alterar ensaios legados/reproduções históricas que ainda dependem do produtor congelado. O script de escala novo da #1940 está em branch concorrente e deve ser migrado para o canônico somente depois da sua prova de paridade SQL, com revisão do diff e nova validação no SHA exato. Não interferir no run ativo nem reescrever sua evidência.
- A modelagem de famílias deve evoluir no projeto canônico com configuração explícita e versionada, separada do perfil histórico, sem defaults demográficos inventados. Gabarito familiar selado, sem acesso pelo scorer; coortes independentes e futura partição por unidade familiar/relacional quando necessária para impedir vazamento científico. Nenhuma quota familiar, teto ou critério do v7 é alterado nesta PR; a geração familiar e sua execução permanecem bloqueadas pelos gates atuais e pela falta de manifesto aprovado.

## Justificativa e impactos

Um único caminho evolutivo reduz divergência entre a distribuição que estima os parâmetros FS e a que testa a operação. A ausência de relações familiares explícitas é uma limitação plausível principalmente para U/TF e o frame de candidatos; não é prova de que o vencedor tenha erro nem invalidação retroativa. Ciência e operação continuam com massas, sementes, contratos e autorizações independentes. O manifesto parametrizado acrescenta metadados de população/partição fora da exceção histórica seed42/30k; não prometer identidade do manifesto. Exigir identidade dos arquivos de pessoas, observações e gabarito, fingerprint e censo, e igualdade das entregas/verdade por chave de teste fixa antes de integração. O teste técnico não é TRAIN, VALIDATION ou TEST científico.

## Precedência, gates e rastreabilidade

Complementa #1769 e #1734, preserva LOCKED, #38, S4b(i), v7 e #1940. Não modifica scorer, modelo, algoritmo, parâmetros, publicação, SQL, gates, HML/PROD, políticas nem autoriza qualquer campanha científica ou 10k/100k adicional. Não considerar concluído antes de regressão técnica, checks obrigatórios no SHA exato, reconciliação com #1940 e integração governada. A implementação familiar continua pendente; o estado da fase B não muda. Referências: `P0A_Alternativas_Recuperacao_Gerador_TF_LOCKED_20261120.md`, `V9_Standard_Linear_100k_Resultado_Run_44970012048.md`, #1851/#1931/#1940.
