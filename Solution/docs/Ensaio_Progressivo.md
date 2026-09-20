# Ensaio Progressivo da Jornada

O `Jornada.Ensaio` é um orquestrador de rehearsal operacional. O provider padrão é SQL Server e a configuração local usa LocalDB; a ingestão atravessa o endpoint HTTP configurado em `Ensaio:Endpoints:IngestaoEntregas`, por padrão `localhost`. O calibrador não é reimplementado: o ensaio executa o `Jornada.Linkage.Parameters.Worker`, caminho operacional C#/SQL Server da Solution.

## Sequência

O roteiro registra ambiente inicial; ingestão de Auxílio Aluguel; tentativa de calibração com uma única fonte; ingestão de Auxílio Emergencial do mesmo Gestor; nova tentativa de calibração; ingestão de Serviços de Abordagem de Gestor distinto; e calibração final condicionada à suficiência estatística de pares m inter-Gestor.

Quando a calibração final produz RASCUNHO, o ensaio executa `VALIDATE` explicitamente sobre essa versão e, sem ativá-la, chama `Jornada.Linkage.Runner` em `MODEL_VALIDATION --publish false`. Portanto o rehearsal não termina na existência do modelo: ele prova também que o Runner consegue carregar a mesma versão e o ruleset congelado sem publicar vínculos correntes.

A ausência de pares m nas etapas de uma única fonte ou de um único Gestor é resultado esperado e deve produzir comportamento fail-closed. Depois da entrada de Gestor distinto, a mera existência de pares não basta: `MinimumIndependentMatchedPairs` continua sendo o critério de suficiência. Se a calibração final não alcançar suficiência, as etapas de validação e Runner falham de forma explícita por ausência de RASCUNHO/VALIDADO; o ensaio não reduz thresholds para fabricar sucesso.

## Evidências

Cada etapa produz checkpoint JSON e Markdown. Entre checkpoints são produzidos diffs quantitativos. Blocos de observabilidade são independentes: se uma tabela/visão ainda não estiver disponível, o erro fica registrado no bloco e não é convertido em zero.

O checkpoint registra explicitamente a cadeia de proveniência `modelo_id → ruleset_id → fingerprint → passes`. Os `linkage_run` também registram `modelo_id` e `modelo_versao`, permitindo confrontar `MODEL_VALIDATION` com o artefato calibrado. Um modelo não é considerado evidência suficiente de calibração operacional quando o caminho exige blocking dinâmico e o ruleset correspondente não foi persistido.

## Relatório interpretativo

Ao final da execução é gerado `RELATORIO_ENSAIO.md`. O relatório consolida linha do tempo, deltas, falhas operacionais, volumes Silver/Gold, completude dos campos de linkage, pares m, sinais de dependência entre fontes, estado do modelo, proveniência do ruleset, execução de linkage e disponibilidade de PPV/sensibilidade.

A interpretação é deliberadamente conservadora: completude de dados não significa qualidade de ligação; RASCUNHO não significa VALIDADO ou ATIVO; existência de pares m não substitui suficiência estatística; ausência de ruleset em modelo que exige blocking dinâmico é quebra de proveniência; `MODEL_VALIDATION` não publica identidade operacional; e ausência de um bloco é lacuna de observabilidade, não evidência de valor zero. Acurácia de ligação só deve ser afirmada quando houver amostra de referência suficiente e estimativas PPV/sensibilidade com intervalos de confiança.

## Testes

Não é criada uma segunda suíte end-to-end exclusiva do executável. A lógica dos componentes continua coberta pelas suítes dos respectivos artefatos. O padrão geral de integração permanece SQL Server/LocalDB; não há provider ou suíte PostgreSQL no produto corrente. Para o ensaio, testes pequenos de plano/configuração e smoke de composição são suficientes enquanto não houver integração real de dados.
