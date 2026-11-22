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
    def test_sql_unavailable_fails_closed(self):
        s = SOURCE.read_text()
        self.assertIn("catch (SqlException)", s)
        self.assertIn("return false;", s)
        self.assertIn("reader.IsDBNull(1) ? null : reader.GetString(1)", s)

if __name__ == "__main__":
    unittest.main()
