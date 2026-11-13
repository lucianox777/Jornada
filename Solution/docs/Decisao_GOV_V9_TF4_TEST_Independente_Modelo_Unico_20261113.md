# GOV-V9-TF4-TEST-INDEPENDENTE-MODELO-UNICO-20261113

**Autoridade:** Product Owner, 13/11/2026 (America/Sao_Paulo). **Estado:** APROVADA PELO PO; implementação em PR, sujeita a CI e prova física. **Regra:** GOV-DOC-01.

## Correção de conceito aprovada

O Product Owner determinou expressamente: **“a camada teste tem que ser totalmente independente, só deve receber como parâmetro o modelo vencedor.”** Esta decisão corrige interpretações anteriores que exigiam a passagem do plano de calibração, envelope com todos os finalistas, métricas de VALIDATION ou informações de ranking para o avaliador TEST. **Nenhum desses dados pertence à interface de TEST.**

Arquitetura estatística aprovada:
1. **TRAIN (100k, seed 42):** ajusta M/U e demais parâmetros das 324 hipóteses, avalia TRAIN e congela até 3 finalistas. Não abre TEST.
2. **VALIDATION (100k, seed 20261112001):** mede exclusivamente os finalistas imutáveis e seleciona o **único vencedor científico** `selectedValidationSnapshotSha256` segundo a política predefinida; produz seu próprio resultado. Não abre TEST.
3. **TEST (100k externo, seed 20261113001):** possui **sua própria coorte, seu próprio gabarito, ambiente SQL descartável e protocolo predefinido**. Recebe **um único parâmetro de modelo** — os bytes originais do vencedor `SHA.fitted.json`, com SHA-256 verificado. Não recebe nem lê plano TRAIN, seleção, finalistas perdedores, métricas de VALIDATION ou estatísticas necessárias à escolha. Executa **somente** o scorer/FS/Resolve do modelo congelado, sem novo ajuste/seleção e sem reestimar parâmetros.

### Separar independência da avaliação de orquestração técnica

A dependência do job TEST de um job VALIDATION bem-sucedido é apenas **ordenação e disponibilização do modelo vencedor**, não dependência estatística de suas medições. A VALIDATION entrega um artefato contendo **somente `<winnerSha>.fitted.json`**. O executor TEST recebe exatamente `--v9-independent-test <winnerSha.fitted.json>`; os caminhos internos da própria base TEST e do SQL estão definidos por variáveis de ambiente do runner TEST, não são parâmetros recebidos de TRAIN/VALIDATION. Se VALIDATION não selecionar vencedor, nenhum TEST é executado.

A base TEST é gerada no começo da campanha pelo job preparatório independente `prepare-test-100k`, cifrada e com manifesto fixado por SHA antes da análise dos resultados, sem reutilizar a base VALIDATION. Ao abrir TEST, o processo confere os hashes dos seus quatro arquivos. Os identificadores sintéticos `P000...` são locais à geração; toda identidade TEST recebe prefixo `EXTERNAL_TEST_<manifestSha>:`, e o gerador histórico ainda grava rótulos internos 60/20/20 que **não** se convertem em partições científicas do TEST: **toda a coorte TEST externa é avaliada na finalidade `TEST`**. Apenas observações sem CPF entram no scorer FS, com censo prévio das 100k pessoas-base e multiplicidade máxima 4.

O executor é independente do CLI `--v9-calibrate` e `--v9-validate-finalists`; usa a avaliação SQL física congelada com **uma** configuração, 20 repetições ponta a ponta para diagnóstico P95, corpus próprio, censo sem truncamento e resultados agregados. A comparação de **coerência** dos resultados TRAIN, VALIDATION e TEST ocorre **após** o TEST, em análise externa; o próprio TEST não consome métricas anteriores.

### Estados e limitações científicas

**O TEST independente mede, não seleciona nem ativa modelos**. `testUsed=true` indica avaliação efetiva somente se o job físico concluiu; `refitted=false`, `certified=false` e `activated=false` são invariantes. Coortes sintéticas com seeds diferentes garantem fontes de geração separadas, mas não demonstram IID, ausência de sobreposição em valores nominais ou validade externa populacional. O relatório explicita `independentlyCertifiedIid=false`; a passagem em testes automatizados não certifica o resultado.

**Risco residual importante:** o job e o protocolo bloqueiam `run_attempt != 1`, mas o registro global persistente de exposições de TEST entre campanhas ainda não foi implementado. A seed TEST fixa pode ser reusada inadvertidamente num `workflow_dispatch` futuro, o que invalida novas inferências confirmatórias sobre a mesma coorte. **Não afirmar teste certificado, não automatizar múltiplas avaliações sobre o mesmo corpus, e não promover a produção até resolver essa dívida**, registrada na [issue #1478](https://github.com/lucianox777/Jornada/issues/1478). As credenciais cifradas `V9_TF4_TEST_PUBLIC_CERT_B64` e `V9_TF4_TEST_PRIVATE_KEY_B64` e `expected_test_cert_sha256` devem ser providas por mecanismos de segurança do repositório; nenhum segredo fica no Git.

### Impacto, precedência e rastreabilidade

O TEST tem seu próprio limite de **360 minutos** (até 330 min na etapa física); TRAIN conserva 360/315 min, VALIDATION 360/330 min, e preparação do corpus TEST 60 min. Sem alterar 324 células TF4, M/U, limites CP/FDR, custos, políticas de ranking, desenho da VALIDATION, origem IBGE ou seeds já aprovadas. O TEST recebe o vencedor e seus próprios dados, **não** o plano/observações de origem.

**Precedência:** complementa e esclarece a autorização [GOV-V9-TF4-TEST-AUTORIZACAO-PO-GATES-20261113](Decisao_GOV_V9_TF4_TEST_Autorizacao_PO_Gates_20261113.md), substitui o hard-lock exclusivamente estrutural da [GOV-V9-TF4-3-JOBS-360-TEST-RESERVADO-20261113](Decisao_GOV_V9_TF4_Jobs_360_TEST_Reservado_20261113.md) **pela execução independente condicional e sem certificação**, preserva o isolamento cifrado de [GOV-V9-TF4-TEST-GERACAO-ANTECIPADA-ISOLADA-20261113](Decisao_GOV_V9_TF4_Geracao_Antecipada_TEST_Isolada_20261113.md) e os requisitos de LOCK pré-seleção do vencedor. A seleção na VALIDATION continua condição para haver modelo vencedor; não é entrada estatística do TEST.

**Implementação:** `Solution/src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs`, `V9IndependentTestCorpusReader.cs`, `V9PhysicalSqlFrozenHoldout.cs`, `V9FrozenSourceResolveReplay.cs`, `Program.cs`, workflow `.github/workflows/v9-v3-four-name-100k.yml`; teste de contrato `Solution/tests/scripts/test_v9_tf4_standalone_test.py`. **Gates:** CI obrigatória, compilação dotnet Release, testes V9 de separação, segurança, squash merge, inspeção de SHA master e prova física independente sob owner-only e preflight. Sem execução real, não declarar resultados de TEST.
