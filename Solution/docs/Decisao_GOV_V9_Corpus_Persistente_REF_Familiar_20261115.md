# GOV-V9-CORPUS-PERSISTENTE-REF-FAMILIAR-20261115

**Autoridade:** Product Owner, 15/11/2026: "Concordo, reavaliei toda a conversa que a gente teve, os pontos de convergência e pode implementar a mudança."

**Estado:** APROVADO; implementação em andamento, **não integrada**. GOV-DOC-01.

## Contexto e decisão
A população sintética operacional é descartável, mas regenerações podem desperdiçar tempo e dificultar comparações. O PO aprovou tornar o corpus sintético **persistente, versionado e reutilizável**, com preparação automática no workflow V9, preferencialmente em job PREPARE independente, antes do TRAIN físico. A decisão de 14/11 que dispensava PREPARE por ganho de ~4min10s permanece válida para o cenário anterior; esta decisão a supera **somente para a preparação de corpus persistente**, cuja economia e transferência devem ser medidas.

1. Na inicialização, calcular fingerprint canônico das dependências de geração: versão de código/regras, população, seed, REF demográfica IBGE por hash, REF familiar por hash, perfis de corrupção (nome, nome da mãe, nascimento, ausências, CPF/CNS) e partição TRAIN/VALIDATION/TEST. **Não incluir parâmetros de otimização que não afetem a geração.**
2. Se houver corpus íntegro e compatível, reutilizá-lo. Se ausente ou incompatível, gerar nova versão, validar, persistir atomicamente e publicar recibo. Não sobrescrever corpus associado a campanha anterior. Hash da configuração e hash do conteúdo são distintos; corrupção falha fechado.
3. O PREPARE e o TRAIN devem usar armazenamento compartilhável entre jobs e execuções, **não depender do disco efêmero do runner**. O handoff valida hashes. Jobs GitHub-hosted possuem teto nativo de 360 minutos **por job**, não por workflow; o PREPARE não pode consumir o relógio do TRAIN. Transferências consomem tempo do job receptor.
4. Permitir parametrizar degradação do nome da mãe. O valor **13,4% é candidato exploratório, NÃO taxa IBGE validada e NÃO novo eixo de otimização**. Preservar o padrão histórico 10% até benchmark e aprovação da taxa efetiva. Registrar discordância observada por pares e tentativas sem efeito.
5. Incorporar relações familiares realistas (mães, irmãos, gêmeos e datas coerentes) **somente após** congelar dados estatísticos observados e parâmetros explicitamente inferidos na REF; priorizar IBGE para indicadores equivalentes, SINASC como complemento. Não inventar distribuição municipal.
6. Manter 100 mil pessoas, seed rastreável, 324 hipóteses TF4, 81 células, scorer, TF, m/u, FDR/CP/P95, TRAIN/VALIDATION/TEST segregados, TEST lacrado e todos os gates de segurança. Não modificar produção.
7. Comparar tempo de geração/transferência, pares candidatos, P95 e integridade antes de promover o novo fluxo. Não executar campanha científica 100k sem autorização operacional separada.

## Riscos, precedência e rastreabilidade
Armazenamento persistente e transferência entre jobs têm custos, risco de expiração e de contaminação entre campanhas. O corpus só pode ser promovido com manifestos completos, SHA-256 e validação de partições; não usar cache mutável como verdade. Preservar provas históricas e a REF IBGE existente. Referências: `Decisao_GOV_V9_Timestamps_Job_360_20261114.md`, `Decisao_GOV_V9_TF4_Jobs_Independentes_Timeout_Nativo_20261114.md`, `DC-SYN-01-E2_Referencia_Diaria_Nascimento_Projecao_IBGE.md`, `Gate_Inicializacao_REF_IBGE.md`.

