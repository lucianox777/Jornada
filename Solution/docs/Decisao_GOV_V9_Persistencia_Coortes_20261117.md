# GOV-V9-PERSISTENCIA-COORTES-20261117 — decisão PO e avaliação de implementação

**Autoridade:** Product Owner, 17/11/2026: avaliar e implantar retenção das bases geradas em VALIDATION e TEST independente, reaproveitando o mecanismo de PREPARE. **Estado:** requisito aprovado; implementação de persistência segura ainda pendente de gates técnicos. **Política:** GOV-DOC-01.

## Diagnóstico verificado no código (master)
O workflow vigente é `.github/workflows/v9-standard-linear-100k.yml` (não o workflow legado `v9-v3-four-name-100k.yml`).
- PREPARE gera corpus TRAIN seed 42, usa `v9-standard-prepare-handoff.py pack`, `train-inputs.tar.gz`, SHA256SUMS e artifact `v9-standard-train-prepared-*` com retenção 30 dias. **É um arquivo de corpus, não backup de SQL Server.**
- VALIDATION gera holdout independente seed 20261112001 no próprio job, publica somente vencedor, status e métricas agregadas, e apaga `validation-people`.
- TEST gera corpus seed 20261113001 **somente após** reserva global da tag one-use e vencedor VALIDATION; publica apenas resultado agregado e apaga corpus e SQL descartável.
- Ambos criam SQL descartável, removido ao final; não existe backup SQL restaurável no fluxo.

## Decisão de arquitetura e precedência
Reaproveitar de PREPARE **a técnica** de empacotamento determinístico, recibos SHA256, identidade de código/run e retenção governada, mas **não** copiar literalmente o artifact plaintext de TRAIN para VALIDATION/TEST: isso ampliaria acesso ao gabarito independente e poderia invalidar o teste one-use. Persistência deve ser cifrada, com acesso segregado, retenção mínima, prova de restauração e proibição de download por TRAIN/seleção; armazenamento GitHub Actions de 30 dias é evictável e não substitui durabilidade garantida.

A escolha entre preservar o corpus de observações (CSV + gabarito) e preservar um backup físico de SQL deve ser explicitada na implementação: PREPARE faz a primeira coisa, não a segunda. Não chamar arquivo tar de backup SQL.

## Critérios obrigatórios de implementação
1. Criar pacote independente por fase, com seed e SHA de gerador/código, manifesto de arquivos, tamanhos e hashes; não publicar plaintext de VALIDATION/TEST como artifact acessível aos jobs de TRAIN.
2. Usar criptografia autenticada e chave privada ausente dos jobs de TRAIN/VALIDATION/TEST; declarar operador autorizado e política de recuperação. Nunca incluir chave no artifact ou log.
3. TEST: persistência **após** reserva global one-use, sem permitir repetição/replay de scoring, regeneração ou retentativa; preservar o modelo vencedor pinado e a barreira de paridade.
4. Verificar restauração e negativos de adulteração, troca de seed/run/commit, vazamento de gabarito e tentativas de reuso. Testar com corpus sintético minúsculo, **sem executar seed TEST real**.
5. Política de retenção, ACL, custo e backend durável compatíveis com a decisão vigente de não persistir bases brutas permanentemente. Falha de persistência não deve silenciosamente liberar TEST.
6. Não confundir retenção de corpus com certificação de paridade operacional do Runner; #1575/#1584 e `productionRunnerFullPipelineParityCertified=false` exigem evidência própria.

## Impacto e limites
Nenhuma alteração na matemática TF4, nas 324 hipóteses, no vencedor, nas seeds ou na separação TRAIN→AUDIT→VALIDATION→TEST. **Não habilitar TEST científico** até demonstrar a persistência segura, a restauração e os gates de paridade. Implementar por PR técnica separada com CI e revisão de segurança. A solicitação de salvar as bases não é autorização para consumir a seed TEST.
