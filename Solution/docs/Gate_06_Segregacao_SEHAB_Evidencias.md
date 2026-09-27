# Gate 6 — segregação SEHAB e evidências

**Data da intervenção:** 27/09/2026. **Estado:** execução parcial; gate do Ensaio ABERTO. **SHA inicial de evidência:** [`2def00bf927b15966e924164810bb807dbe6b6cb`](https://github.com/lucianox777/Jornada/commit/2def00bf927b15966e924164810bb807dbe6b6cb).

## Migração técnica verificada pelo diff

- Migrados byte a byte: 5 arquivos de `Solution/clients/Jornada.Integrador.CSharp`, 3 arquivos dos testes do transmissor, 7 contratos Pessoa v1–v5, 1 metadado específico AA01 e 1 configuração exemplo SEHAB. Total: 17 movimentações. Destino: `ApoioSecretarias/`.
- Criada solução `ApoioSecretarias/SolucaoApoioSecretarias.sln`; `Solution/Jornada.sln` já não tinha o projeto do integrador. `global.json` e `Directory.Build.props` copiados para garantir compilação independente. O suporte ainda reside no **mesmo repositório Git** e não constitui outro repositório remoto.
- Os oito SHA-256 de contratos e metadados específicos originais foram preservados em `ApoioSecretarias/config/governance/schema-approvals.SEHAB.json`; o inventário principal agora contém somente os 38 contratos restantes, incluindo SMADS/SMDET/SMS. Nenhuma aprovação institucional foi criada.
- Criado preparador CSV → JSONL → ZIP determinístico, de campos estritamente mapeados, com validação de schema/versão/hash, sem inferir motivo de ausência de CPF. O exemplo CSV e o mapeamento são somente **sintéticos**. Ainda falta mapeamento validado para fonte real e normalizador de registros para fontes SEHAB.
- O workflow `apoio-secretarias` verifica integridade, testes do preparador, build C# e regressão do transmissor. Não equivale à transmissão/recebimento/processamento real.

## Referências remanescentes a classificar antes do fechamento

- `Solution/database/Jornada_Seed_Dev.sql`, scripts de escala/local E2E, chaves de teste e suas fixtures: uso sintético DEV; manter rótulos de gestor quando necessário aos testes, mas garantir que o contrato receptor seja disponibilizado externamente.
- `Solution/database/migrations/20260921_Pessoa_V5_Contrato_371.sql`: histórico de registro de versões e hashes da SEHAB. Exige decisão de implantação/fornecimento externo do schema e auditoria de migração para não alterar histórico.
- `Solution/src/Jornada.Ensaio/EnsaioPlan.cs`: plano do Ensaio ainda vinculado à SEHAB e requer parametrização anterior ao Ensaio.
- `Solution/config/contracts/registros/AA01/v1/registro.json`: metadado específico transferido para ApoioSecretarias; o contrato genérico `registro.schema.json` permanece no catálogo receptor.
- `Solution/src/Jornada.Api/wwwroot/monitor/index.html` contém ocorrência de SEHAB, mas `Jornada.Api/*` está **expressamente fora do escopo autorizado de alterações**. Não certificar ausência universal de hardcodes sem resolução dessa restrição.
- `Documentos/Testes/SEHAB`: histórico de caracterização AS-IS, não arquivo de origem para distribuir; não copiar massa real a outro repositório sem autorização e classificação. Documentos históricos que mencionam SEHAB não são dependências executáveis, mas exemplos operacionais devem apontar para a nova solução.

## Evidência que ainda falta coletar

1. Preparar massa sintética SEHAB com schema v5, validar SHA e integridade do ZIP. Em ambiente segregado, carregar no receptor Jornada o contrato canônico correspondente **sem** duplicar seu código-fonte na solução principal.
2. Enviar pelo C# com credencial DEV controlada, verificar resposta do recebimento e consultar pelo nome exato do ZIP; executar Processor e exigir status final `PROCESSADA` e contagens de pessoas/registros esperadas. Registrar SHA do pacote, commit do receptor, commit do suporte, IDs de execução e logs redigidos.
3. Repetir pipeline com contratos de SMADS, SMDET e SMS, v1–v5 quando aplicável; capturar resultados independentes, testes negativos (schema inválido, idempotência, permissões, replay) e os checks HML exigidos pelo Plano.
4. Confirmar em diff e varredura estática que não restam referências hardcoded operacionais a SEHAB na solução principal, ressalvadas ocorrências DEV sintéticas e arquivo(s) protegidos. Documentar qualquer exceção impeditiva; não ocultá-la em um gate verde.

**Regra de conclusão:** não marcar o item 6 `[x]` nem iniciar Ensaio sem prova verificável dos quatro pontos e sem solucionar as referências remanescentes. Este documento não é certificado de aceite HML.
