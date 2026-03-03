# 🚀 TaskManagement System (v2.0)

A robust, event-driven distributed system demonstrating high-reliability patterns in .NET. This project implements the **Transactional Outbox** and **Inbox (Idempotent Consumer)** patterns to guarantee eventual consistency between decoupled microservices.

---

## 🏗️ Architecture & Tech Stack

This solution follows a **Hub-and-Spoke** dependency model to ensure clean architectural boundaries and optimized container builds.

* **Runtime:** `.NET 10.0`
* **Orchestration:** `Docker` & `Docker Compose`
* **Message Broker:** `Apache Kafka`
* **Database:** `PostgreSQL` (Two-DB setup for Business and Worker logic)
* **Persistence:** `Entity Framework Core` with Automated Migrations



---

## 🔄 Reliability Lifecycle

The system handles long-running tasks by decoupling the API request from the actual work.

| Status | Business Perspective | Technical Infrastructure |
| :--- | :--- | :--- |
| **Pending** | "Request received." | Atomic write to `Tasks` and `Outbox` in **PrimaryDb**. |
| **Processing** | "Worker has started." | Kafka event consumed; registered in **SecondaryDb** Inbox. |
| **Completed** | "Success!" | Logic finished; status update event sent back to Primary API. |
| **Failed** | "Error occurred." | Exception caught; failure status propagated via Kafka. |

---

## 🛠️ Key Architectural Features

### 1. Transactional Outbox Pattern
Ensures that the database update and the Kafka message publication are atomic. If the database save fails, the message is never sent. If the message send fails, the background **Producer** retries until success.

### 2. Idempotent Consumer (Inbox Pattern)
The **Worker** service tracks every processed `AggregateId` in an `InboxMessages` table. This prevents duplicate processing if a Kafka message is delivered more than once.

### 3. Optimized Docker Build
By separating the **Persistence** and **Shared** projects, Docker only rebuilds the layers that change. The API, Producer, and Worker services are small, runtime-optimized images.



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