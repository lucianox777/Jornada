# Documentos da Jornada do Cidadão

Este índice existe para evitar que snapshots históricos preservados no repositório sejam confundidos com os artefatos correntes. Ele **não cria uma nova release** e não substitui os documentos normativos, o DDL, os contratos ou `RELEASE_INFO.txt`.

## Pontos de entrada correntes

- **Release de engenharia efetivamente selada:** `RELEASE_INFO.txt` — Base Normativa v3.64, Solution Engenharia v4.05, SolutionSchema v3.69, tag `jornada-solution-v4.05`.
- **Estado técnico candidato desta consolidação:** SolutionSchema v3.70. A release/tag v5.00 ainda não foi cortada.
- **Resumo executivo não versionado:** `Documentos/Resumo_Executivo.md`. Deve refletir a fronteira entre a última release selada e o estado técnico candidato.
- **Especificação Técnica publicada:** `Documentos/Especificacao_Tecnica_Jornada_v3.62.docx` e `.pdf`. Não existe `Especificacao_Tecnica_Jornada_v3.64.*` materializada nesta árvore.
- **Especificação Técnica candidata:** `Documentos/Especificacao_Tecnica_Jornada_Candidata.md`, sem número normativo e sem efeito de publicação/release até aprovação e corte formais. Consolida a v3.62 com o estado técnico comprovável do HEAD e mantém pendências institucionais como gates externos.
- **Requisitos consolidados:** `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1` é a porta de entrada institucional. Os documentos v1.0 permanecem históricos e não devem ser lidos cumulativamente com v1.1.
- **Modelo físico corrente:** `Documentos/Anexo_Modelo_Fisico_Jornada_v1.40` em MD, DOCX e PDF, derivado de `Solution/database/Jornada_Fase1_v3.70.sql`. A fonte Markdown está sincronizada com o inventário automatizado atual de **69 tabelas**; DOCX/PDF permanecem artefatos derivados e devem ser regenerados antes de nova publicação de entrega.

## Lacuna normativa v3.64 × Especificação v3.62

`RELEASE_INFO.txt` e os estados de engenharia v4.04/v4.05 registram **Base Normativa v3.64** para a release selada. Esse fato é parte da proveniência da release e não deve ser reescrito retroativamente. Ao mesmo tempo, a última Especificação Técnica materializada no repositório é a **v3.62**.

Essas duas afirmações têm papéis diferentes e devem permanecer explícitas:

1. `RELEASE_INFO.txt` identifica a base normativa **declarada pela release** e sua linhagem;
2. `Especificacao_Tecnica_Jornada_v3.62.docx/.pdf` é o último texto de Especificação Técnica **publicado e verificável** nesta árvore;
3. `Estado_Engenharia_v4.04.md`, `Estado_Engenharia_v4.05.md`, notas e artefatos de engenharia registram mudanças e contexto técnico, mas **não constituem por si só uma Especificação Técnica v3.64**;
4. enquanto uma v3.64 formal não for publicada, nenhuma documentação corrente pode apontar para um arquivo v3.64 inexistente nem reconstruir seu conteúdo por inferência.

Essa regra resolve a ambiguidade de leitura sem fabricar documento normativo e sem alterar o `RELEASE_INFO.txt` selado. A candidata de consolidação não é uma reconstrução da v3.64: ela é um novo artefato de revisão, explicitamente derivado da v3.62 e do estado técnico verificável, que só receberá versão normativa no ato de publicação formal.

## Artefatos históricos preservados

`Documentos/Anexo_Modelo_Fisico_DER_Jornada_v1.39.docx` e `.pdf` representam o estado anterior associado à contagem histórica de 53 tabelas. Para contagem e estrutura do schema técnico candidato 3.70, deve ser usada a fonte corrente do modelo físico v1.40; a presença dos arquivos v1.39 no diretório não os torna concorrentes com o documento corrente.

`Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.docx` e `Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.pdf` são um **snapshot histórico de pendências**, preservado apenas para rastreabilidade. Eles não constituem o backlog corrente nem devem ser usados para inferir que os itens ali listados continuam abertos; o estado corrente deve ser obtido dos artefatos vigentes identificados neste índice e do backlog aberto do projeto.

Os arquivos `Estado_Engenharia_v*.md` são snapshots versionados do estado de engenharia em momentos específicos. O mais recente deles não substitui automaticamente `RELEASE_INFO.txt` nem o estado técnico da branch corrente. Da mesma forma, os arquivos `Evidencia_Runtime_*` registram execuções específicas e não constituem, isoladamente, declaração da release vigente.

Na pasta `Documentos/Requisitos/`, as versões v1.0 são baselines históricos. A regra de leitura vigente está documentada no índice mestre v1.1, e os antigos aditivos usados na consolidação permanecem em `Documentos/Requisitos/Historico/` apenas para auditoria.

## Regra de precedência

Em caso de dúvida sobre versão ou vigência:

1. use `RELEASE_INFO.txt` para identificar a última release/tag efetivamente selada e a Base Normativa que ela declara;
2. use `Especificacao_Tecnica_Jornada_v3.62.docx/.pdf` para o último texto de Especificação Técnica efetivamente publicado nesta árvore; não presuma a existência ou o conteúdo de uma v3.64 ausente;
3. use `Especificacao_Tecnica_Jornada_Candidata.md` apenas para revisão da próxima consolidação normativa; ela não substitui a v3.62 antes da publicação formal;
4. use o índice mestre de requisitos v1.1 para a leitura institucional consolidada candidata;
5. use `Anexo_Modelo_Fisico_Jornada_v1.40.md` e `Solution/database/Jornada_Fase1_v3.70.sql` para o estado físico candidato 3.70; para publicação de entrega, regenere os derivados DOCX/PDF a partir da fonte corrente;
6. use estados/notas de engenharia para rastrear deltas técnicos, sem promovê-los implicitamente a norma;
7. trate documentos explicitamente versionados anteriores e evidências runtime como histórico, salvo indicação expressa em documento corrente.

Nenhum item deste índice implica aprovação institucional, implantação em HML/Produção ou conclusão de gates que dependam de dados reais, governança ou decisão externa.
