using System.ComponentModel.DataAnnotations;

namespace FieldOps.Api.Models;

public record AssignWorkOrderRequest([Range(1, int.MaxValue)] int EmployeeId);
