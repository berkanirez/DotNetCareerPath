using System.Runtime.CompilerServices;

// Day 48: same narrow, test-only exception as the other modules — only
// FieldOpsApiFactory needs this.
[assembly: InternalsVisibleTo("FieldOps.Api.Tests")]
