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

## Compatibilidade com o receptor DEV e gates

O exemplo principal usa o schema Pessoa **v5** apenas para validar o preparo local. A migração principal da Jornada registra v5 de SMADS, SMDET e SMS como RASCUNHO; o registro v5 da SEHAB é entregue separadamente em `database/migrations/Registrar_SEHAB_Pessoa_v5.sql`, sem ativação automática. **Não transmitir o ZIP v5 a um receptor que ainda usa v4 ativo**: para a prova HTTP/SQL isolada use `preparador/mapeamentos/sehab.synthetic.v4.example.json`, `preparador/fixtures/SEHAB/manifest.v4.json` e `config/contracts/gestores/SEHAB/pessoa/v4/pessoa.schema.json`.

O workflow `apoio-secretarias` executa também o gate estático `scripts/check-segregation.py` e seus testes negativos: impede que o integrador, os contratos SEHAB e o registro v5 voltem à solução principal. A exceção é o link **somente de leitura** do schema externo na fixture de testes de integração SQL; exemplos sintéticos DEV e documentos históricos não são artefatos de produção. O workflow verifica o SHA dos oito arquivos específicos, executa os quatro testes do preparador, compila/transmite a suíte C# e verifica o inventário e schemas v1–v5 dos **21** arquivos de SMADS, SMDET e SMS que permanecem no produto principal. A prova operacional contra o receptor SQL e a autorização de HML são gates separados em `Solution/scripts/local-e2e.sh` e no Plano, não inferidos do workflow de apoio.

## Compilar e transmitir

```bash
dotnet restore SolucaoApoioSecretarias.sln --locked-mode
dotnet build SolucaoApoioSecretarias.sln -c Release --no-restore
dotnet run --project tests/Jornada.Integrador.Tests -c Release --no-build --no-restore
dotnet run --project clients/Jornada.Integrador.CSharp -- --enviar .local/pacotes/ENTREGA_SEHAB_SEHAB_v2_<sha256>.zip --config config/gestores/SEHAB/integrador.config.json
dotnet run --project clients/Jornada.Integrador.CSharp -- --resultado ENTREGA_SEHAB_SEHAB_v2_<sha256>.zip --config config/gestores/SEHAB/integrador.config.json
```

Copie `config/gestores/SEHAB/integrador.config.example.json` para arquivo não versionado e insira a credencial do ambiente. Nunca commitar a configuração efetiva. O arquivo de exemplo usa endereços ilustrativos.

A regressão estática pode ser executada localmente com `python scripts/check-segregation.py` e `python -m unittest discover -s scripts/tests -v`, a partir de `ApoioSecretarias/`. O guard verifica inventários, composição das duas soluções, referências dos projetos C# de produção, caminhos físicos proibidos e ausência de registro SEHAB na migração principal Pessoa v5. Não exige banco, rede, credenciais ou dados reais.

## Fonte contratual e fronteira de execução

Os sete contratos Pessoa e o metadado AA01 foram transferidos com bytes idênticos e hashes originais em `config/governance/schema-approvals.SEHAB.json`, ainda PENDENTES de aprovação institucional. A Jornada receptora precisa carregar ou disponibilizar contratos compatíveis para aceitar os ZIPs: retirá-los do código-fonte principal não equivale a desregistrar um contrato ativo do banco.

Este diretório constitui uma solução independente no mesmo repositório; a criação de **outro repositório Git** permanece pendente. Preparo sintético e testes de código NÃO demonstram recebimento e processamento em SQL/HTTP. A comprovação operacional e regressões obrigatórias estão no item 6 do plano principal.
