-- SQLCMD only: new database install on 3.70, then post-candidate v3.71 extension.
-- Run ONCE for a fresh database. Existing v3.70+ databases use the upgrade script.
:on error exit
:r database/Jornada_Fase1_v3.70.sql
:r database/postrelease/v3.71/20261107_Territorial_Origem_Desenvolvedor.sql
