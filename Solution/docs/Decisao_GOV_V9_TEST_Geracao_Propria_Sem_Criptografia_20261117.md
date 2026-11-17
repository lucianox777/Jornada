# GOV-V9-TEST-GERACAO-PROPRIA-20261117 — decisão aprovada pelo Product Owner

**Data:** 17/11/2026. **Estado:** aprovada; implementação em revisão/CI, ainda não certificada fisicamente.

## Contexto e justificativa

O desenho científico vigente já utiliza três coortes sintéticas distintas de 100.000 pessoas; não particiona uma única coorte: TRAIN é gerada em PREPARE (seed 42) e transferida por artefato verificado; VALIDATION gera sua própria coorte (seed 20261112001); TEST utilizava uma geração antecipada e criptografada em `prepare-test-sealed` (seed 20261113001). O PO confirmou a simplificação do TEST após avaliar a ausência de necessidade de transportar antecipadamente dados sintéticos.

## Decisão vigente e precedência

1. PREPARE gera **somente** a coorte TRAIN, preservando o handoff original e SHA.
2. VALIDATION gera e utiliza sua coorte própria.
3. Após a VALIDATION escolher e congelar um único vencedor operacional, o próprio job TEST gera uma nova coorte sintética de 100.000 pessoas, seed fixa 20261113001, utilizando o gerador e referências estatísticas existentes; somente o vencedor congelado pode ser avaliado, sem refit, reseleção, publicação ou ativação.
4. **Revogar a exigência de lacre criptográfico antecipado, certificado e chave privada para a coorte TEST** e remover o job `prepare-test-sealed`. A criptografia era um mecanismo de impedir consulta antecipada ao artefato; a inexistência da coorte até a etapa TEST elimina esse vetor específico. Isso não significa que ambientes descartáveis garantam, isoladamente, independência científica.
5. Manter validação de SHA do vencedor, controle global de uso único por tag antes de gerar/expor o TEST, seed/parâmetros fixados antes da seleção, execução isolada e descarte dos dados temporários. Falha em qualquer gate impede conclusão científica.
6. A preservação prolongada das três bases como artefatos continua **em avaliação**, não foi aprovada nesta decisão; não publicar base TEST bruta automaticamente.

**Precedência:** substitui somente os trechos de decisões V9 anteriores que obrigavam preparação, cifragem e exposição do TEST antes do TRAIN; preserva obrigatoriedade de TEST quando houver vencedor, isolamento TEST/executor e retrocompatibilidade legada. O preflight de credenciais criptográficas torna-se obsoleto para este fluxo e não deve ser exigido como requisito do TEST; demais gates de segurança permanecem até revisão explícita.

## Impactos, riscos e verificação

A geração do TEST passa a ocorrer após o congelamento do vencedor, não antes do TRAIN. A geração usa a mesma família de gerador da VALIDATION, com semente diferente. Sementes distintas não demonstram disjunção de identidades e referências estatísticas compartilhadas não provam generalização para população real. A tag de exposição é recurso de uso único: uma falha após sua reserva exige novo protocolo de seed, sem apagar a tag para repetir o mesmo TEST. CI estática e E2E físico devem validar o fluxo antes de declará-lo liberado.

**Rastreabilidade:** workflow `.github/workflows/v9-standard-linear-100k.yml`; índice `Solution/docs/Indice_Decisoes_Vigentes.md`; implementação proposta em branch `feat/v9-test-generate-in-own-job-20261117`.
