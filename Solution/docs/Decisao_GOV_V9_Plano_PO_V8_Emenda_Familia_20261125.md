# Jornada V9 — emenda familiar ao plano PO v8 (25/11/2026)

**ID:** GOV-V9-PLANO-PO-V8-EMENDA-FAMILIA-20261125. **Autoridade:** Product Owner, resposta expressa “Concordo com vc, siga seu plano” à avaliação de 25/11/2026 da proposta de 20h30. **Estado:** DECIDIDO; implementação, validação e integração pendentes. GOV-DOC-01. Prevalece sobre os trechos incompatíveis do [v8](Decisao_GOV_V9_Plano_PO_V8_20261125.md), [contrato 10k](Plano_V9_Fase4_10k_20261125.md) e pré-registro anterior, sem revogar demais decisões. Não abre nova frente antes de A.

## Motivo e escopo
O estresse superamostra casos difíceis, de modo que a probabilidade de zero fusões diminui com a exposição mesmo sob taxa condicional constante. Massas sem estrutura familiar subexpõem esses casos. Separar qualidade sob hipóteses demográficas e descoberta de falhas raras; não afirmar representatividade populacional comprovada. Política oficial S4b(i), b1/r1, modelo e vencedor campanha 38, hashes, M/U/TF, limiares, gates e comparabilidade científica congelados.

## Desenho e recomendação
- Três matrizes completas inalteradas (72 células/144 trajetórias/216 estados por perfil): comparável 10k seed355 clean, desafio 10k seed7311026 field, família 10k demograficamente referenciada com hipóteses explícitas de sensibilidade. Comparável/desafio preservam os hashes e são declarados sem estrutura familiar deliberadamente modelada no relatório C (não afirmar ausência de colisões acidentais).
- Estresse complementar ao terceiro perfil, massas/namespaces independentes sem contaminar universo de candidatos nem resultados do núcleo 10k, sem pooling. Matriz diagnóstica reduzida: i oficial, ii líder seguro, ii todos membros seguros, cada uma em rodada única/dupla; b1, r1, prior por registros e busca ampla fixos. Seis trajetórias/nove estados por massa complementar; não reduzir nenhuma das três matrizes completas. Dimensionar por preflight real, runner padrão GitHub, limite global 6h, watchdog e prazo v8, sem ampliar timeout ou contratar infraestrutura. Nenhum 100k.
- Zero fusões observadas na família demograficamente referenciada é condição necessária, não suficiente, para recomendação favorável. Em comparável/desafio, investigar toda fusão como possível regressão/falha funcional. Em estresse, investigar toda fusão, mecanismo, exposição, gravidade, repetibilidade e mitigação; falha sistemática não é aceitável automaticamente por raridade. Violação de gate, proteção ou segurança bloqueia independentemente do perfil. Resultado experimental negativo explicado não impede concluir relatório C; decisão de política e trade-offs continua exclusiva do PO.
- Ponderação somente com unidade/denominador compatível e estratos mutuamente exclusivos ou distribuição conjunta explícita: R=sum(w_h*r_h). Taxas de estresse não são diretamente transportáveis sem demonstrar comparabilidade condicional da exposição, seleção, erros e universo de candidatos. Não somar sobreposições; publicar por pessoa, família, par, fonte/pares aceitos e identidade contaminada separadamente. CP unilateral zero eventos 1-0,05^(1/n) só para n unidades independentes e efetivamente expostas; 120 ~2,47%, 500 ~0,60%, 1000 ~0,30%. Não tratar versões, pares dependentes, células ou famílias conectadas por colisão como IID. Aplicar CP+Hoeffding/Bonferroni somente sob hipóteses e caps válidos, declarar não suportado quando necessário. Sem alegação de certificação de 0,1446% ou comparação formal #38.

