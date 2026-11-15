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
