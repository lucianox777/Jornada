# V9 — censo do fan-out do blocking SQL por observação elegível (somente diagnóstico)

## Por que esta medição existe

A issue #999 exige medir não apenas recall e número de candidatos M/U, mas o **fan-out por observação** e caudas P95/P99 antes de selecionar passes complementares. Um blocking com bom recall médio pode ser inviável por concentrar milhares de candidatos em observações comuns. A PR #1002 tornou possível procurar até quatro passes em modo opt-in; isso NÃO é uma autorização de promoção nem uma prova de custo no SQL. As PRs #1003–#1005 forneceram as perdas M, paridade C1+ATIVO e suporte conjunto TRAIN no **mesmo SQL físico**.

O teste atual cobre apenas o banco efêmero `JornadaHarness` em `DOTNET_ENVIRONMENT=Development`, com **seeds sintéticas 42/43/44 de 30 mil pessoas-base cada**, regras ATIVAS no harness mais C1, sem o histórico completo `VERSIONED_ALIAS`. Nada aqui estima fan-out populacional municipal de 11 milhões.

## Unidade correta, denominadores e implementação

`V9SqlCandidateFanoutAudit` é alimentado por **cada par distinto da mesma leitura real de `#v9pairs`**, já usada para censo M/U e pontuação FS congelada. Para evitar viés:

- **Denominador:** TODAS as observações do corpus que têm CPF ausente e nascimento presente, dentro de TRAIN, VALIDATION ou TEST, inclusive observações com **zero candidatos**. Ter apenas um gestor ou nenhum par inter-Gestor elegível não remove a observação deste denominador. Não confundir a observação com uma identidade-base.
- **Numerador de grau total:** cada par SQL (M ou U) soma exatamente +1 ao grau de cada uma das **duas observações**; a soma dos graus é exatamente `2×(M_sql+U_sql)` por split.
- **Numerador de grau falso:** cada par U soma +1 ao grau falso das duas pontas; soma exata `2×U_sql`. A separação entre M/U usa os IDs de pessoa-base **somente internamente ao harness**.
- A contagem `DistinctFalseBasePairs` colapsa pares U de observações em **duplas não ordenadas de identidades-base** e NÃO os chama de observações independentes, ESS ou unidades de cidadãos indevidamente vinculados.
- São calculados `WithCandidates`, `WithoutCandidates`, P50, P95, P99 e máximo dos graus totais; P95/P99/máximo dos graus U. Os percentis usam **nearest rank exato**, isto é, valor na posição `ceil(p*n/100)` no vetor de **todas** as observações elegíveis ordenado, zeros inclusos; NÃO interpolam, NÃO truncam e NÃO usam `TOP N`.
- Os dois histogramas de `Degree` e `Observations` por split permitem que a CI em Python recalcule de forma independente todos os percentis, incidências, zero-hit e caudas. Não exportam nomes, datas individuais, CPF, ordinais, IDs de base ou arestas.

`V9PhysicalSqlBlockingProbe` exige que M/U do fan-out sejam **idênticos** ao mesmo censo `Splits` no SQL. O próprio coletor rejeita candidato duplicado, fora do frame, com CPF/nascimento inelegível, gestores iguais, observações de splits distintos e erros de identidade/partição. O limite pré-existente de 1 milhão de pares SQL permanece **fail-closed**, sem amostragem oculta.

## O que fica pendente

Este é um diagnóstico **descritivo de grau e de risco de explosão de comparações**, não um gate estatístico para aceitar uma política. Fan-out baixo não demonstra FDR aceitável; fan-out alto deve ser acompanhado de tempo/custo de índices reais, seleção de regras apenas em TRAIN/VALIDATION, estimador do prior, `m/u` conjunto condicional, TF legitimado e prova de desempenho no TEST congelado. A CI verifica somente aritmética, proveniência e segurança dos cálculos; não autoriza ativação V9 nem DT-15.

**Proibições:** sem alteração de regras SQL operacionais, `Rank/Resolve`, JornadaLocal, IBGE original, NODE/NAS, Gold, HML/PROD, histórico V8 ou público. Sem inferência de risco populacional usando 30 mil ou de custo de 11 milhões por extrapolação linear.
