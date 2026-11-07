# V9 — persistência durável do corpus sintético canônico em `ref`

Implementação isolada, **ainda não integrada à master** e independente da campanha física congelada no SHA `b3a93d90c4914fe33b2197f3ae40462601817e83`.

## Contrato

O cadastro existente `ref.corpus_sintetico_versao_v9` contém proveniência e digests, **não contém os registros**. A migração adicional `20261103_Ref_Corpus_Sintetico_Arquivos_V9.sql` introduz `ref.corpus_sintetico_arquivo_v9` e as procedures de escrita idempotente e comprovação de completude.

A camada `ref` agora pode arquivar, **byte a byte e sem reexecutar o gerador**, os quatro arquivos do corpus de 30.000 pessoas:

- `pessoas_verdade.csv`;
- `observacoes.csv`;
- `gabarito.json`;
- `generation-manifest.json`.

Cada arquivo possui SHA-256 vinculado ao cadastro de origem previamente aprovado. A integridade é conferida tanto pelo cliente quanto pelo SQL (`HASHBYTES('SHA2_256', ...)`). Escritas devem ocorrer em uma única transação; alteração e exclusão são proibidas por trigger. Reenvio de bytes idênticos é idempotente. Recuperação de um acervo parcial é recusada, e o export só fica visível quando os quatro arquivos são verificados. Nenhum dado vai para `ref.frequencia_nome`, tabelas de Gold, TRAIN operacional ou modelo ativado.

## Instalação (somente banco `ref` persistente e aprovado)

1. Identificar um banco SQL Server **durável** para o acervo de desenvolvimento, com backup, permissões mínimas, criptografia e retenção definida. Não utilizar HML/PROD sem autorização específica; não confundir `JornadaHarness` descartável da GitHub Action com armazenamento durável.
2. Confirmar que o cadastro de metadados de V9 já existe nesse banco e que a referência IBGE publicada confere com o manifesto e com o preflight vigente. Instalar primeiro a migração `20261103_Ref_Corpus_Sintetico_Evidencia_V9.sql` somente após essa conferência; instalar então a nova migração `20261103_Ref_Corpus_Sintetico_Arquivos_V9.sql`.
3. Com os quatro arquivos provenientes da geração validada pelo gate `Solution/scripts/v9-tf-ref-provenance-gate.py`, apontar `ConnectionStrings__Jornada` para esse banco e executar, a partir de `Solution`:

```powershell
dotnet run -c Release --project src/Jornada.Linkage.Parameters.Worker -- --v9-ref-corpus-store import .local/v9-tf-physical/people-30k
```

4. Em uma sessão futura, para **reutilizar os bytes originais sem geração**, exportar para uma pasta nova, que ainda não exista:

```powershell
dotnet run -c Release --project src/Jornada.Linkage.Parameters.Worker -- --v9-ref-corpus-store export .local/v9-ref-recovered-30k
```

5. Para exigir o conjunto completo antes de usar os arquivos em qualquer ensaio:

```sql
EXEC ref.sp_exigir_corpus_sintetico_v9_completo @codigo=N'V9_SEED42_30000_CSHARP_V1';
```

**Segurança:** os CSVs são sintéticos, mas contêm dados nominais plausíveis. Não os imprimir, anexar a issues, incluir em artifacts públicos ou enviar ao Git. Restringir leitura do campo `conteudo` aos operadores autorizados, auditar a recuperação e garantir backups e restauração. Usuários comuns não devem receber UPDATE/DELETE sobre o acervo. O comando exige configuração explícita do banco; não modifica automaticamente bancos operacionais.

## Limites e sequência científica

A campanha `v9-physical-seed42-probe.yml` **não foi alterada**. A execução oficial no SHA congelado continua criando um SQL descartável e reconstruindo o corpus para conferir o pin IBGE e o bootstrap; importar em uma tabela nesse contêiner não preservaria os bytes após o job. Esta entrega permite arquivamento persistente em um banco SQL com duração e backup próprios, mas **não afirma que os 30.000 já estejam gravados em tal banco**. Isso só será evidência quando uma importação real devolver sucesso, com local, SHA, contagem 4/4 e backup confirmado.

Reutilização do corpus em pipelines futuros exige desenho e CI específicos, sem pular a verificação do SHA, do bootstrap, da referência IBGE e da separação TRAIN/VALIDATION/TEST. O corpus completo não é licença para usar TEST na seleção, nem autorização DT-15 para ativar modelos. O fluxo operacional permanece separado.

Para rodar manualmente os testes negativos SQL em um **banco descartável**, depois das migrações, utilizar `Solution/tests/sql/V9_Ref_Corpus_Sintetico_Arquivos_Negativos.sql`.
