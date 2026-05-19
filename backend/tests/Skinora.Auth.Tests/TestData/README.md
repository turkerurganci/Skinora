# Test Data

## GeoIP2-Country-Test.mmdb

- **Source:** https://github.com/maxmind/MaxMind-DB/tree/main/test-data
- **License:** Apache 2.0 (MaxMind, Inc.)
- **Purpose:** Unit test fixture for `MaxMindCountryResolver` (T83 — 02 §21.1).
- **Notes:** Production deployments load a real `GeoLite2-Country.mmdb`
  from MaxMind per `Docs/INTEGRATION_RUNBOOKS/GEOIP_SETUP.md`.
  This fixture is NEVER shipped with backend artifacts.