## Critérios de aceite
- Cache hit sem regeneração; cache miss gera uma única versão; mudança de parâmetro/REF invalida; mudança só de otimização não invalida.
- SHA de conteúdo adulterado falha fechado; publicação incompleta não é reutilizada; concorrência não produz versões conflitantes.
- PREPARE → TRAIN com prova de download íntegro em runners diferentes; medir custo de handoff.
- Distribuições familiares auditadas e congeladas, sem misturar hipótese e observação; mãe 13,4% não é aplicada como fato antes da validação.
- CI 3/3, testes específicos e PR integrada; execução física 100k posterior autorizada separadamente.

**Registro de implementação:** a criação deste documento e a atualização do índice constituem apenas a primeira etapa de implementação; **não atestam** entrega do cache, job PREPARE ou gerador familiar.

## Incremento controlado da PR #1567 — 15/11/2026 (WIP, não mesclar)

A branch de trabalho foi **sincronizada com a master do Processor até #1571** sem integrar a PR de corpus à master. A união preservou ambos os históricos e reconstituiu o índice de decisões (nenhum código produtivo V9 foi substituído).

**Implementado como prova técnica NÃO científica:**
- `v9-corpus-handoff-verify.py` verifica independentemente o handoff PREPARE com SHA da configuração, allowlist de quatro arquivos, tamanho, SHA de conteúdo e recibo gerado/reutilizado, rejeitando conteúdo, metadata, recibo ou symlink adulterados. Novos testes sintéticos de transferência e falha fechada, sem executar o calibrador.
- O workflow **manual opt-in da própria branch** tem job de consumo em runner distinto (`train-handoff-probe`) ligado por `needs: [prepare-corpus]` e artifact **do mesmo `github.run_id`**. A admissão exige SHA exato, branch exata, ator proprietário e tentativa única; valida os bytes outra vez no consumidor. Este job é só **PROBE DE TRANSFERÊNCIA**, não inicia `physical-100k`, não executa as 324 hipóteses e não abre TEST científico.
- O recibo de entradas suporta registrar experimentalmente `mother_corruption_rate_override` com `EXPLORATORY_NOT_OBSERVED_IBGE`, distinguindo 10% explícito, 13,4% hipotético e o valor omitido. **A geração com taxa variável NÃO está implementada.** Se o operador fornecer qualquer override no workflow opt-in, ele rejeita a operação **antes da geração** com `V9_MATERNAL_RATE_EXPERIMENTAL_NOT_IMPLEMENTED`. Por omissão, a taxa clean histórica de 10% segue inalterada.
- A tentativa de modificar diretamente `Jornada.Linkage.SyntheticCorpus` foi revertida após a CI revelar que aquele código é fonte de fingerprints científicos **congelados para seed42/43/44**. Esses contratos não devem ser reescritos nem afrouxados. Uma variante experimental isolada será necessária para taxas diferentes (incluindo 13,4%); a taxa não poderá aparecer como empiricamente observada sem REF aprovada.
- A CI do contrato `v9-corpus-persistence-contract.yml` limita-se aos testes `test_v9_corpus_*.py` e ao guard de workflows, enquanto CI obrigatória já mantém os checks de segurança, compilação e testes gerais necessários. Não afrouxar locks de outras campanhas para melhorar artificialmente a CI deste protótipo.

**Ainda não atendido:** durabilidade real entre execuções (Actions cache pode expirar), execução **física** do handoff entre runners após PREPARE manual, validação de desempenho/custo de transferência, parametrização operacional segura da taxa materna em gerador versionado, estatísticas oficiais familiares IBGE/SINASC com valores quantitativos e distribuição auditada, integração no workflow principal TRAIN/VALIDATION, nem revisão final do PO. O TEST científico 100k permanece **lacrado, bloqueado e não consumido**, independentemente dos nomes dos diretórios usados no ensaio. A PR #1567 permanece expressamente **aberta e NÃO MESCLÁVEL por decisão de governança**, ainda que os conflitos técnicos Git tenham sido resolvidos. A seleção produtiva de V9 segue bloqueada.
