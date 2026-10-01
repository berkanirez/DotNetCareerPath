namespace FieldOps.Api.Models;

public record LoginResponse(string Token, int EmployeeId, int OrganizationId, string Role);
