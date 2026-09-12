# Documentos da Jornada do Cidadão

Este índice existe para evitar que snapshots históricos preservados no repositório sejam confundidos com os artefatos correntes. Ele **não cria uma nova release** e não substitui os documentos normativos, o DDL, os contratos ou `RELEASE_INFO.txt`.

## Pontos de entrada correntes

- **Release de engenharia efetivamente selada:** `RELEASE_INFO.txt` — Base Normativa v3.64, Solution Engenharia v4.05, SolutionSchema v3.69, tag `jornada-solution-v4.05`.
- **Estado técnico candidato desta consolidação:** SolutionSchema v3.70. A release/tag v5.00 ainda não foi cortada.
- **Resumo executivo não versionado:** `Documentos/Resumo_Executivo.md`. Deve refletir a fronteira entre a última release selada e o estado técnico candidato.
- **Especificação Técnica:** `Documentos/Especificacao_Tecnica_Jornada_v3.62.docx` e `.pdf`.
- **Requisitos consolidados:** `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1` é a porta de entrada institucional. Os documentos v1.0 permanecem históricos e não devem ser lidos cumulativamente com v1.1.
- **Modelo físico corrente:** `Documentos/Anexo_Modelo_Fisico_Jornada_v1.40` em MD, DOCX e PDF, derivado de `Solution/database/Jornada_Fase1_v3.70.sql` e com inventário automatizado de 66 tabelas.

## Artefatos históricos preservados

`Documentos/Anexo_Modelo_Fisico_DER_Jornada_v1.39.docx` e `.pdf` representam o estado anterior associado à contagem histórica de 53 tabelas. Para contagem e estrutura do schema técnico candidato 3.70, deve ser usado o modelo físico v1.40; a presença dos arquivos v1.39 no diretório não os torna concorrentes com o documento corrente.

`Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.docx` e `Documentos/Anexo_Pendencias_Desenvolvimento_Jornada_v1.47.pdf` são um **snapshot histórico de pendências**, preservado apenas para rastreabilidade. Eles não constituem o backlog corrente nem devem ser usados para inferir que os itens ali listados continuam abertos; o estado corrente deve ser obtido dos artefatos vigentes identificados neste índice e do backlog aberto do projeto.

Os arquivos `Estado_Engenharia_v*.md` são snapshots versionados do estado de engenharia em momentos específicos. O mais recente deles não substitui automaticamente `RELEASE_INFO.txt` nem o estado técnico da branch corrente. Da mesma forma, os arquivos `Evidencia_Runtime_*` registram execuções específicas e não constituem, isoladamente, declaração da release vigente.

Na pasta `Documentos/Requisitos/`, as versões v1.0 são baselines históricos. A regra de leitura vigente está documentada no índice mestre v1.1, e os antigos aditivos usados na consolidação permanecem em `Documentos/Requisitos/Historico/` apenas para auditoria.

## Regra de precedência

Em caso de dúvida sobre versão ou vigência:

1. use `RELEASE_INFO.txt` para identificar a última release/tag efetivamente selada;
2. use o índice mestre de requisitos v1.1 para a leitura institucional consolidada candidata;
3. use `Anexo_Modelo_Fisico_Jornada_v1.40` e `Solution/database/Jornada_Fase1_v3.70.sql` para o estado físico candidato 3.70;
4. trate documentos explicitamente versionados anteriores e evidências runtime como histórico, salvo indicação expressa em documento corrente.

Nenhum item deste índice implica aprovação institucional, implantação em HML/Produção ou conclusão de gates que dependam de dados reais, governança ou decisão externa.
