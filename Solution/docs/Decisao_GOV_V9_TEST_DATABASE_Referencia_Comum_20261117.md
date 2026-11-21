# GOV-V9-TEST-DATABASE-REFERENCIA-COMUM — 17/11/2026

**Estado:** decisão funcional aprovada pelo Product Owner; implementação pendente. **Rastreabilidade:** issue #1632. **Política:** GOV-DOC-01.

## Contexto e decisão
O parâmetro `test_database`, quando preenchido, identifica uma **referência estatística externa comum** a ser utilizada pelo gerador sintético de todas as coortes: TRAIN (seed 42), VALIDATION (seed 20261112001) e TEST independente (seed 20261113001). As coortes são geradas separadamente; não há compartilhamento de observações. Quando o parâmetro não for informado, permanece o uso das referências IBGE congeladas.

## Justificativa
Comparabilidade das distribuições sintéticas e consistência dos parâmetros de geração ao longo das três etapas, preservando avaliação em dados independentes.

## Implementação existente e lacuna
O executável `Jornada.Linkage.SyntheticCorpus` e comando `generate` já são comuns às três etapas, mas cada job invoca o gerador. Na `master` de 17/11/2026, `test_database` é aceito apenas pelo PREPARE TRAIN, onde provoca falha fechada porque o adaptador externo não existe. VALIDATION e PREPARE TEST lacrado não propagam o parâmetro. Portanto, esta decisão **não está implementada**.

## Contratos e impactos
**Esclarecimento do PO (17/11/2026): não exigir snapshot da `test_database` como requisito funcional.** A base externa deve estar acessível nas três etapas, que geram coortes distintas com o mesmo gerador e suas próprias sementes. Propagar a identificação/conexão da referência às três invocações e falhar fechado quando a referência externa solicitada estiver indisponível. A independência das coortes não depende de snapshot da referência; mudanças no conteúdo estatístico externo entre etapas podem, porém, alterar suas distribuições e prejudicar a comparabilidade. Registrar origem e versão quando disponíveis é recomendação de rastreabilidade, **não bloqueio por ausência de snapshot**. Não fazer fallback silencioso ao IBGE. O contrato de conexão/schema e as garantias de privacidade precisam ser definidos e testados antes da execução física.

## Precedência e limites
Esta decisão **complementa** a arquitetura V9 de TRAIN/VALIDATION/TEST e **amplia o escopo funcional pretendido** do parâmetro `test_database` para as três coortes; não revoga o lacre TEST antes do TRAIN, o TEST de exposição única, a seleção do vencedor exclusivamente na VALIDATION, nem os gates de segurança. A referência estatística comum não é uma base de observações compartilhadas. Sementes distintas não atestam ausência de identidades coincidentes. Nenhuma execução científica com referência externa está liberada até implementar e comprovar o adaptador.

## Reconciliação documental — 21/11/2026

Documento recuperado da PR #1633 para integrar a decisão aprovada pelo PO ao acervo oficial. O estado de implementação descrito acima é histórico (17/11/2026), **não** uma auditoria do runtime atual; confirmar contratos, adaptador e propagação antes de afirmar implementação ou liberar execução física. Nenhum gate científico, referência LOCKED ou workflow é alterado por esta recuperação.
