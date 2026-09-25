using System;
using System.Collections.Generic;

namespace Acme.Orders.Domain;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DomainConceptAttribute : Attribute
{
    public DomainConceptAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
public interface IIdentified<out TId>
{
    TId Id { get; }
}

public interface IAggregateRoot
{
}

public abstract class AuditedEntity
{
    protected AuditedEntity(DateTime createdAtUtc)
    {
        CreatedAtUtc = createdAtUtc;
    }

    public DateTime CreatedAtUtc { get; }
}

public enum OrderStatus
{
    Draft,
    Submitted,
    Cancelled
}

public readonly struct CustomerId
{
    public CustomerId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }
}

public sealed record OrderNumber(string Value);

public readonly record struct Money(decimal Amount, string Currency);

[DomainConcept("aggregate-candidate")]
public sealed class Order : AuditedEntity, IAggregateRoot, IIdentified<Guid>
{
    private readonly List<Money> _charges = new();

    public Order(Guid id, OrderNumber number, CustomerId customerId, DateTime createdAtUtc)
        : base(createdAtUtc)
    {
        Id = id;
        Number = number;
        CustomerId = customerId;
        Status = OrderStatus.Draft;
    }

    public Guid Id { get; }

    public OrderNumber Number { get; }

    public CustomerId CustomerId { get; }

    public OrderStatus Status { get; private set; }

    public IReadOnlyList<Money> Charges => _charges;

    [DomainConcept("operation")]
    public void AddCharge(Money charge)
    {
        _charges.Add(charge);
    }

    public Money CalculateTotal(string currency)
    {
        decimal total = 0;
        foreach (Money charge in _charges)
        {
            total += charge.Amount;
        }

        return new Money(total, currency);
    }
}
