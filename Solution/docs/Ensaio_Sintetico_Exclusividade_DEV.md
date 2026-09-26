# Exclusividade do Processor no ensaio sintético DEV

O ensaio de 20.000 pessoas em três ondas coloca os ZIPs gerados em uma
**Bronze temporária local** configurada em `BronzeStorage__RootPath` somente
para sua API e seu Processor. Em 22/09/2026 uma entrega entrou em
`QUARENTENA` com `erro_codigo=BRONZE_OBJETO_NAO_ENCONTRADO`,
`tentativa_count=2` e `recuperacao_count=1`. Havia Processors vivos nos
nós `NODE1` e `NODE2` conectados ao mesmo SQL. Isso é consistente com
outro nó reservando a entrega sem acesso ao arquivo local; o log do nó é
necessário para determinar qual processo realmente a reservou.

## Escolha de isolamento para executar novamente

**Preferível: banco DEV separado para o ensaio.** O provisionador local
PowerShell respeita `JORNADA_LOCAL_ENV_FILE` e
`JORNADA_SQL_DATABASE`. Faça uma cópia privada de `.env` chamada `.env.synthetic.local` (ignoradа pelo Git), defina
nela `JORNADA_SQL_DATABASE=JornadaSyntheticDev` (mantendo a mesma porta
e a senha do contêiner SQL já existente), e aponte
`JORNADA_LOCAL_ENV_FILE` para essa cópia antes de executar
`local-synthetic-calibration.ps1 -People 20000 -Waves 3 -Seed 42
-ErrorProfile correlated`. O `local-db.ps1 up` cria esse banco se
necessário. Os Processors de cluster devem permanecer apontados para
o banco original, não para `JornadaSyntheticDev`. A limpeza do ensaio
atinge **somente o banco configurado**, mas é destrutiva para seus dados
operacionais: utilize um banco descartável.

**Alternativa operacional:** parar os Processors externos de todos os
nós conectados ao banco DEV antes do ensaio (e mantê-los parados até o
fim), sem precisar parar outros componentes de cluster. Verificar que
não há heartbeat `Processor/RUNNING` nos últimos 35 segundos. Não
fazer isso em HML/Produção; o ensaio exige marcador
`Jornada.EnvironmentProfile=Development`.

## Proteções implementadas

Os wrappers PowerShell e Bash executam
`Jornada_Dev_SyntheticCalibration_ExclusivePreflight.sql` **antes**
do script de limpeza. O preflight confirma o marcador Development,
exige o schema de heartbeat e recusa qualquer Processor RUNNING com
heartbeat nos últimos 35 segundos. A rotina C# repete o exame
antes de gerar dados e imediatamente antes de iniciar sua própria
API/Processor, tanto na carga única quanto nas cargas em ondas.

Essa guarda não é um lock distribuído: **não impede** outro nó de
entrar online depois da última verificação. O isolamento por banco é
a proteção apropriada contra essa corrida. Uma Bronze compartilhada
entre todos os nós seria necessária para um ensaio de cluster com
consumidores concorrentes; não configure os ZIPs temporários como se
fossem compartilhados quando não forem.

Falhas terminais futuras do Ensaio incluem o `erro_codigo` estável
fornecido pelo endpoint de status. O motivo da quarentena deve ser
diagnosticado antes de limpar ou reiniciar o banco. A divergência de
bundle exibida pelo Monitor Operacional é uma checagem independente
de configuração, não uma explicação automática da quarentena.

**Limite:** os testes de contrato/CI não provam a execução local das três
ondas, e o modelo criado continua RASCUNHO, sem ativação ou promoção.


## Histórico do gate TEST e critério de retomada

Depois de isolar o banco DEV, a carga avançou até o Parameters.Worker.
O candidato FS foi escolhido exclusivamente com VALIDATION, mas o
conjunto TEST congelado registrou 3 falsos vínculos. Na ocasião, o Worker recusou a publicação do RASCUNHO sob o gate legado de zero FP. O novo orçamento FP de engenharia (100 bp por default no Worker) não prova retroativamente que esses três FP atendem ao limite: é necessário conferir os denominadores de VALIDATION/TEST e os FP por classe, assim como FN e recall. O threshold não pode ser ajustado olhando o mesmo TEST conhecido. O PR #446 passou a
persistir classes e denominadores agregados desse resultado em
`identidade.modelo_linkage.falha_resumo`, sem identificadores pessoais.

O novo `scripts/local-synthetic-diagnostics.ps1` consulta somente
o banco DEV configurado em `.env.synthetic.local`. Executá-lo **antes**
de limpar o banco preserva a evidência agregada: referências IBGE
disponíveis, últimos modelos FALHOU com razões do TEST e estados/erros
dos lotes. Os wrappers PowerShell/Bash também tentam exibir esse
diagnóstico automaticamente quando o Ensaio termina com erro. No
PowerShell, na pasta `Solution`, use uma única linha:

```powershell
.\scripts\local-synthetic-diagnostics.ps1 -EnvFile .\.env.synthetic.local
```

A investigação seguinte deve partir dessas contagens, confrontar
VALIDATION versus TEST e só propor uma mudança de algoritmo/regra a
partir de hipóteses e dados de treino independentes. Uma nova rodada
confirmatória exige novos seeds e TEST ainda não inspecionado. O diagnóstico não altera o TEST histórico nem ativa modelo. A execução confirmatória exige novo TEST, medidas por estrato e conferência governada com a tolerância técnica previamente declarada.

**Banco original:** o `JornadaLocal` com NODE1/NODE2 serve para
ensaios posteriores de integração de cluster com Bronze compartilhada.
Executar diretamente nele o wrapper de ondas atual apagaria estado
operacional e permitiria a outro Processor reservar um ZIP guardado
somente no computador local. Para comparar referência e schema do
banco original, o caminho seguro é trabalhar em uma cópia DEV ou
reproduzir o snapshot de referência no banco isolado. Mesmo em bancos
distintos, a instância SQL e seu I/O continuam compartilhados.
