# Solução de Apoio às Secretarias

Solução independente `SolucaoApoioSecretarias.sln`: transmissor C# migrado sem alterações de API, testes do transmissor, preparador de CSV e contratos SEHAB v1–v5 preservados byte a byte.

**Escopo do exemplo:** `preparador/fixtures/SEHAB` e `preparador/mapeamentos/sehab.synthetic.example.json` são estritamente sintéticos, não uma transformação homologada de arquivos reais. Não armazenar massa real ou credenciais no repositório.

## Preparar uma entrega sintética

```bash
python -m pip install -r preparador/requirements.txt
python preparador/preparador.py --csv preparador/fixtures/SEHAB/pessoas.csv --mapeamento preparador/mapeamentos/sehab.synthetic.example.json --manifest preparador/fixtures/SEHAB/manifest.json --schema config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json --saida .local/pacotes
python -m unittest discover -s preparador/tests
```

O preparador exige mapeamento explícito e motivo de ausência de CPF declarado pela origem, valida o schema integral e seu hash, e produz ZIP determinístico de três arquivos com SHA-256 no nome. Para uma fonte real, aprovar um mapeamento próprio, identificar sua proveniência e validar a versão contratual. O arquivo opcional `--registros` recebe registros JSONL já normalizados; a conversão de registros de cada sistema de origem depende de mapeamento homologado.

## Compilar e transmitir

```bash
dotnet restore SolucaoApoioSecretarias.sln --locked-mode
dotnet build SolucaoApoioSecretarias.sln -c Release --no-restore
dotnet run --project tests/Jornada.Integrador.Tests -c Release --no-build --no-restore
dotnet run --project clients/Jornada.Integrador.CSharp -- --enviar .local/pacotes/ENTREGA_SEHAB_SEHAB_v2_<sha256>.zip --config config/gestores/SEHAB/integrador.config.json
dotnet run --project clients/Jornada.Integrador.CSharp -- --resultado ENTREGA_SEHAB_SEHAB_v2_<sha256>.zip --config config/gestores/SEHAB/integrador.config.json
```

Copie `config/gestores/SEHAB/integrador.config.example.json` para arquivo não versionado e insira a credencial do ambiente. Nunca commitar a configuração efetiva. O arquivo de exemplo usa endereços ilustrativos.

## Fonte contratual e fronteira de execução

Os sete contratos foram transferidos com bytes idênticos e hashes originais em `config/governance/schema-approvals.SEHAB.json`, ainda PENDENTES de aprovação institucional. A Jornada receptora precisa carregar ou disponibilizar contratos compatíveis para aceitar os ZIPs: retirá-los do código-fonte principal não equivale a desregistrar um contrato ativo do banco.

Este diretório constitui uma solução independente no mesmo repositório; a criação de **outro repositório Git** permanece pendente. Preparo sintético e testes de código NÃO demonstram recebimento e processamento em SQL/HTTP. A comprovação operacional e regressões obrigatórias estão no item 6 do plano principal.
