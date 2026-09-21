# Guia de leitura por papel e glossário

## Guia por papel

### Desenvolvimento / manutenção

1. `../README.md`
2. `Arquitetura_Identidade_Linkage.md`
3. `Invariantes_Arquitetura.md`
4. `Mapa_Modulos_Produto_Andaime.md`
5. `Conformidade_Invariantes.md`
6. runbooks e testes do componente alterado.

### Arquitetura

1. Especificação Técnica candidata e requisitos.
2. `Arquitetura_Identidade_Linkage.md`
3. `UML_Arquitetura_Indice.md`
4. `Sequencias_Identidade_Linkage.md`
5. decisões contingentes e ADRs vigentes.

### DBA / PRODAM

1. Modelo físico/anexo vigente.
2. `database/Jornada_Fase1_v3.70.sql` + manifest de migrations.
3. `Runbook_Operacao.md` e `Runbook_Desenvolvimento_Local.md`.
4. invariantes que são protegidos por constraint/procedure/trigger.
5. #378/#407 para itens dependentes de HML/PRD.

### Dados / Linkage

1. `Arquitetura_Identidade_Linkage.md`.
2. `Calibrador_FS_Specification.md`.
3. `Linkage_Implementation_Conference.md`.
4. #31 para validação estatística com dado real.
5. `Conformidade_Invariantes.md`.

### Governança / jurídico / gestão

1. Especificação Técnica e requisitos.
2. `Governanca_Finalidade_Acesso.md`.
3. #379 para deliberações externas.
4. #378 para identidade corporativa/separação ambiental.
5. documentação de segurança e auditoria.

### Secretaria / BI / operação finalística

1. contratos de integração e OpenAPI pertinentes;
2. Telas/BI e QC;
3. glossário abaixo;
4. regras de projeção/acesso vigentes;
5. não usar documentos históricos de engenharia para inferir política atual.

## Glossário

**Âncora CPF** — associação permanente CPF→UUID, quando válida e governada. Não é score probabilístico.

**Andaime de engenharia** — ferramenta de ensaio, conferência, avaliação ou verificação que prova o produto sem receber autoridade operacional.

**Blocking** — redução do universo de candidatos. Gera candidatos; não decide vínculo.

**Bronze** — pacote recebido preservado fora do SQL em storage content-addressed, com metadados/controle no banco.

**Calibrador** — componente que estima/publica parâmetros/rulesets versionados; não recebe autorização automática de Produção pelo simples sucesso técnico.

**Canonical UUID / referência canônica** — referência corrente de identidade após decisão admitida.

**Conference** — implementação independente de scorer/policy para conferir a implementação. Não substitui validação estatística nem o Runner.

**Divergência** — condição governada que exige tratamento/visibilidade; encerrar a fila não é necessariamente remover a causa.

**Fato finalístico** — benefício/serviço/registro recebido da origem. Sua validade não depende de identidade já resolvida.

**Gold** — projeção materializada dos fatos para consumo analítico/serving, preservando a separação entre fato e identidade.

**Identificador secundário** — documento/identificador útil como evidência ou qualidade, mas sem autoridade determinística para constituir Pessoa. Atualmente NIS/PIS/PASEP/NIT e RG; CNH está planejada em #402.

**initial_uuid** — UUID de continuidade da identidade persistente de origem; não é evidência estatística.

**Ledger** — trilha append-only de atos/estados governados.

**Linkage** — resolução probabilística versionada de identidade quando rotas determinísticas não resolvem.

**Modelo ATIVO** — versão de modelo selecionada pelo rito governado; não significa homologação populacional se #31 estiver pendente.

**Pessoa** — identidade canônica interna da Jornada; não é sinônimo de uma linha recebida nem de CPF.

**Processor** — worker que transforma pacote aceito em Silver/identidade/Gold conforme contrato.

**POSSIVEL** — estado planejado para zona cinzenta sem vínculo automático (#408); não está autorizado a ser inferido antes da implementação/limiar governado.

**Serving** — superfícies de consulta/BI derivadas do estado operacional.

**Silver** — observações normalizadas/preservadas da origem; não apaga o valor original por causa da normalização de Linkage.

**u condicionado ao blocking** — distribuição de não-match estimada entre pares que sobrevivem ao ruleset efetivo de candidatos.

**Zona cinzenta** — faixa entre “não vincular automaticamente” e “evidência suficiente para levar ao balcão”; largura depende de dados reais (#31/#408).
