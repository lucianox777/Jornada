# Third-party notices

## nomesbr (reference catalog only)

`Jornada.Contracts.BrazilianNameComponents` is a **new, conservative C#
classification** inspired by the public Ipea `nomesbr` particle/agnome/title
catalog; the upstream destructive `simplifica_PARTICULAS_AGNOMES_PATENTES()`
implementation is **not** ported. The original complete name, including
FILHO/NETO/JUNIOR and diagnostic titles, is retained.

- Upstream repository: `ipeadata-lab/nomesbr`
- Frozen reference commit: `3b4a9eb10d994d70af3cc95d6c342e6038d2b573`
- Upstream package version: `0.1.1`
- Upstream license: MIT; original authorship Ipea
- Local experimental contract: `PERSON_NAME_BRAZILIAN_COMPONENTS_V1`
- Runtime dependency on the upstream R package: none

The reference is version-locked for auditability. A subsequent upstream revision
or local rule change requires a new local version and new conformance tests.


## metaphonebr

`Jornada.Contracts.MetaphoneBr` is a C# adaptation of the public `metaphonebr` algorithm maintained by Ipea.

- Upstream repository: `ipeadata-lab/metaphonebr`
- Frozen reference commit: `17fdee95581442cdcc98fddc30aea3079caf27ae`
- Upstream package version: `0.0.5`
- License: MIT
- Upstream copyright: Ipea, 2025
- Jornada algorithm contract: `PERSON_NAME_METAPHONE_BR_V1`

The upstream implementation is not vendored as a runtime dependency. Jornada keeps its adaptation versioned and verifies it against the exact conformance vectors published by the frozen upstream commit. Any intentional semantic change requires a new Jornada algorithm version and new calibration evidence.