## Referências e hipóteses para futuro pré-registro
Fonte agregada SMADS, Diagnóstico Socioterritorial 2025, jan/2025: 1.645.430 famílias, 3.683.221 pessoas, 531.577 unipessoais (~32,3% famílias; ~14,4% pessoas), média 2,24 pessoas/família; menores 18 ~36,3%. [Publicação](https://autodromodeinterlagos.prefeitura.sp.gov.br/documents/d/assistencia_social/diagnostico_final_compressed-pdf). Texto indexado consultado na avaliação anterior, páginas do PDF não conferidas visualmente; confirmar versão, tabela e denominadores antes do manifesto final. [Otta et al.](https://pubmed.ncbi.nlm.nih.gov/27776569/): 11,96 partos gemelares/mil maternidades em SP 2003–2014, ~2,4% dos nascidos vivos pertencem a pares; não mede inclusão conjunta no CadÚnico. [Seade via Jornal USP](https://jornal.usp.br/atualidades/fundacao-seade-aponta-queda-historica-de-fecundidade-em-sao-paulo-neste-seculo/): fecundidade 2,1 em 2000 e 1,4 em 2024; não deriva presença conjunta de irmãos. IBGE fornece marginais de nomes, não distribuição conjunta de colisões familiares. Nenhum microdado/dado real.

| Característica | Central 10k | Sensibilidade | Evidência |
|---|---:|---:|---|
| Pessoas em famílias unipessoais | 14,4% | 12–18% | SMADS, família cadastral distinta da unidade materna |
| Menores de 18 | 36,3% | 32–40% | SMADS |
| Gêmeos conjuntamente presentes | 120 pares | 60–140 | Otta, inclusão conjunta hipotética |
| Pessoas com irmãos não gêmeos presentes | 20% | 10–30% | sem fonte direta |
| Mãe/filha nome completo igual | 20 pares | 5–50 | sem fonte |
| Pessoas com homônimo completo não aparentado | 2% | 0,5–5% | sem fonte direta |
| Homônimos não aparentados nome/data iguais | 5 pares | 0–20 | sem fonte |
| Mães distintas com nome completo igual | 20 pares | 5–100 | sem fonte |
| Pessoas em famílias de 5+ | 10% | 5–15% | sem fonte municipal direta |

Valores são referências centrais e faixas de sensibilidade aprovadas como desenho de trabalho, não prevalências comprovadas, intervalos de confiança, quotas finais nem ordem de geração. Sobreposições e denominadores deverão ser especificados; nenhuma seleção pós-resultado. Converter família cadastral em unidade materna apenas com hipóteses verificáveis. Se a fonte não sustenta representatividade, manter denominação demograficamente referenciada e declarar limitação.

## Cap e orçamento de estresse
Cap aprovado antes de geração: B=8 pessoas-base por família materna incluída, C=8 versões/pessoa, C_F=64 versões/família; singleton conta como família. Não dividir famílias nem aumentar cap após geração. Pré-especificar tratamento de famílias >8, quantificar exclusão/truncamento e restrição do universo; rejeitar violação. Cap não é prevalência. Contrato individual C inalterado.

| Estrato (unidade diagnóstica) | Inicial | Ampliado |
|---|---:|---:|
| Gêmeos prenomes distintos (pares) | 250 | 500 |
| Gêmeos prenomes semelhantes (pares) | 250 | 500 |
| Irmãos não gêmeos (famílias) | 300 | 600 |
| Mãe/filha homônimas (pares) | 150 | 300 |
| Homônimos não aparentados (pares) | 300 | 600 |
| Homônimos mesma data (pares) | 150 | 300 |
| Mães distintas nome igual (pares) | 150 | 300 |
| Famílias 5–8 (famílias) | 50 | 100 |
| Total unidades diagnósticas | 1600 | 3200 |

Unidades não equivalem a pessoas ou versões; censo de exposição sem CPF, gestores, blocking, elegibilidade S4b e unidades dependentes antes da execução. Definir tamanho dos grupos, interseções, semente e ordem de chegada, corrupção/ausência, quotas finais e critério de ampliação pelo orçamento antes de qualquer resultado. Ampliar somente por capacidade previamente definida, não por erro favorável/desfavorável; sem promessa de caber no runner. Publicar cada estrato separado, limite de 1000 gêmeos combinados não vale para cada subtipo. Preservar 10k do núcleo, orçamento adicional explícito, não inflar o núcleo.

## Execução, dependências e rastreabilidade
A continua prioridade exclusiva: #1940 paridade 1k, negativos V8/V9/reversão, curva e FULL comparável 10k, integração governada. #1941 unificação técnica comprovada; #1942 preparação familiar isolada, não avançar nem integrar até A concluída. Em B reconciliar branches, adaptar código e manifesto à emenda (fixtures anteriores 4/8/32 são históricas, não cap vigente), preservar hashes comparável/desafio, construir correlações, pesos quando sustentados, observações, gabarito selado fora scorer/ZIP, censo e preflight, depois executar apenas o escopo DEV autorizado. Pré-registro final indispensável, geração bloqueada até A e todos gates. Em C incluir resultados separados, riscos, dependência familiar potencial para U/TF, limitações e eventual necessidade de ciência futura; nenhuma recalibração. Estimativa incremental de engenharia 4–6 dias, não SLA, exclui A e tempo físico ainda não medido. Parada v8 se B incompleta com menos de cinco dias até expiração 22/12/2026 06:09:48 UTC; verificar vencedor e custódia sem publicar bytes. D somente texto. Sem TRAIN, TEST, nova campanha, 100k, HML/PROD, INCREMENTAL ou ativação.

Rastreabilidade: #1851/#1931/#1940/#1941/#1942; avaliação PO 25/11/2026; [pré-registro](V9_Family_Generator_PreGeneration_20261125.json). DECIDIDO não implica implementado, validado ou integrado.
