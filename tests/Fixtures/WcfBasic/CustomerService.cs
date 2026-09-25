namespace Fixtures.Wcf.Basic
{
    public sealed class CustomerService : ICustomerService
    {
        public CustomerResponse Update(CustomerRequest request)
        {
            return new CustomerResponse { Updated = request != null };
        }
    }
}
