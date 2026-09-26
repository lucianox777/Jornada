# Bootstrap nominal de u — município de São Paulo (decisão de 26/09/2026)

**Status:** método SP aprovado para implementação/ensaio, **não aprovado para ativação HML**; critérios detalhados em [decisões centrais](Decisoes_Linkage_Calibracao_IBGE_20260926.md).

**Fonte:** snapshot local imutável `CENSO2022_NOMES_BRASIL_V1`, arquivo `projection/frequencia-municipio.ndjson.gz`; o manifesto contém 4.088.822 linhas de **todos** os municípios, não só SP. Filtrar `UF=35` e `municipio_codigo=3550308`, com verificação do hash original, tipo, sexo e todas as linhas municipais pertinentes. Não criar novo `referenceCode`.

**Sem fallback geográfico automático:** nunca preencher um nome não publicado/suprimido no município com frequência de UF ou Brasil. Ausência não é frequência zero. Se a marginal municipal necessária não estiver disponível ou cobertura estiver abaixo do critério técnico medido, marcar `MARGINAL_INSUFICIENTE` e bloquear o derivado SP. Os dados nacionais servem *somente* como controle estatístico em experimento distinto.

**Cauda:** manter no sorteio de bootstrap todas as frequências SP publicadas positivas, inclusive `k<25`. O parâmetro versionado `k_min=25` é proposto para estimativas **individuais** de term frequency, não para excluir nomes raros e renormalizar a distribuição. `RSE≈1/sqrt(k)` é heurística preditiva Poisson, não erro amostral do Censo. Células suprimidas recebem classificação explícita de desconhecidas; categoria agregada OOV só quando houver denominador municipal confiável, método versionado e teste que proíba `u=0`/LLR artificialmente infinito. Caso contrário, fail-closed, não fallback.

**Marginais requeridas:** `NOME/TODOS` municipal; `NOME/FEMININO` para mãe, se publicado; e `SOBRENOME/TODOS` municipal. Verificar disponibilidade efetiva antes da codificação/ativação. Persistir derivado SP V2 e nacional V1 com identidades independentes por fonte/hash/município/método/contrato/sexo/seed/pares/política de cauda; ENSURE idempotente, imutável, explícito antes da Entrega e sem recalcular no boot.

**Evidência antes de ativar:** analisar 100% das linhas de SP; medir nomes únicos, massas publicadas, `k>=25`, `0<k<25`, supressão e cobertura; TVD municipal versus Brasil na união *integral* de chaves, marginais e denominadores compatíveis, Jensen–Shannon opcional. Medir cobertura no **corpus inteiro** com/sem CPF: porcentagem dos nomes e das mães por classe de suporte/ausência, inclusive não resolvidos. Comparar nacional e municipal em avaliações distintas do mesmo corpus com TRAIN/VALIDATION/TEST congelados, FP por classe, FN, recall, PPV, conflitos, abstenção e custo. Divergência de nomes por si só não prova melhoria.

**Fora do escopo:** `m` vem de pares rotulados reais; `u` condicionado ao blocking continua calculado por modelo, distinto da referência nominal IBGE. `MaxCandidatePairs=10.000.000` é limite de outro componente. Orçamento FP e threshold não se alteram automaticamente; avaliação populacional #31 e aprovação HML são independentes.

**Referências:** [Plano](Plano_Desenvolvimento.md), [ADR-002](../../Documentos/ADR/ADR-002-calibrador-fs-u-condicionado.md), [ADR-003](../../Documentos/ADR/ADR-003-corpus-sintetico-nomes-frequencia-ibge.md), [PR #498](https://github.com/lucianox777/Jornada/pull/498).
