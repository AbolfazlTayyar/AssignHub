# .NET Developer Task

## 1. Automatic Assignment of the Right Expert

Each request has a subject, e.g. financial, legal, or technical. Each expert:

- Has one or more specialties.
- Has a defined capacity for open requests.
- May be active or inactive.

Implement a system that assigns a request to an **active** expert with a **matching specialty** and the **lowest ratio of open requests to capacity**.

**Example:** Expert 1 has 2 requests out of a capacity of 4 (ratio 0.5); Expert 2 has 3 requests out of a capacity of 10 (ratio 0.3). Expert 2 is selected.

If no suitable expert is available, the request should remain **pending**. For ties, define a specific rule and write down your reasoning for it.

## 2. Maintaining System Correctness Under Concurrent Requests

Several assignment requests may arrive at the same time, or the client may repeat the submission of a request because of a lost connection.

Implement a solution that guarantees:

- No expert's capacity is ever exceeded.
- A single request is never assigned to two experts.
- Repeating a submission with the same `RequestKey` does not create a new request.
- It works correctly even when two instances of the application run against a shared database.

Show with a test that if **20 concurrent requests** arrive for an expert with a **capacity of 5**, only 5 requests are assigned and the rest remain pending.

## 3. Processing the Waiting Queue with Priority

Requests have a priority of either **"Normal"** or **"Urgent"**. When a request is closed, the expert's capacity is freed, and the system must pick the next suitable request from the waiting queue.

- Urgent requests take precedence over normal ones.
- A normal request must not wait forever because urgent requests keep arriving (no starvation).
- A request with no matching expert must not block processing of the rest of the queue.
- Design the selection policy yourself and explain it with an example.
