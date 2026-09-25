using System;

namespace Legacy.Customer.Contracts
{
    [Serializable]
    public class CustomerRecord : RecordBase, ICustomerRecord
    {
        public CustomerRecord(int customerId, string displayName)
        {
            CustomerId = customerId;
            DisplayName = displayName;
        }

        public int CustomerId { get; private set; }

        public string DisplayName { get; set; }

        public string Describe()
        {
            return CustomerId + ":" + DisplayName;
        }
    }

    public abstract class RecordBase
    {
    }

    public interface ICustomerRecord
    {
        int CustomerId { get; }

        string Describe();
    }
}
