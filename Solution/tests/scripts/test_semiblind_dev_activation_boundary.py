"""Fail-closed semiblind DEV activation contract, without enabling search."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "src/Jornada.Api/SemiblindSearchActivationGate.cs"
class SemiblindDevActivationBoundary(unittest.TestCase):
    def test_explicit_flag_and_resident_dev(self):
        s = SOURCE.read_text()
        for marker in ['EnabledSetting = "SemiblindIdentitySearch:Enabled"',
                       "!environment.IsDevelopment() || !configuration.GetValue<bool>(EnabledSetting)",
                       "Jornada.EnvironmentProfile",
                       'string.Equals(environmentName, "Development", StringComparison.Ordinal)',
                       'string.Equals(profile, "Development", StringComparison.Ordinal)']:
            self.assertIn(marker, s)
    def test_sql_query_reads_resident_profile_not_database_name_as_authorization(self):
        s = SOURCE.read_text()
        self.assertIn("SELECT DB_NAME()", s)
        self.assertIn("sys.extended_properties", s)
        self.assertIn("name=N'Jornada.EnvironmentProfile'", s)
        self.assertIn('string.Equals(profile, "Development", StringComparison.Ordinal)', s)
        self.assertIn('!string.IsNullOrWhiteSpace(databaseName)', s)
        self.assertNotIn('databaseName.Contains("DEV")', s)

    def test_denies_before_sql_when_flag_or_environment_not_development(self):
        s = SOURCE.read_text()
        preflight = s[s.index("public async Task<bool> IsEnabledAsync"):s.index("try", s.index("public async Task<bool> IsEnabledAsync"))]
        self.assertIn("!environment.IsDevelopment()", preflight)
        self.assertIn("!configuration.GetValue<bool>(EnabledSetting)", preflight)
        self.assertIn("return false;", preflight)
        self.assertNotIn("sql.OpenAsync", preflight)

    def test_eligibility_requires_all_four_conditions(self):
        s = SOURCE.read_text()
        predicate = s.split("internal static bool IsEligible(", 1)[1]
        self.assertIn('enabled', predicate)
        self.assertIn('string.Equals(environmentName, "Development", StringComparison.Ordinal)', predicate)
        self.assertIn('!string.IsNullOrWhiteSpace(databaseName)', predicate)
        self.assertIn('string.Equals(profile, "Development", StringComparison.Ordinal)', predicate)
        self.assertNotIn('StringComparison.OrdinalIgnoreCase', predicate)

    def test_sql_query_is_read_only_and_timeout_bounded(self):
        s = SOURCE.read_text()
        query = s.split('command.CommandText = """', 1)[1].split('"""', 1)[0]
        self.assertIn("sys.extended_properties", query)
        self.assertIn("SELECT DB_NAME()", query)
        for mutation in ("INSERT ", "UPDATE ", "DELETE ", "EXEC ", "DROP "):
            self.assertNotIn(mutation, query.upper())
        self.assertIn("command.CommandTimeout = 5;", s)

    def test_sql_unavailable_fails_closed(self):
        s = SOURCE.read_text()
        self.assertIn("catch (SqlException)", s)
        self.assertIn("return false;", s)
        self.assertIn("reader.IsDBNull(1) ? null : reader.GetString(1)", s)

if __name__ == "__main__":
    unittest.main()
