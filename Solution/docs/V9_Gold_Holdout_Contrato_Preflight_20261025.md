# V9 Gold — contrato de pré-verificação de holdout independente (etapa 1)

**25/10/2026 — implementação parcial, NÃO experimento físico nem validação independente.**

Esta etapa introduz `V9GoldIndependentHoldoutContract.Prepare` e testes unitários fail-closed. O método aceita o candidato congelado pela PR #1190, manifesto declarado da nova coorte e SHA calculado do CSV, e devolve plano imutável de seis corridas (CURRENT e KNEE, cada um nas três ordens fixas). Não executa SQL nem abre o TEST. Não define limiar operacional: 0,05 entra exclusivamente como valor já selecionado em TRAIN V6.

O pré-voo rejeita escopo TRAIN_SELECTION, TEST ou VALIDATION histórica, seed diferente da proposta ou explicitamente histórica, coorte fora de 30 mil pessoas clean, CSV inconsistente com manifesto ou igual ao SHA TRAIN, fit diferente do congelado, refit, certificação/ativação e conexão diferente da LocalDB descartável prevista. Um namespace de identidade deriva da seed e do fingerprint da coorte; identificadores locais P000... iguais entre seeds não constituem prova de reutilização.

**Limitação crítica:** manifesto é declaração e SHA é prova de integridade, não prova da origem estatística. Ainda falta criar gerador/manifesto verificável de coorte nova, vincular hash real dos bytes CSV à seed e à versão do gerador, adaptar o Runner físico para escopo `INDEPENDENT_HOLDOUT` sem reaproveitar `TRAIN_SELECTION`, implementar seis execuções isoladas, métricas e auditoria por grupos independentes. Não executar `--v9-gold-harness` com CSV novo: esse comando corretamente exige SHA TRAIN original. Não forjar envelope.

A issue #1191 acompanha a implementação integral, inclusive perícia de falsas criações/falsos pares, evidência física e gates. Esta etapa não altera migration DT-10 canônica, ref, Gold operacional, HML/PROD, TEST ou DT-15.


## Etapa 2 — pré-voo verificável do gerador (sem SQL)

A CLI `--v9-gold-holdout-preflight <candidato-V6.json> <pasta-coorte-nova> <plano-novo.json>` agora verifica os **bytes reais** dos três arquivos produzidos pelo gerador C# (`observacoes.csv`, `pessoas_verdade.csv`, `gabarito.json`) contra `generation-manifest.json`. Exige esquema v1, `DEMOGRAPHIC_PRIMARY_V1`, perfil `clean`, seed nova congelada, versões de gerador/ruleset/RNG, fingerprint da entrada, hashes individuais e reconciliação do gabarito com manifesto. Também verifica layouts CSV, unicidade dos IDs, referências de observações às pessoas e contagens reais (30 mil pessoas e quantidade de observações do gabarito). Rejeita overlays de erro e nomes de saídas extras/duplicadas.

A conexão fornecida por `ConnectionStrings__Jornada` é **validada como LocalDB descartável** antes de ler arquivos. O plano é escrito com `FileMode.CreateNew` **somente depois** de passar todos os gates. Não é necessário conectar-se ao SQL para esse pré-voo. Exemplo de geração prévia: `dotnet run --project src/Jornada.Linkage.SyntheticCorpus -- generate --reference-root <ibge> --population-profile demographic-primary --birth-daily-source <birth.json> --error-profile clean --people 30000 --seed 20261025001 --out <coorte-nova>`. Os parâmetros de referência devem ser explicitamente versionados/congelados no experimento.

**Limite de evidência:** o manifesto e seus hashes comprovam integridade e consistência dos artefatos **declarados**, mas não autenticam por si sós a execução do gerador nem certificam independência estatística entre coortes. A execução física em seis LocalDBs independentes, métricas e verificação de provenance do fit ainda estão pendentes. Nunca promover esse pré-voo a certificação.
