-- V9 diagnostic-only index infrastructure, isolated from operational schema.
-- Run ONLY in a disposable JornadaHarness database after explicitly provisioning
-- synthetic rows and the already-versioned name projections in dbo.V9BlockingIndexLab.
-- No creation of the table here: the harness owns its lifecycle and data provenance.
IF DB_NAME() <> N'JornadaHarness'
    THROW 51000, 'V9 index lab is restricted to JornadaHarness.', 1;
IF OBJECT_ID(N'dbo.V9BlockingIndexLab', N'U') IS NULL
    THROW 51001, 'Provision the synthetic diagnostic table first; no operational DDL.', 1;
-- Columns must be populated by the versioned C# projections:
-- PersonNameBasicNormalization / MetaphoneBr (person AND mother).
-- This script never computes approximate phonetics in SQL.
DECLARE @expected TABLE (column_name sysname NOT NULL);
INSERT INTO @expected(column_name) VALUES
 (N'partition'),(N'person_normalized'),(N'person_no_particles'),
 (N'person_prefix3'),(N'person_metaphone'),
 (N'person_first_name'),(N'person_last_surname'),(N'person_surnames'),
 (N'mother_normalized'),(N'mother_no_particles'),
 (N'mother_prefix3'),(N'mother_metaphone'),
 (N'mother_first_name'),(N'mother_last_surname'),(N'mother_surnames'),(N'birth_date');
IF EXISTS (SELECT 1 FROM @expected e
           WHERE COL_LENGTH(N'dbo.V9BlockingIndexLab', e.column_name) IS NULL)
    THROW 51002, 'Missing versioned projection columns in diagnostic table.', 1;
-- Single-feature indexes support dynamic selection of present evidence.
-- Compound indexes for candidate pass winners are deferred to benchmarks.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_person_normalized')
 CREATE INDEX IX_V9lab_person_normalized ON dbo.V9BlockingIndexLab(partition,person_normalized);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_person_no_particles')
 CREATE INDEX IX_V9lab_person_no_particles ON dbo.V9BlockingIndexLab(partition,person_no_particles);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_person_prefix3')
 CREATE INDEX IX_V9lab_person_prefix3 ON dbo.V9BlockingIndexLab(partition,person_prefix3);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_person_metaphone')
 CREATE INDEX IX_V9lab_person_metaphone ON dbo.V9BlockingIndexLab(partition,person_metaphone);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_mother_normalized')
 CREATE INDEX IX_V9lab_mother_normalized ON dbo.V9BlockingIndexLab(partition,mother_normalized);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_mother_no_particles')
 CREATE INDEX IX_V9lab_mother_no_particles ON dbo.V9BlockingIndexLab(partition,mother_no_particles);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_mother_prefix3')
 CREATE INDEX IX_V9lab_mother_prefix3 ON dbo.V9BlockingIndexLab(partition,mother_prefix3);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_mother_metaphone')
 CREATE INDEX IX_V9lab_mother_metaphone ON dbo.V9BlockingIndexLab(partition,mother_metaphone);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_birth_date')
 CREATE INDEX IX_V9lab_birth_date ON dbo.V9BlockingIndexLab(partition,birth_date);

-- Component projections: derived in the harness by versioned name-token policy.
-- Never infer surnames from a fixed number of tokens without an explicit policy.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_person_first_name')
 CREATE INDEX IX_V9lab_person_first_name ON dbo.V9BlockingIndexLab(partition,person_first_name);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_person_last_surname')
 CREATE INDEX IX_V9lab_person_last_surname ON dbo.V9BlockingIndexLab(partition,person_last_surname);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_person_surnames')
 CREATE INDEX IX_V9lab_person_surnames ON dbo.V9BlockingIndexLab(partition,person_surnames);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_mother_first_name')
 CREATE INDEX IX_V9lab_mother_first_name ON dbo.V9BlockingIndexLab(partition,mother_first_name);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_mother_last_surname')
 CREATE INDEX IX_V9lab_mother_last_surname ON dbo.V9BlockingIndexLab(partition,mother_last_surname);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.V9BlockingIndexLab') AND name=N'IX_V9lab_mother_surnames')
 CREATE INDEX IX_V9lab_mother_surnames ON dbo.V9BlockingIndexLab(partition,mother_surnames);
