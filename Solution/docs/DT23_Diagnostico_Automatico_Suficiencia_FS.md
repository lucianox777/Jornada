# DT-23 — Diagnóstico automático de suficiência amostral para recalibração FS

**Decisão confirmada:** 09/10/2026 · **Estado:** ABERTA / NÃO IMPLEMENTADA · **Prioridade:** após estabilização do bootstrap congelado de referências da PR #883 · **Natureza:** diagnóstico read-only, sem execução de calibração.

## Fronteira obrigatória

- **Automático:** acumular/consultar evidência real independente, calcular suficiência e emitir diagnóstico periódico com semáforo **VERDE / AMARELO / VERMELHO**, suportes e justificativas.
- **Manual:** iniciar a calibração FS por comando explícito de operador autorizado; `GENERATE_DRAFT`, `VALIDATE` e `ACTIVATE` não são disparados pelo diagnóstico. Ativação continua governada e auditável.
- **Inicialização:** carrega/verifica somente referências IBGE/demográficas congeladas em `ref`; **não** calibra, não recalcula projeções, não exige amostra suficiente para health/readiness.
- **Imutabilidade:** referências e calibrações históricas publicadas em `ref` não são sobrescritas. Um novo resultado é uma nova versão, após operação manual.

## Critérios técnicos de implementação

1. Definir elegibilidade da verdade independente por CPF e excluir da verdade rótulos derivados apenas do próprio linkage. Ocultar CPF das features ao avaliar o scorer.
2. Contabilizar `m` e `u` sobre o universo **efetivamente sobrevivente ao blocking**, incluindo deduplicação, suporte por passe/estrato, diversidade de pessoas/fontes e eventuais dependências entre evidências.
3. Parametrizar e versionar limites de suficiência, confiança e janela temporal. Os números exploratórios de 5.000/1.000 **não são gates homologados**.
4. Persistir ou expor snapshot de diagnóstico reproduzível: data de corte, versão da política, modelo ativo, contagens, estratos insuficientes, motivos, estado e proveniência; jamais dados pessoais em logs públicos.
5. Produzir semáforo operacional com justificativa objetiva e indicação de **oportunidade de recalibração manual**. VERDE significa evidência suficiente para iniciar avaliação, **não** autorização de promoção nem certificação automática de FDR.
6. Garantir execução idempotente, observabilidade, tratamento de indisponibilidade de fontes e testes de transição entre estados; falha de diagnóstico não deve derrubar API/Processor.

## Critérios de aceite

- Testes demonstram mudança do diagnóstico quando evidência independente elegível cresce, quando suporte por passe falta e quando a política versionada muda.
- Ausência de evidência retorna estado explícito de insuficiência/indeterminação, nunca falso VERDE.
- Testes provam que ciclos do Monitor, reinício da infraestrutura e subida de referências **não** invocam Calibrador nem `GENERATE_DRAFT`/`VALIDATE`/`ACTIVATE`.
- Exibição read-only no Monitor, com controle de acesso e evidência auditável; sem scheduler de calibração.
- Revisão do operador continua condição necessária para calibrar e promover.

## Relações

- [Especificação de evidência real e governança](Especificacao_Calibracao_Progressiva_Evidencia_Real_20260929.md)
- [Especificação FS corrente](Calibrador_FS_Specification.md)
- [Monitor Operacional](Monitor_Operacional.md)
- [DT-15 — governança](DT15_Governanca_Decisao_Modelo.md)
- [Decisões vigentes](Indice_Decisoes_Vigentes.md)

**Separação de escopo:** a PR #883 trata da carga congelada de referências e do vínculo/fingerprint; esta DT-23 é trabalho futuro independente. Não declarar a DT concluída com base apenas na documentação.
