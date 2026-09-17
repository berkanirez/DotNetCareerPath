using System.ComponentModel.DataAnnotations;

namespace FieldOps.Api.Models;

// OrganizationId deliberately removed (Day 35) — which organization an
// employee belongs to is no longer something the client gets to state in
// the request body. It comes from the X-Organization-Id header instead
// (see EmployeesController), the same way a real system would derive it
// from an authenticated identity rather than trusting client-supplied data.
public record CreateEmployeeRequest([Required, StringLength(200)] string Name);
