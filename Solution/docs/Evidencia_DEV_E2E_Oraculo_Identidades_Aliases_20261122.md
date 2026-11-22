# Evidência incremental — separação física do oráculo offline de identidades (22/11/2026)

**Referências:** plano aprovado GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122, etapa 8; frente B, issue #1851; GOV-DOC-01; PRs #1903/#1904 (proteções de hardlink correlatas).

## Contexto e justificativa

O comparador offline `e2e-independent-identity-oracle.py` impunha separação entre gabarito e observado somente por `Path.resolve()`, que não detecta hardlinks para o mesmo inode. Além disso, o CLI escrevia o recibo sem impedir que seu destino fosse um hardlink para o gabarito, as observações ou o ZIP, permitindo sobrescrever entradas independentes. A checagem de nomes/caminhos não comprova separação física.

## Implementação e impactos

- `compare()`: rejeita verdade e observado com a mesma identidade física (`os.path.samefile`) e impede que o ZIP opcional seja o mesmo arquivo físico de qualquer um desses JSONs.
- `main()`: rejeita recibo cuja localização resolvida, hardlink ou symlink já existente aponte a qualquer entrada, antes de ler/processar os dados e escrever o recibo.
- Testes negativos cobrem hardlinks entre entradas, sobrescrita do gabarito, observado e ZIP, symlink de saída e caminho de sucesso com recibo realmente separado.
- Testes explicitamente conectados ao workflow obrigatório `jornada-pr-required-fast.yml`.

## Limitações, precedência e rastreabilidade

Guardas de aliases baseados no estado do filesystem no instante da verificação; não constituem proteção contra concorrência/TOCTOU hostil. Somente contrato offline com dados sintéticos: não é oráculo independente de score ou cluster, não executa HTTP/SQL físico, não é certificação científica nem autorização para TRAIN/TEST, 100k ou HML/PROD.

Incremento de controle de implementação **sem nova decisão do PO**; permanecem vigentes o plano E2E integral e GOV-DOC-01. Não há mudança no `Indice_Decisoes_Vigentes.md` porque não se criou, alterou ou revogou decisão. A integração está condicionada ao CI e às revisões exigidas pelo GitHub.

## Complemento fail-closed: destino simbólico não relacionado (22/11/2026)

**Lacuna identificada na revisão desta PR:** o controle inicial rejeitava symlink do recibo apenas quando apontava para um dos três arquivos de entrada. Um symlink para outro arquivo existente — ou um symlink pendente para arquivo ainda inexistente — permanecia elegível a `Path.write_text()`, que seguiria o link e sobrescreveria o destino. Isso não demonstrou incidente real; é análise do contrato offline. **Correção:** rejeitar explicitamente `args.receipt.is_symlink()` **antes** de `resolve()`/`write_text()`, mantendo a verificação de hardlinks para os insumos. Novos testes cobrem alvo alheio preservado e link pendente sem criação de alvo. A corrida TOCTOU entre checagem e escrita ainda não é eliminada; execução segue restrita a diretórios DEV controlados, sem certificação de sandbox hostil. **Impactos:** reforço negativo da implementação E2E já autorizada, sem alterar contrato de dados, algoritmo, estatística ou liberar Runner, TRAIN/TEST, 100k ou HML/PROD. **Precedência/rastreabilidade:** GOV-DOC-01, plano aprovado etapa 8, issue #1851; nova CI no SHA da PR após atualização.
