# Auditoria retrospectiva de rastreabilidade — issues e PRs (22/11/2026)

**Estado:** inventário inicial verificável; não é atestado de exaustividade nem autorização de fechamento em lote. **Governança:** GOV-DOC-01. **Repositório:** lucianox777/Jornada.

## Motivação, método e limites

Após a decisão do PO de consolidar ou encerrar issues antigas, verificou-se que alterar o escopo da #408 diretamente ocultava a distinção entre requisito histórico, PR integrada e decisão superveniente. Reconstrução baseada em leitura das issues atuais, busca de PRs por número de issue, corpo das PRs e conferência de estado de PRs selecionadas. **Busca textual não prova relação causal:** menções incidentais, comentários de testes ou nomes coincidentes são classificados como *indícios*, não como implementação. A API disponível nesta sessão não expôs o histórico de eventos de edição de cada issue; portanto, **não se afirma reconstrução integral das versões antigas da descrição**. O corpo original da #408 foi preservado sob aviso de substituição na própria issue. Não foram fechadas PRs ou issues.

## Matriz de vínculos verificados

| Issue | Escopo original / decisão posterior | PRs com vínculo direto identificado | Estado conferido | Encaminhamento e ressalva |
|---|---|---|---|---|
| #392 | Motivos de ausência de CPF; PO aprovou encerramento | #427 (contrato Pessoa v5, compartilhada com #393/#402) | #427 integrada | Verificar gate contratual/3.71 antes de fechar; PR compartilhada não é exclusiva |
| #393 | Natureza tipada territorial; PO aprovou encerramento | #427 | integrada | Mesmo gate 3.71 |
| #402 | CNH e RG parcial; PO aprovou encerramento | #425, #427 | ambas integradas | Mesmo gate 3.71 |
| #401 | Fingerprint causal da divergência; PO aprovou encerramento | #722, #724 | ambas integradas | Verificar execução do runtime e testes |
| #411 | Coordenar trem estrutural 3.71; PO quer fechar e deixar #414 aberta | #425, #427, #722, #723, #724; #490 cita #411 em implementação de métricas | cinco PRs estruturais integradas; #490 integrada | Coordenadora transversal: não fechar sem validar rebind, instalador, manifest e dependência #414. #723 contém fatia histórica #408 |
| #414 | Beneficiário x responsável; PO determina manter aberta | #722 (estrutura), #995/#996/#997/#1351 (menções relacionadas, requerem inspeção funcional individual) | #722/#995/#996/#997/#1351 integradas | Implementação parcial não comprova aceitação funcional |
| #408 | Original: POSSIVEL + apresentação/atos humanos; decisão superveniente: **não permitir confirmação/rejeição/autorização humana de vínculo; corrigir na origem e reavaliar nova carga** | #723 (declara implementar fatia estrutural #408); #410 documenta POSSIVEL como planejado | #723 e #410 integradas | **Issue foi reescrita em 22/11 e mantida aberta como auditoria técnica; requer correção da rastreabilidade:** restaurar escopo histórico como registro e separar auditoria de remoção em issue própria antes de fechar. A implementação #723 permanece no histórico, não se desfaz por fechar PR |
| #424 | Medir rescoring/storage; PO aprovou absorção na otimização | #488, #489, #490 | três integradas | Transferir critérios de medição ao destino explícito (#418 é candidata, verificar aceite), referenciar PRs e só então fechar |
| #409 | Métricas reais de CPF, qualidade e regularização; PO aprovou absorção no Monitor | Nenhuma PR de implementação inequivocamente atribuível na busca textual; #1357 trata documentação de rótulos reais | #1357 integrada | Identificar issue Monitor de destino e transferir denominadores/critério de dados reais; não declarar execução de medições |
| #404 | Invariantes, diagramas, documentação; PO aprovou absorção na governança documental | #410, #527 | ambas integradas | Conferir entregáveis residuais e destino antes de fechar |
| #405 | Hardening segredos; PO pediu auditoria residual | #412, #422, #457, #458, #459, #462–#487 (série de segurança; verificar cada relação no inventário específico) | #422, #458, #465, #486 integradas; demais não conferidas individualmente nesta rodada | Documentação registra triagem de histórico, literais e credenciais remanescentes; **não** fechar em lote |
| #416 | Calibrador sintético DEV; PO aprovou fechamento condicionado | #417, #426, #428–#433, #436–#440 e correções #441–#447 | #426, #429, #432, #433, #437, #438, #440 integradas; demais não conferidas nesta rodada | Provar critérios completos; não confundir ensaio DEV com TRAIN científico |
| #434 | Regras brasileiras de nomes; PO aprovou fechamento condicionado | #435, #436 | ambas integradas | Confirmar cobertura e testes |
| #413 | Classificar sentinelas sem destruir original; PO manteve aberta | Nenhuma PR direta comprovada nesta rodada | — | Sem mudança de escopo ou encerramento |
| #31, #79, #449 | Pendências preservadas pelo PO | Fora da amostra de PRs desta rodada | — | Continuam abertas |

