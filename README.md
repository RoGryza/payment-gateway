# Payments Gateway

## Assumptions

* Acquiring bank can comfortably handle our traffic (no need for request
throttling).
* The given bank API should be used as-is.

## Usage

Tests and running require the bank simulator (`docker compose up`).

tests:
```
dotnet test
```

running:
```
$ dotnet run --project src/PaymentGateway.Api
$ curl -ki https://localhost:7092/api/Payments/935b998a-c4d0-4041-b86d-a5eb1739f552
```

docker build:
```
$ docker build -f src/PaymentGateway.Api/Dockerfile -t payment-gateway-api .
```

`/health` is the liveness check and `/metrics` exposes Prometheus metrics. The
latter is unauthenticated, in production it would be served on a separate
internal port or behind the ingress.

## API changes

Compared to the reference repository:

* Changed CardNumberLastFour to string.
* Change status enums to strings (reference repository serialises as ints).
* Added an optional IdempotencyKey to PostPaymentRequest for idempotency support.

| Status | Meaning |
|---|---|
| 200 | Payment processed: `Authorized`, `Declined` or `Rejected`. Also returned when replaying an identical, already resolved request. |
| 400 | Not a usable request: malformed JSON, a field of the wrong type, an empty (all-zero) `idempotencyKey`, or any missing/null required field (`cardNumber`, `expiryMonth`, `expiryYear`, `currency`, `amount`, `cvv`). Nothing is persisted. |
| 404 | `GET` of an unknown payment. |
| 409 | Idempotency key already used with different data, or its first attempt has an unknown outcome. Only possible when a key was supplied. |
| 500 | Our bug. |
| 503 | The acquiring bank is unavailable or timed out. |

Errors use `application/problem+json` (RFC 9457).

## Design decisions

`PaymentsController` maps HTTP to and from `PaymentService`, which owns the
payment lifecycle and idempotency rules and knows nothing about HTTP. The
service returns an outcome (`Processed`, `Replayed`, `Conflict`) and the
controller decides the status code. The one HTTP-specific observability detail,
tagging the server request metric with the retry outcome, stays in the
controller. Span tags, payment metrics and logs are emitted from the service.

`BankClient` and `PaymentsRepository` are injected as concrete classes rather
than behind interfaces because we don't use mocks (see Testing).

### API: 200 `Rejected` vs 400

Since validation failures still create a `Rejected` payment, these are
returned as 200. 400 is reserved for requests that can't be persisted
as a payment, even a `Rejected` one: any missing or `null` required field,
string amounts, etc. Present but invalid values (e.g. an expiry month of 13,
or a year above 9999) are `Rejected`.

### Resiliency

Added limited idempotency support to `POST /api/Payments`. The user may supply
an idempotency key; without one, every request is processed as a new payment
and none of the guarantees below apply. For simplicity keys are global, in
production we'd scope them to a user. In the happy path, repeated POST requests get the
same response, our implementation rebuilds it from the stored payment.

If submitting to the bank fails, we keep the payment's status as `Unresolved`
since we can't know if it was submitted. An unresolved payment is never
returned to the client, so `GET` on one (unreachable in practice) returns 500.

A payment is persisted as `Unresolved` *before* calling the bank, claiming its
idempotency key. A retry therefore never reaches the bank again.

Since the bank does not support idempotency, our own idempotency is limited. We
implemented at-most-once payment submission, guarding against double spending.
In cases where we don't know the state of a request submitted to the bank, we
return 409 to indicate the idempotency key is conflicting. Given the limited
API, this means the user is left with unknown payments in these cases.

Details and limitations of the idempotency support:

* The key is optional, but if provided it must not be the empty (all-zero)
  Guid.
* Retries are compared on the data we retain: last four digits of the card,
  expiry, currency and amount. The full card number and CVV are never stored,
  so a retry that differs only in those is treated as identical.
* Concurrent identical requests: the first claims the key, the others get 409
  while it is in flight. Concurrent races are only covered by the lock in the
  repository, not by dedicated tests.

We deliberately avoided auto-retries on the bank client since it is not
idempotent.

### Validation

Request validation is defined inline with DataAnnotations. This keeps
declarative validation rules close to the fields they check against. Card
expiry check is also done as part of request validation. A card is valid
through the end of its expiry month (UTC), and the expiry year is capped at
9999 since the bank expects a four digit year.

### Observability

The HTTP API and our bank client are traced, logged and expose prometheus metrics.
We use standard asp.net logs and metrics, and enrich with some of our own metrics and span fields.

* Logs are structured JSON, correlated with traces via trace and span ids.
  Card numbers and CVVs are never logged. Payment lifecycle events are logged
  by the payment service, request and bank client calls by the framework.
* A single exception handler classifies failures as originating in this
  service or a dependency (tagged on the span and on the standard HTTP server
  metrics), and maps them to 500/503.
* `payments.processed` counts outcomes by status and currency. Currency is
  caller-supplied on rejected requests, so unsupported values are reported as
  `other` to keep metric cardinality bounded.

### Testing

There weren't any compelling cases for traditional unit tests here, like complex
domain code and algorithms.

Testing is done via integration tests which run the full HTTP application. I
default to these to validate the application end-to-end, without the need for
much duplication or mocking (e.g. multiple tests checking the same operation in
different layers). Where the bank simulator can't produce a failure (timeouts,
a 4xx from the bank) the test stubs the bank's `HttpMessageHandler` instead.

Tests are structured around helpers to minimise per-test changes when the application
changes. The idea is keeping changes mostly to tests that directly validate the
operations being changed, so we keep only the operation under test explicit.
The assertion helpers help make sure we are keeping our checks consistent (e.g.
a payment was stored with the expected data).

In real code I'd use some form of fuzzing (e.g. model checking via swarm testing) to
consider this production ready. I avoided them here as it would considerably
increase code size for such a small application. These complement the
integration tests by automatically exploring more complex input sequences
overlaid with fault injection and concurrency, avoiding test code size
explosion and catching errors the engineer did not preempt.
