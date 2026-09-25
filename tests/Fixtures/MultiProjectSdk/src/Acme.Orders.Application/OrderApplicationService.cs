using System;
using Acme.Orders.Domain;

namespace Acme.Orders.Application;

public interface IOrderRepository
{
    Order? Find(Guid orderId);

    void Save(Order order);
}
public sealed class OrderApplicationService
{
    private readonly IOrderRepository _repository;

    public OrderApplicationService(IOrderRepository repository)
    {
        _repository = repository;
    }

    public Money AddCharge(Guid orderId, Money charge)
    {
        Order order = _repository.Find(orderId)
            ?? throw new InvalidOperationException("Order was not found.");

        order.AddCharge(charge);
        _repository.Save(order);
        return order.CalculateTotal(charge.Currency);
    }
}
