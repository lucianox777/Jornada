# Execução técnica — TEST científico V9 (issue #1584)

Status: **implementação em andamento; sem certificação científica**.

## Limite desta PR

Esta PR trata exclusivamente da implementação integral do TEST científico V9, separada da PR #1820, que reconcilia o contrato e os testes de regressão existentes. Base inicial: master; após o merge da #1820, reconciliar a segunda PR com master antes de integrar.

## Contrato obrigatório

1. Entrada de modelo: somente o artefato operacional SHA-pinned emitido pela VALIDATION; nenhuma leitura de métricas TRAIN/VALIDATION para escolher ou ajustar o vencedor.
2. Verificar SHA, esquema TF4/81, projeção, regras de blocking, limiares, agrupamento e round-trip operacional antes de acessar a coorte TEST.
3. Executar exclusivamente em SQL descartável JornadaHarness e coorte TEST independente; sem execução do TEST no Runner produtivo, sem ativação, publicação ou refit.
4. Se VALIDATION entregar vencedor válido, TEST deve ser obrigatório; caso contrário, CLOSEOUT explícito sem TEST. Preservar o ledger de uso único.
5. O parâmetro `test_database` é referência estatística opcional do PREPARE, não é entrada do TEST. Preservar fail-closed onde o adaptador ainda não existe.
6. Emitir relatório e CLOSEOUT auditáveis, com evidência de integridade, isolamento e privacidade; não declarar certificação ou paridade física sem comprovação.
7. Preservar a retrocompatibilidade do Runner legado e o V9 opt-in; não exigir equivalência de métricas entre coortes distintas nem execução produtiva do TEST.

## Sequência verificável

- [ ] Reconciliar esta branch após o merge da PR #1820.
- [ ] Corrigir dependências de runtime SQL de modo que o artefato congelado seja a única autoridade do modelo, mantendo verificação byte-exata de conversão.
- [ ] Corrigir caminho legado de workflow incompatível com a CLI científica, sem permitir substituição silenciosa de TEST.
- [ ] Completar os gates automáticos de admissão, relatório e CLOSEOUT sem remover bloqueios sem evidência.
- [ ] Adicionar testes unitários, contratuais e de isolamento e executar CI obrigatória.
- [ ] Atualizar os documentos oficiais e `Solution/docs/Indice_Decisoes_Vigentes.md` conforme GOV-DOC-01 quando houver decisão nova/aprovada; preservar precedências.
- [ ] Verificar evidências físicas autorizadas antes de declarar certificação; **não iniciar campanha 100k ou consumir TEST one-use apenas para integrar esta PR**.

## Rastreabilidade e precedência

Issue #1584; PR #1820; `Solution/docs/Decisao_GOV_V9_TEST_Isolamento_Retrocompat_Liberacao_20261117.md`; `Solution/docs/Decisao_GOV_V9_TEST_Projecao_Congelada_20261118.md`; `Solution/docs/Indice_Decisoes_Vigentes.md`. Este plano não cria nem substitui decisão de governança.
