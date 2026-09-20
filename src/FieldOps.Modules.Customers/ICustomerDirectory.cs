namespace FieldOps.Modules.Customers;

public interface ICustomerDirectory
{
    IReadOnlyList<CustomerSummary> GetAll();
    CustomerSummary? GetById(int id);
}
