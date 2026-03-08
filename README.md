# 🚀 Task Management System (v2.0)

A high-performance, event-driven distributed system built with .NET 10. This project demonstrates advanced reliability patterns for microservices, specifically solving for **Eventual Consistency**, **Idempotency**, and **Fault Tolerance**.

---

## 🏗️ Architecture & Tech Stack

This solution follows a decoupled, service-oriented model designed for high throughput and system resilience.

* **Runtime:** `.NET 10.0`
* **Message Broker:** `Apache Kafka`
* **Database:** `PostgreSQL` (Dual-instance setup for Primary API and Worker persistence)
* **Resiliency:** `Polly` (Exponential Backoff & Retries)
* **Orchestration:** `Docker Compose`

---

## 🔥 New in v2.0: The Resiliency Overhaul

### 1. Parallel Processing (Semaphore Throttling)
The Worker now utilizes `SemaphoreSlim` to process up to 5 concurrent tasks per container instance. 
* **Performance:** Processing time for 50-task batches reduced from **~50s** to **~10s** (80% improvement).

### 2. Fault Tolerance & Dead Letter Queues (DLQ)
Equipped with **Polly**, the system now distinguishes between transient and permanent failures:
* **Transient Retries:** Automatic exponential backoff (2s, 4s, 8s) for database hiccups or network blips.
* **DLQ Pattern:** "Poison messages" that fail all retries are automatically ejected to a `task-events-failed` topic for inspection, preventing pipeline stalls.

### 3. Clean Architecture Refactor
Business logic has been decoupled from the infrastructure. The Kafka polling logic (Workers) is now separated from the state machine logic (`TaskService`), making the system unit-testable and maintainable.

---

## 🔄 The Reliability Lifecycle

| Status | Business Perspective | Technical Infrastructure |
| :--- | :--- | :--- |
| **Pending** | "Request received." | Atomic write to `Tasks` and `Outbox` in **PrimaryDb**. |
| **Processing** | "Worker has started." | Kafka event consumed; registered in **SecondaryDb** Inbox. |
| **Completed** | "Success!" | Logic finished; status update event sent back to Primary API. |
| **Failed** | "Error occurred." | Exception caught; failure status propagated via Kafka/DLQ. |

---

## 🛠️ Core Patterns

### Transactional Outbox
Ensures atomic database/message operations. We never publish an event if the database transaction fails. The **OutboxPublisherWorker** ensures "at-least-once" delivery to Kafka.

### Inbox (Idempotent Consumer)
The Worker tracks every `AggregateId` in an `InboxMessages` table. This guarantees that even if Kafka delivers a message twice, the business logic only executes once.

### Order-Safe Status Updates
The `TaskStatusUpdateWorker` implements a strict state-progression check to ensure out-of-order Kafka messages (e.g., "Completed" arriving before "Processing") do not corrupt the Primary database state.

---

## 🚀 Quick Start

Ensure you have **Docker Desktop** installed.

### Clone & Launch
```bash
# Clone the repo
git clone [https://github.com/Silicon-Rain/TaskManagementSystem](https://github.com/Silicon-Rain/TaskManagementSystem)
cd TaskManagementSystem

# Start the entire ecosystem
docker-compose up --build -d