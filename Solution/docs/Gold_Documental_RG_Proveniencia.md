# Gold documental — RG 1:N e proveniência (#903)

## Contrato de dados

O Linkage é o único responsável pela identidade. Canais não escolhem o vínculo e a Gold se abstém de publicar candidatos quando uma observação aponta para UUIDs distintos.

A projeção `silver.vw_documento_evidencia_candidato_gold` admite apenas evidências válidas, modelos publicados e atributos com comprovação admissível. `gold.vw_documento_evidencia_candidato_uuid` associa as ocorrências a UUIDs não ambíguos existentes na Gold. As duas projeções são somente leitura.

Para RG, `gold.vw_rg_documento_distinto` representa **vários RGs por pessoa**, com identidade documental composta por UUID, número normalizado, emissor e UF. Ocorrências repetidas do mesmo RG são agrupadas, sem eleição de documento vencedor por data; RGs distintos permanecem separados. Falta de número, emissor ou UF impede consolidação segura, mas não apaga o candidato Silver. `gold.vw_rg_documento_proveniencia` mantém a relação entre RG identificado e todas as evidências de origem distintas.

## Instalação e validação

Migração: `database/migrations/20261010_Gold_Documento_Candidatos_903.sql`.

Smoke: `database/tests/20261010_Gold_Documento_Candidatos_903_Smoke.sql`, somente em `JornadaE2E` ou `JornadaSyntheticDev`. A verificação estrutural e a simulação com variáveis não substituem testes com dados inseridos nas tabelas reais. Para aceite E2E, popular a base descartável com pelo menos dois RGs distintos para um UUID e duas evidências independentes para o mesmo RG; habilitar `SESSION_CONTEXT('gold_rg_fixture_required')=1` antes do smoke. Sem a fixture, não declarar a validação comportamental concluída.

## Limites

Não existe alteração automática de vínculo, calibração, escolha manual de campos por canal nem persistência física de coleção RG neste incremento. Outros tipos documentais exigem política de chave própria; não generalizar a chave de RG.
