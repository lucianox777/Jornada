# ADR-006 — NIS/CNIS e RG como identificadores secundários

- **Status:** Aceita para a candidata técnica v5.00
- **Data:** 2026-09-21
- **Escopo:** identidade progressiva / contrato Pessoa v4 / qualidade cadastral
- **Issue:** #394

## Contexto

A Jornada já utiliza `pessoa_uuid` como identidade canônica interna e CPF como âncora determinística externa principal. O contrato Pessoa v4 preserva identificadores tipados por observação.

NIS/PIS/PASEP/NIT possuem valor operacional importante, mas a própria governança CNIS admite múltiplas inscrições/elos para uma mesma pessoa. RG também é documento útil para identificação e investigação, porém não possui a estabilidade/unicidade necessária para funcionar como chave canônica municipal.

Referências de contexto para NIS/CNIS:
- INSS — Inscrição: https://www.gov.br/inss/pt-br/direitos-e-deveres/inscricao-e-contribuicao/inscricao
- Portaria DIRBEN/INSS nº 990, com alterações posteriores: https://portalin.inss.gov.br/portaria990
- CAIXA — Cadastro NIS: https://www.caixa.gov.br/servicos/nis/Paginas/default.aspx

## Decisão

### Hierarquia

1. `pessoa_uuid` é a identidade canônica interna da Jornada.
2. CPF é a âncora determinística externa principal e permanente.
3. `UUID_JORNADA` é retroalimentação interna governada, não documento externo.
4. NIS/PIS/PASEP/NIT e RG são **identificadores secundários**.
5. Linkage probabilístico continua sendo o mecanismo complementar quando a identidade não é resolvida pelas rotas admitidas.

### NIS/PIS/PASEP/NIT

O contrato usa um único `tipo_identificador_codigo=NIS`. `namespace_codigo=NIS|PIS|PASEP|NIT` preserva a procedência/nomenclatura recebida. O número é normalizado e recebe validação estrutural local `NIS_BR_11_V1`.

`statusEvidencia=DECLARADO|COMPROVADO` é preservado separadamente da validade estrutural. Um NIS estruturalmente válido e comprovado continua sendo identificador secundário: não recebe poder de constituir Pessoa.

### RG

RG permanece tipado por valor, emissor e UF. O catálogo passa a explicitar seu papel efetivo como `NAO_AUTOMATICA/NAO_HIERARQUICO`, igual ao papel arquitetural adotado para NIS.

### Proibições

NIS e RG:

- não criam `pessoa_uuid`;
- não criam nem atualizam `identity_map` automaticamente;
- não possuem `ResolutionMethod` próprio;
- não promovem `pessoa_origem_progressiva` para `REFERENCIA`;
- não vencem, suspendem ou substituem CPF;
- não produzem fusão automática;
- não entram automaticamente como ground truth, feature, blocking, LLR, prior ou threshold do Fellegi–Sunter.

### Cardinalidade e qualidade

Uma Pessoa pode possuir 0..N NIS/PIS/PASEP/NIT e 0..N RGs ao longo do tempo. A Jornada preserva as observações e a proveniência sem eleger artificialmente um número social ou RG como chave municipal.

Se o mesmo NIS aparecer em observações correntemente atribuídas a Pessoas distintas, a Jornada sinaliza `NIS_ASSOCIADO_MULTIPLAS_PESSOAS` no QC/BI. Essa condição é evidência de qualidade cadastral; não identifica qual fonte está correta e não autoriza merge.

`serving.v_bi_identificador_secundario_qualidade` e `serving.v_bi_qualidade_identidade_origem` expõem somente classificações e contagens, nunca NIS ou RG em claro.

## Consequências

- o contrato Pessoa v4 admite `tipo=NIS` com namespaces `NIS`, `PIS`, `PASEP`, `NIT`;
- o Processor valida estruturalmente NIS e persiste NIS/RG em `silver.pessoa_identificador_observacao`;
- NIS/RG permanecem fora do writer de identidade determinística;
- ausência de CPF não é suprida automaticamente por NIS ou RG;
- a validação estatística #31 permanece independente.