# Decisão canônica — Calibrador único adaptativo, ref e bootstrap (14/10/2026)

**Status:** decisão arquitetural aprovada; implementação e certificação pendentes na [issue #1089](https://github.com/lucianox777/Jornada/issues/1089). Este documento prevalece, no escopo abaixo, sobre propostas anteriores de controlador adaptativo em runtime, calibrador V9 paralelo ou seleção operacional dinâmica de estratégias. Não declara que a solução já esteja implantada.

## Arquitetura e ciclo de vida

1. **Um único Calibrador histórico da Jornada, implementado em C#.** Incorporar nele os algoritmos de busca e avaliação desenvolvidos nos experimentos V9; não criar segundo calibrador, otimizador independente ou controlador de estratégias no linkage operacional. Scripts históricos permanecem como evidência e oráculos de regressão, não como segunda fonte de decisão.
2. **Calibração inicial:** executar processo experimental controlado, produzir snapshot de parâmetros completo, versionado, rastreável e **imutável na camada `ref`**. O bootstrap da aplicação **consome esse snapshot**, verificando hash, schema, proveniência, versões de projeção/comparadores, elegibilidade e estado de aprovação. **Bootstrap não treina nem recalibra.** Ausência/incompatibilidade de modelo exigido deve falhar de modo explícito, nunca aplicar parâmetros inventados.
3. **Calibrações subsequentes:** exclusivamente **sob comando manual**. Diagnóstico automático de suficiência pode recomendar nova calibração, mas não dispara treinamento, publicação, promoção ou ativação. Cada nova versão passa por avaliação, aprovação e publicação explícita; versões anteriores permanecem disponíveis para auditoria/rollback.
4. **Separação de publicação e ativação:** gravar referência inicial em `ref` não autoriza automaticamente ativar V9 em produção. DT-15 e gates estatísticos/operacionais permanecem obrigatórios. Preservar V8, Gold, IBGE original e ambientes não autorizados.

## Engenharia reversa obrigatória e parâmetros adaptativos

O Calibrador deve **descobrir** configurações por experimentação reproduzível, não codificar como constantes os valores historicamente vencedores. Recuperar scripts, commits, workflows, artefatos, corpus, seeds, fingerprints, normalizações e consultas físicas. Espaço de busca extensível e versionado:

- Atributos originais e projeções homologadas: nome, nome da mãe, sobrenomes, nascimento e novos campos futuros; cada atributo declara semântica, ausência, normalização, comparadores, materialização e elegibilidade.
- **Quantidade de caracteres:** comprimentos de prefixo para pessoa e mãe, prefixos de prenome, iniciais, último sobrenome e outras combinações; recuperar varreduras históricas de 1 a 4 caracteres e prefixo3, mas não fixar 3 como ótimo universal. Tratar nomes curtos/ausentes sem chave curinga e sem truncamento silencioso.
- Passes e união deduplicada, ordem e combinações; restrições de índices, fanout e orçamento de consultas; medir ganho incremental M/U e latência SQL física.
- Distribuições M/U condicionadas à união efetiva de candidatos, dependência entre evidências nominais, TF com unidade e baseline compatíveis, prior, limiares e abstenção. Não multiplicar marginais dependentes nem atribuir TF inexistente.

Novos atributos entram pelo catálogo versionado de projeções/comparadores e seus hiperparâmetros; **não** passam a integrar snapshot publicado sem nova calibração manual e validação.

## Protocolo de reprodução e certificação

- Reconstruir exatamente o frame histórico e os denominadores, seeds, perfis de erro e versões de algoritmos. Reproduzir contagens M/U, recall de blocking, fanout, seleção de passes e hashes. Referências históricas aproximadas de **95,0044% (plano progressivo de dez células TRAIN)** e **96,87% (união de 19 células TRAIN)** são **regressões de blocking**, não certificação de FS/FDR, nem metas a perseguir por ajuste indevido.
- TRAIN estima e explora; VALIDATION seleciona sob orçamento estatístico previamente versionado, considerando multiplicidade; TEST permanece lacrado até avaliação final, sem refit. Registrar LCB de recall, UCB de FDR, amostra efetiva por identidade/estrato, abstenções, FP, FN, custo e limites. Se não houver suporte ou aprovação, declarar INCONCLUSIVO/REPROVADO.
- Comparar novo C# com implementação independente e com os artefatos originais; divergência exige explicação rastreável, mesmo se percentual final for semelhante. Ganhos superiores são aceitos somente com protocolo equivalente e sem vazamento.
- SQL experimental apenas no `JornadaHarness` efêmero; não alterar Gold, V8, IBGE original, JornadaLocal, NODE/NAS, HML/PROD. Merge exige CI completa no SHA e revisão, sem equiparar CI verde a certificação estatística.

## Referências de implementação e documentação

- [Especificação do Calibrador FS](Calibrador_FS_Specification.md)
- [Blocking dinâmico compartilhado](Linkage_Dynamic_Blocking.md)
- [Avaliação de comprimento de prenome/inicial materna](V9_Avaliacao_Prefixos_Prenome_Inicial_Mae_Camadas_20261014.md)
- [Prefixos pessoa/mãe e SQL](V9_Prefixos_Nome_Mae_Combinacoes_SQL_20261014.md)
- [Issue de implementação #1089](https://github.com/lucianox777/Jornada/issues/1089), [issue de qualidade #999](https://github.com/lucianox777/Jornada/issues/999), PRs experimentais #1087/#1088.

**Regra de precedência:** documentos históricos continuam válidos como descrição do estado e das evidências de sua época; qualquer prescrição conflitante de calibrador paralelo, adaptação de política em runtime ou recalibração automática é superada por esta decisão.