using System.Runtime.CompilerServices;

// Day 48: a deliberate, narrow exception to "the host never sees this
// module's internals" — only the test assembly gets access, specifically
// so FieldOpsApiFactory can construct OrganizationsDbContext directly to
// apply migrations against a Testcontainers-managed SQL Server (mirroring
// StockPilot Day 28's StockPilotApiFactory). FieldOps.Api itself still never
// references OrganizationsDbContext or EfOrganizationDirectory by name.
[assembly: InternalsVisibleTo("FieldOps.Api.Tests")]
