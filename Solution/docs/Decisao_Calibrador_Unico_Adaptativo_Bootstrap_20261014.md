# Decisão canônica — Calibrador único adaptativo, ref e bootstrap (14/10/2026)

**Status:** decisão arquitetural aprovada; implementação e certificação pendentes na [issue #1089](https://github.com/lucianox777/Jornada/issues/1089). Este documento prevalece, no escopo abaixo, sobre propostas anteriores de controlador adaptativo em runtime, calibrador V9 paralelo ou seleção operacional dinâmica de estratégias. Não declara que a solução já esteja implantada.

## Decisão complementar vinculante — parâmetros do bootstrap (14/10/2026)

**Não confundir versão do motor com versão dos parâmetros.** O **Calibrador V9 é o único motor definitivo C#**; os melhores valores obtidos pelos experimentos V9 **não são parâmetros definitivos** nem constantes permanentes no código: constituem **candidatos à calibração inicial do bootstrap**, sujeitos à reconciliação de evidências, validação e certificação antes da publicação. O snapshot inicial aprovado é **congelado e versionado em `ref`** e carregado pelo bootstrap sem executar nova calibração. Futuras calibrações poderão publicar snapshots sucessores diferentes, exclusivamente por solicitação manual, validação, aprovação e ativação explícita.

O snapshot de bootstrap deve identificar, conforme efetivamente calibrado: número e ordem de passes, combinações de campos por passe, tamanhos de prefixo e extração de prenome/último sobrenome, regras de normalização e versão do catálogo de atributos/comparadores, estimativas M/U condicionadas à união deduplicada dos candidatos SQL, TF de termos **somente quando publicada ou empiricamente estimada com proveniência e unidade estatística compatível**, limiares, política de abstenção, versão de algoritmos, fingerprints de dados/partições e métricas de TRAIN/VALIDATION/TEST lacrado. Não presumir TF de sobrenome ou distribuição conjunta a partir de marginais de prenomes; não converter recall de blocking em certificação de FDR do linkage.

**Parâmetros de busca não são parâmetros calibrados:** limites máximos de passes, campos por passe, orçamento de fanout/candidatos, memória, latência, espaço de busca e critérios estatísticos de certificação podem permanecer na configuração do Calibrador como guardrails para uma execução manual. Já o **número efetivamente selecionado de passes**, seus campos, comprimentos e pesos pertencem ao snapshot `ref`, não a defaults operacionais hardcoded. O atual `BlockingRuleSetSearchConfiguration` expõe defaults `MaxPasses=2`, `MaxFieldsPerPass=2`, `PrimitivePoolSize=8`, `MinimumTrueMatchRecall=0.95`: são limites/metas da busca, **não valores ótimos comprovados nem conteúdo automaticamente publicado no bootstrap**. A busca deve poder ampliar seu espaço de maneira controlada para avaliar as combinações V9, sem afrouxar limites de segurança.

**Regra de publicação:** escolher a configuração candidata com base em TRAIN e VALIDATION comparáveis, controlar custo/fanout P50/P95/P99/máximo, recall de candidatos e estimativas FS/FDR com intervalos apropriados, manter TEST lacrado até certificação; falhar fechado se evidências ou referência de TF necessária estiverem ausentes. Nunca ativar V9/DT-15 automaticamente por existir um resultado experimental ou snapshot candidato. Preservar resultados de experimentos apenas como evidências reproduzíveis, não como motores legados executáveis.

## Arquitetura e ciclo de vida

1. **O **Calibrador V9 é a versão final e única da Jornada**, implementada em C#, substituindo integralmente as versões anteriores em DEV.** Incorporar nele os algoritmos de busca e avaliação desenvolvidos nos experimentos V9; não criar segundo calibrador, otimizador independente ou controlador de estratégias no linkage operacional. **Não é exigida compatibilidade retroativa nem manutenção executável de calibradores legados em DEV**; preservar somente evidências e oráculos necessários à reprodutibilidade. Scripts históricos permanecem como evidência e oráculos de regressão, não como segunda fonte de decisão.
2. **Calibração inicial:** executar processo experimental controlado, produzir snapshot de parâmetros completo, versionado, rastreável e **imutável na camada `ref`**. O bootstrap da aplicação **consome esse snapshot**, verificando hash, schema, proveniência, versões de projeção/comparadores, elegibilidade e estado de aprovação. **Bootstrap não treina nem recalibra.** Ausência/incompatibilidade de modelo exigido deve falhar de modo explícito, nunca aplicar parâmetros inventados.
3. **Calibrações subsequentes:** exclusivamente **sob comando manual**. Diagnóstico automático de suficiência pode recomendar nova calibração, mas não dispara treinamento, publicação, promoção ou ativação. Cada nova versão passa por avaliação, aprovação e publicação explícita; versões anteriores permanecem disponíveis para auditoria/rollback.
4. **Separação de publicação e ativação:** gravar referência inicial em `ref` não autoriza automaticamente ativar V9 em produção. DT-15 e gates estatísticos/operacionais permanecem obrigatórios. Preservar Gold, IBGE original e ambientes não autorizados; não preservar o calibrador V8 como caminho operacional paralelo.

## Engenharia reversa obrigatória e parâmetros adaptativos

O Calibrador deve **descobrir** configurações por experimentação reproduzível, não codificar como constantes os valores historicamente vencedores. Recuperar scripts, commits, workflows, artefatos, corpus, seeds, fingerprints, normalizações e consultas físicas. Espaço de busca extensível e versionado:

- Atributos originais e projeções homologadas: nome, nome da mãe, sobrenomes, nascimento e novos campos futuros; cada atributo declara semântica, ausência, normalização, comparadores, materialização e elegibilidade.
- **Quantidade de caracteres:** comprimentos de prefixo para pessoa e mãe, prefixos de prenome, iniciais, último sobrenome e outras combinações; recuperar varreduras históricas de 1 a 4 caracteres e prefixo3, mas não fixar 3 como ótimo universal. Tratar nomes curtos/ausentes sem chave curinga e sem truncamento silencioso.
- Passes e união deduplicada, ordem e combinações; restrições de índices, fanout e orçamento de consultas; medir ganho incremental M/U e latência SQL física.
- Distribuições M/U condicionadas à união efetiva de candidatos, dependência entre evidências nominais, TF com unidade e baseline compatíveis, prior, limiares e abstenção. Não multiplicar marginais dependentes nem atribuir TF inexistente.

Novos atributos entram pelo catálogo versionado de projeções/comparadores e seus hiperparâmetros; **não** passam a integrar snapshot publicado sem nova calibração manual e validação.

## Protocolo de reprodução e certificação

- Reconstruir exatamente o frame histórico e os denominadores, seeds, perfis de erro e versões de algoritmos. Reproduzir contagens M/U, recall de blocking, fanout, seleção de passes e hashes. Referências históricas aproximadas de **95,0044% (plano progressivo de dez células TRAIN, 19.436/20.458 no universo global sem CPF)** e **95,0582% (união de 19 células TRAIN, 19.447/20.458 no universo global sem CPF)** são **regressões de blocking**, não certificação de FS/FDR, nem metas a perseguir por ajuste indevido. O mesmo numerador 19.447 sobre o subconjunto elegível 20.075 produz **96,87%**, mas esse denominador não substitui o universo global na meta operacional.
- TRAIN estima e explora; VALIDATION seleciona sob orçamento estatístico previamente versionado, considerando multiplicidade. **O TEST das seeds 42/43/44 publicado no veredito SQL de 13/10 está consumido como holdout histórico**, porque TP/FP/FDR já foram observados durante o desenvolvimento. Ele permanece imutável como evidência, mas não pode ser reutilizado como TEST final da V9 recalibrada. A certificação futura deve congelar um **novo TEST não observado**, sem refit nem escolha posterior de parâmetros. Registrar LCB de recall, UCB de FDR, amostra efetiva por identidade/estrato, abstenções, FP, FN, custo e limites. Se não houver suporte ou aprovação, declarar INCONCLUSIVO/REPROVADO.
- Comparar novo C# com implementação independente e com os artefatos originais; divergência exige explicação rastreável, mesmo se percentual final for semelhante. Ganhos superiores são aceitos somente com protocolo equivalente e sem vazamento.
- SQL experimental apenas no `JornadaHarness` efêmero; não alterar Gold, IBGE original, JornadaLocal, NODE/NAS, HML/PROD; remover calibradores legados somente com testes de regressão e sem tocar dados operacionais. Merge exige CI completa no SHA e revisão, sem equiparar CI verde a certificação estatística.

## Referências de implementação e documentação

- [Especificação do Calibrador FS](Calibrador_FS_Specification.md)
- [Blocking dinâmico compartilhado](Linkage_Dynamic_Blocking.md)
- [Avaliação de comprimento de prenome/inicial materna](V9_Avaliacao_Prefixos_Prenome_Inicial_Mae_Camadas_20261014.md)
- [Prefixos pessoa/mãe e SQL](V9_Prefixos_Nome_Mae_Combinacoes_SQL_20261014.md)
- [Issue de implementação #1089](https://github.com/lucianox777/Jornada/issues/1089), [issue de qualidade #999](https://github.com/lucianox777/Jornada/issues/999), PRs experimentais #1087/#1088.

**Regra de precedência:** documentos históricos continuam válidos como descrição do estado e das evidências de sua época; qualquer prescrição conflitante de calibrador paralelo, adaptação de política em runtime ou recalibração automática é superada por esta decisão.

## Registro concreto dos melhores resultados em JSON — 18/10/2026

Os resultados históricos não devem permanecer apenas em conversas ou
relatórios dispersos: a configuração experimental versionada
[`config/linkage/v9-bootstrap-experimental-candidates.json`](../config/linkage/v9-bootstrap-experimental-candidates.json)
contém os números observados por perfil, parâmetros de seleção
comprovados e referências documentais. Seu **estado inativo é
intencional**: esses dados pertencem aos *candidatos ao bootstrap*,
não constituem os pesos, m/u, TF, prior, limiares ou regras completas
certificadas que o Worker deverá carregar em `ref`.

**Resultados explicitamente registrados:** seed42, 30 mil identidades
por perfil, `clean` com 2 passes nomeados (`P_PHON_M_LAST`,
`P_PREFIX3_DOB`), recall 19.630/20.364 em TRAIN,
6.509/6.763 em VALIDATION, 242/28 U; `correlated` com
nove passes reportados, 19.437/20.458 TRAIN,
6.432/6.714 VALIDATION, 516.029/59.376 U. **A ordem completa dos nove passes do plano `correlated` foi
recuperada e congelada** do artefato CI `11926175227` do workflow
`38628254862`, com SHA dos relatórios e fonte; ambos os perfis têm
`signatureComplete=true`. Isso resolve rastreabilidade dos passes,
**não** resolve a certificação da política.
O LCB unilateral TRAIN do `correlated` (~94,678%)
permanece abaixo de 95% apesar do recall pontual de 95,009%.

**Não migrar esses números automaticamente para
`LinkageParameters:AlgorithmVersion` ou defaults do appsettings
operacional.** O registro de resultados em JSON resolve a dívida
de perda de evidência básica, não a publicação de um snapshot
`ref` íntegro, do bootstrap único nem a ativação V9/DT15.
O histórico de 43,1581% do velho ensaio ATIVO+C1 era do
`JornadaHarness` **sintético** com TF desligada,
não do banco real da prefeitura; o texto histórico é
preservado e anotado, mas não caracteriza o modelo V9 corrigido.


## Emenda superveniente de 18/10/2026 — perfil inicial e unidade de decisão

A massa sintética **clean** passa a ser **única fonte automática de bootstrap inicial** V9 em DEV, **não** um estimador empírico da qualidade brasileira. Os perfis `correlated`/`independent`/`field` permanecem apenas em arquivos históricos e reprodução manual; não selecioná-los em novas calibrações nem exigir CI correlated. O JSON `v9-bootstrap-experimental-candidates.json` adicionado pela PR #1151 (merge `2111ad4`) é **inventário histórico**, `active=false`, sem parâmetros publicados em `ref`.

A seleção do calibrador deve usar decisões Rank→Resolve **por observação-fonte**, com candidatos formados por **identidade** (não observação), margem entre identidades distintas, FDR posterior por grupo com UCB válido e separação de `CONFLITO`/abstenção. A PR #1152 contém execução SQL Server física **sintética** com esses diagnósticos, mas o grupo `ORACLE` usa a verdade e `BLIND` usa tripla exata; nenhum deles prova Gold operacional, seletor de produção integrado ou TEST novo. Não trocar controle de conflito por margem zero só porque houve 0 FP sintéticos. Resultados e pontos pendentes: [V9_Reconciliacao_Decisoes_Evidencias_Runners_20261018.md](V9_Reconciliacao_Decisoes_Evidencias_Runners_20261018.md).

**Atenção à palavra “bootstrap”:** a geração/calibração **inicial** do snapshot é evento explícito separado da **subida da aplicação**; a subida **consome/valida** um snapshot aprovado em `ref` e jamais dispara novo ajuste FS. Parâmetros de busca (orçamentos, espaço, tolerâncias) não são passes, caracteres e pesos efetivamente calibrados. Nenhuma configuração inicial sintética, nem CI verde, autoriza DT-15, HML ou Produção.
