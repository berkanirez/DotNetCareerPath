using System.Runtime.CompilerServices;

// Day 48: same narrow, test-only exception as FieldOps.Modules.Organizations
// — only FieldOpsApiFactory needs this, to apply migrations against a
// Testcontainers-managed SQL Server.
[assembly: InternalsVisibleTo("FieldOps.Api.Tests")]
