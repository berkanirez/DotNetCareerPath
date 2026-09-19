using System.ComponentModel.DataAnnotations;

namespace FieldOps.Api.Models;

// Note deliberately stands in for a real file/photo reference — no
// upload/storage infrastructure exists yet (Day 46).
public record AddEvidenceRequest([Required, StringLength(500)] string Note);
