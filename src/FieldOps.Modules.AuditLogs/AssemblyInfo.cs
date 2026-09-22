using System.Runtime.CompilerServices;

// Day 48/52: same narrow, test-only exception as every other module — only
// FieldOpsApiFactory needs this, to run migrations against a Testcontainers
// database before tests execute.
[assembly: InternalsVisibleTo("FieldOps.Api.Tests")]
