using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository
{
    private readonly List<Payment> _payments = new();

    public Payment? Get(Guid id)
    {
        lock (_payments)
        {
            return _payments.FirstOrDefault(p => p.Id == id);
        }
    }

    public Payment? GetByIdempotencyKey(Guid idempotencyKey)
    {
        lock (_payments)
        {
            return _payments.FirstOrDefault(p => p.IdempotencyKey == idempotencyKey);
        }
    }

    // Mirrors INSERT ... ON CONFLICT DO NOTHING RETURNING: claims payment.IdempotencyKey if free,
    // otherwise returns whoever already claimed it. A payment without a key claims nothing.
    public (bool Inserted, Payment Existing) TryInsert(Payment payment)
    {
        lock (_payments)
        {
            var existing = payment.IdempotencyKey is null
                ? null
                : _payments.FirstOrDefault(p => p.IdempotencyKey == payment.IdempotencyKey);
            if (existing is not null)
            {
                return (false, existing);
            }

            _payments.Add(payment);
            return (true, payment);
        }
    }

    public void Update(Payment payment)
    {
        lock (_payments)
        {
            var index = _payments.FindIndex(p => p.Id == payment.Id);
            _payments[index] = payment;
        }
    }
}