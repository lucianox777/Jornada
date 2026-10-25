# Solução independente de Apoio às Secretarias — protótipo funcional V0.1

> **Não é o instalador da Jornada**, não integra `Solution/Jornada.sln` e **não contém contratos homologados nem credenciais**. O perfil e o contrato anexados são **exclusivamente sintéticos**. **Não usar dados reais nem ativar envio de produção sem homologação do perfil, contrato, destino e ambiente institucional**.

## Funcionalidades implementadas

- Aplicação desktop Python/Tkinter (Windows) com escolha de **arquivo ou pasta**, perfil, pasta de saída e opção **enviar também**.
- CLI (inclusive modo Docker) reutiliza exatamente o mesmo núcleo.
- Perfil explícito JSON versionado define `gestor`, `sistemaOrigem`, `dataReferencia`, campos de cada origem, padrões de nomes de arquivos e contratos aprovados por SHA-256. Suporta CSV UTF-8 e JSONL.
- **FAIL-CLOSED**: pasta inteira é conferida antes de publicar qualquer ZIP ou abrir conexão; arquivo não mapeado, campo extra/faltante, schema/hash divergente, erro de linha, Pessoa ausente ou CPF sem motivo na origem bloqueiam o preparo. Os originais nunca são alterados.
- Se tudo estiver válido, **sempre** gera arquivo determinístico 
  `ENTREGA_<GESTOR>_<SISTEMA_ORIGEM>_v2_<sha256>.zip`, com **exatamente** `manifest.json`, `pessoas.jsonl`, `registros.jsonl` na raiz.
- `enviar=false` por padrão: somente prepara e salva. `enviar=true`: transmite o **mesmo ZIP** à API HTTPS configurada com `X-Jornada-Gestor`, `X-Jornada-Access-Key` e `Idempotency-Key: sha256:<hash>`. Não registra access key e bloqueia redirecionamento HTTP.
- Relatório JSON + HTML em `saida/relatorios/` em toda execução, inclusive quando invalidada antes da geração. Falha HTTP não remove ZIP. `RECEBIDO` é confirmação HTTP e **não** `PROCESSADO`.

## Teste local (Python 3.13 recomendado)

```bash
python -m pip install -r requirements.txt
python -m unittest discover -s testes -v
python -m apoio --entrada exemplos/entrada --perfil exemplos/perfil.sintetico.json --saida ./saida --nao-enviar
python executar_interface.py
```

**AVISO:** o exemplo usa pessoas fictícias, não um contrato operacional. A validação do exemplo prova apenas o comportamento do preparador. O repositório separado precisa importar hashes/contratos **homologados** para cada Secretaria, sem cópias ad hoc.

A pasta de saída deve ficar **fora** da pasta selecionada; caso contrário, é rejeitada para impedir ingestão dos próprios resultados. Para arquivo único, informe o caminho completo do arquivo. A pasta pode conter múltiplos arquivos, mas cada um precisa corresponder exatamente a uma regra do perfil. Uma seleção produz **uma remessa**; se houver várias naturezas/tipos, execute perfis/remessas separados.

## Operação programada

Sem envio (padrão seguro):

```bash
python -m apoio --entrada /origem --perfil /perfil/perfil.json --saida /saida
```

Com envio explícito habilitado, em ambiente homologado:

```bash
export JORNADA_ACCESS_KEY='CHAVE_APENAS_NO_AMBIENTE_SEGURO'
python -m apoio --entrada /origem --perfil /perfil/perfil.json --saida /saida --enviar
```

Se o envio falhar, retransmita **o ZIP já gerado**, validando novamente manifesto, versões, contratos e integridade, sem recompactar:

```bash
python -m apoio --reenviar-zip /saida/ENTREGA_GESTOR_ORIGEM_v2_<sha256>.zip --perfil /perfil/perfil.json --saida /saida
```

O perfil também aceita `"enviar": true`. O parâmetro CLI `--nao-enviar` desabilita envio mesmo quando o perfil tiver `enviar=true` (segurança em DEV). **Segredos jamais devem constar do perfil, imagem, pacote, Git ou relatório**.

## Docker (execução sem GUI)

