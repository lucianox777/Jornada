# ADR-006 — NIS/CNIS como âncora determinística secundária

- **Status:** Aceita para a candidata técnica v5.00
- **Data:** 2026-09-21
- **Escopo:** resolução progressiva de Pessoa / contrato Pessoa v4
- **Issue:** #394

## Contexto

A Jornada já possui `pessoa_uuid` como identidade canônica interna e CPF como âncora determinística externa principal. O contrato v4 também preserva identificadores tipados por observação, mas NIS/PIS/PASEP/NIT ainda não participavam do runtime.

As fontes oficiais distinguem as origens NIT, PIS, PASEP e NIS, mas tratam esses números como inscrições da pessoa física no CNIS. O INSS orienta não criar nova inscrição quando já existe NIT/PIS/PASEP/NIS e prevê formação de **elo CNIS** quando há múltiplas inscrições. A própria regulamentação também reconhece situações críticas em que uma inscrição pode ter sido atribuída indevidamente a mais de uma pessoa. A CAIXA informa que PIS e NIS são, na prática, nomenclaturas do mesmo número em contextos diferentes.

Referências:
- INSS — Inscrição: https://www.gov.br/inss/pt-br/direitos-e-deveres/inscricao-e-contribuicao/inscricao
- Portaria DIRBEN/INSS nº 990, com alterações posteriores: https://portalin.inss.gov.br/portaria990
- CAIXA — Perguntas Frequentes Cadastro NIS: https://www.caixa.gov.br/servicos/nis/perguntas-frequentes/Paginas/default.aspx

## Decisão

### Hierarquia

1. `pessoa_uuid` é a identidade canônica interna.
2. **CPF é a âncora determinística externa principal e permanente.**
3. `UUID_JORNADA` permanece retroalimentação interna e não concorre na hierarquia de documentos externos.
4. **NIS é a âncora determinística externa secundária**, abaixo do CPF.
5. Linkage probabilístico permanece complementar quando as rotas determinísticas não resolvem.

O catálogo usa um único `tipo_identificador_codigo = NIS`. A procedência é preservada em `namespace_codigo = NIS | PIS | PASEP | NIT`. Namespace diferente não cria uma segunda âncora quando `valor_normalizado` é o mesmo.

### Elegibilidade

Um número social só é elegível para resolução determinística quando:

- possui 11 dígitos e passa pela validação estrutural local `NIS_BR_11_V1`; e
- a observação declara `statusEvidencia = COMPROVADO`, com `verificadoEm`.

`DECLARADO` é preservado em Silver e QC/BI, mas permanece `NAO_VALIDADO` e **não cria nem resolve UUID**. A validação estrutural local não prova titularidade nem situação cadastral no CNIS.

### Cardinalidade e conflito

- uma Pessoa pode possuir **0..N números sociais** comprovados;
- um mesmo número social corrente pode apontar para **no máximo uma Pessoa canônica**;
- o índice único corrente é aplicado a `identity_map(tipo='NIS', identificador)` enquanto `vigencia_fim IS NULL`;
- NIS não recebe uma tabela de âncora imutável equivalente a `cpf_ancora`, pois elos/correções legítimos precisam permanecer possíveis por fluxo governado.

Se um NIS comprovado já apontar para outra Pessoa:

- com CPF válido presente, o CPF continua resolvendo a observação;
- com `UUID_JORNADA` canônico presente e sem CPF, o UUID interno continua resolvendo;
- o `identity_map` do NIS passa a `EM_CONFLITO`;
- uma divergência governada é aberta;
- não há transferência, fusão ou escolha silenciosa de vencedor.

Sem CPF/UUID prioritário, NIS comprovados que apontem para UUIDs diferentes deixam a observação em conflito. NIS adicionais ainda não mapeados só são vinculados automaticamente quando todos os NIS comprovados já mapeados convergem para a mesma Pessoa.

## Consequências

- `ResolutionMethod` passa a admitir `NIS_DETERMINISTICO`.
- o contrato Pessoa v4 admite `tipo=NIS` com namespaces `NIS`, `PIS`, `PASEP`, `NIT`;
- `serving.v_bi_nis_qualidade` expõe apenas classificação/contagens, nunca o número;
- NIS não vira feature/LLR do Fellegi–Sunter por consequência desta ADR;
- a validação estatística #31 continua independente;
- conflito CPF × NIS é problema de consistência do identificador secundário, não motivo para rebaixar a âncora CPF.