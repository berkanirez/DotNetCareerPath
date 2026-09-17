using System.ComponentModel.DataAnnotations;

namespace FieldOps.Api.Models;

public record CreateEmployeeRequest(
    [Required, StringLength(200)] string Name,
    [Range(1, int.MaxValue)] int OrganizationId);