```bash
docker build -t apoio-secretarias:0.1 .
docker run --rm \
  -v "$(pwd)/exemplos/entrada:/origem:ro" \
  -v "$(pwd)/exemplos:/perfil:ro" \
  -v "$(pwd)/saida:/saida" \
  apoio-secretarias:0.1 --entrada /origem --perfil /perfil/perfil.sintetico.json --saida /saida --nao-enviar
```

**Em Windows PowerShell**, ajuste as expressões de diretório para caminhos absolutos e permissões dos volumes. A imagem não é do banco da Jornada: carrega somente aplicação e dependências, recebendo schemas/mapeamentos montados em `/perfil:ro`. Para envio, monte segredo de modo protegido/secret manager e injete `JORNADA_ACCESS_KEY`, sem colocá-lo na linha de comando visível a outras contas.

## Construção do executável Windows

Uma vez em **repositório Git independente**, o workflow `.github/workflows/build.yml` executa testes Ubuntu/Windows e gera dois executáveis PyInstaller no artefato `apoio-secretarias-windows-x64`: `ApoioSecretarias.exe` (interface) e `ApoioSecretariasCLI.exe` (automação). Não depende do instalador da Jornada.

Também é possível executar na estação Windows (com Python instalado):

```powershell
py -3.13 -m pip install -r requirements-build.txt
py -3.13 -m unittest discover -s testes -v
py -3.13 -m PyInstaller --clean --onefile --windowed --name ApoioSecretarias executar_interface.py
py -3.13 -m PyInstaller --clean --onefile --console --name ApoioSecretariasCLI executar_cli.py
.\install\Instalar-Apoio.ps1 -Dist .\dist
```

O instalador copia os EXEs para `%LOCALAPPDATA%\Jornada\ApoioSecretarias` e cria atalho na área de trabalho, sem alterar configurações da Jornada. O desinstalador retira **somente** binários; não apaga pacotes nem dados da Secretaria.

## Contratos e segurança

Perfil de exemplo: `exemplos/perfil.sintetico.json`. Campos externos devem ser definidos pela Secretaria **antes** de processar, sem inferências semânticas. O schema Pessoa/Registro tem arquivo, versão e SHA-256 fixados no perfil; hash divergente reprova toda a seleção. O contrato JSON Schema é validado com Draft 2020-12, inclusive formatos ISO.

Para registros finalísticos, defina outra regra em `arquivos` com `"papel": "registros"`, informe schema factual autorizado em `contratos.registros` e o trio `natureza`, `codigoTipo`, `tipoVersao` no perfil. O ZIP sempre contém `registros.jsonl`, mesmo quando vazio. Cada registro deve referenciar uma `idPessoaEntrega` presente no pacote.

Ativação HML/produção, compatibilidade da versão Pessoa da API receptora, autorização PRODAM, validação do conteúdo real da Secretaria, instalação Windows e contêiner buildados com hash verificável **são gates distintos**. Não tratar EXE de CI como assinatura institucional ou instalador de produção homologado.

## Situação de entrega

- **Executado neste ambiente:** suíte de testes locais; geração determinística de pacote sintético; relatório JSON/HTML; cenários negativos fail-closed; transmissão exercitada por simulação em teste, **sem contato com API remota**.
- **Ainda não executado aqui:** compilação Windows (ambiente não possui Python para Windows/PyInstaller), build Docker (Docker indisponível), integração SQL/HTTP real, HML/PROD. O workflow está preparado, mas ainda não foi executado em um repositório Git independente.

A versão de protótipo não deve receber dados reais até concluir segurança de credenciais, autorização e validação institucional. Registro de demanda: Jornada #496 e #1186.

## Endurecimento V0.2 — concorrência, hash e relatório

- Um arquivo de origem alterado durante a leitura bloqueia o lote com `ORIGEM_ALTERADA`, sem promover ZIP.
- A transmissão calcula o hash a partir dos **mesmos bytes imutáveis** enviados, não de uma leitura separada do arquivo.
- Mesmo com `--saida` incorretamente apontando para dentro da origem, relatórios de erro vão para um diretório seguro temporário em vez de modificar a seleção.
- Testes negativos cobrem as três garantias. A proteção contra escrita por processos externos ou em dispositivos remotos ainda depende de ACLs e montagem somente-leitura; não considerar isso substituto de homologação.