## PRs e riscos transversais

- **#723** foi integrada em 03/10/2026 e declara explicitamente que só persiste a apresentação POSSIVEL, sem ativar POSSIVEL operacionalmente. **Não** confundir registro de apresentação/ato com autorização operacional comprovada.
- O runtime atual contém `Solution/src/Jornada.Api/IdentityCorrectionService.cs` chamando `identidade.sp_aplicar_correcao_identidade`, e `Solution/docs/Identity_Decision_Ledger.md` descreve confirmação institucional. É preciso auditar se são caminhos operacionais incompatíveis com a decisão superveniente do PO; não afirmar sem teste que estejam ativos em produção.
- **#410** integrada em 21/09/2026 diz que POSSIVEL era estado planejado na época. **#723** integrada depois criou estruturas, não comprovando ativação.
- **#488/#489/#490** demonstram implementação parcial da #424. Absorção administrativa não apaga essas entregas.
- Busca por "#401", "#404", "#405" ou "#409" também retorna PRs com referências incidentais; **não** classificar todas como dependentes nem encerrar por correspondência textual.
- **PR #1844** é documental e está aberta. Não confundir aprovação do PO com merge, nem PR aberta com requisito abandonado.

## Regras de reconciliação antes de qualquer fechamento

1. Recuperar timeline de edições de título/corpo da issue via API de eventos/auditoria do GitHub ou export verificável; registrar datas, autor, diff e texto anterior. **Não recuperado nesta rodada**.
2. Para cada PR aberta encontrada, inspecionar corpo, arquivos alterados, commits, checks e vínculos múltiplos. Distinguir: integrada, fechada sem merge, aberta necessária, aberta parcialmente útil e aberta obsoleta.
3. PR integrada é evidência histórica imutável; **não tentar fechar/desfazer PR integrada**. PR aberta só deve ser fechada após provar que todo seu escopo é obsoleto e não atende outra issue.
4. Para issue substituída, manter registro histórico e abrir issue sucessora para a nova obrigação, com links bidirecionais, antes de encerrar a antiga. Prioridade: #408.
5. Para issue absorvida, identificar issue destino, copiar critérios e referências de PRs, registrar precedência e só depois encerrar a origem. Prioridades: #424, #409, #404.
6. Para issues estruturais #392/#393/#401/#402/#411, validar instalação/rebind 3.71, testes e dependência #414 antes do fechamento; o aceite do PO é necessário, mas não substitui evidência.
7. Integrar documentação e índice GOV-DOC-01 antes de considerar encerramento concluído. Preservar links, datas e justificativas.

## Decisões e precedência

**PO, 22/11/2026:** #424 → otimização; #409 → Monitor; #404 → governança documental; #413 e #414 continuam abertas; #392/#393/#401/#402/#411 podem encerrar após verificação; #408 proíbe decisão humana direta e exige reavaliação por nova carga; #405 exige inspeção residual. A decisão de #408 **substitui** as regras anteriores de confirmação humana como requisito funcional, mas **não apaga** a PR #723 ou os registros auditáveis anteriores.

**Estado de execução:** inventário inicial registrado nesta branch; ainda faltam timeline de edição, mapeamento exaustivo das PRs abertas, identificação dos destinos e integração do registro. Não há autorização para fechamento indiscriminado de PRs.
