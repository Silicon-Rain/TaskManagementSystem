# TaskManagement Solution

A modern .NET distributed system demonstrating the Transactional Outbox & Inbox patterns using ASP.NET Core, Kafka, and PostgreSQL. This project simulates a Work Request System. Instead of a simple "Save and Forget" app, this system handles long-running, high-stakes tasks (like generating financial reports or sending bulk notifications) that must be processed reliably, even if parts of the system crash.

| Status | Business Perspective (User) | Technical Perspective (Under the Hood) |
|---|---|---|
| Pending | "We've received your request and it's queued for processing." | Record saved in Tasks and OutboxMessages within a single SQL transaction in DB_A. |
| Processing | "A worker has claimed your task. Work is currently in progress." | The consumer has pulled the event from Kafka and registered it in the InboxMessages table in DB_B. |
| Completed | "Success! Your task is finished and results are ready." | The Email/Logger services have finished. An update was sent back to mark the task as finalized. |
| Failed | "Something went wrong. Our team has been notified." | An exception occurred during processing. The system has exhausted retries without success. |

Kafka topic: task-events

Event Types:
TaskCreated: Triggered when the user hits the POST endpoint.
TaskStatusChanged: Triggered when the worker finishes the job.

Consumer Groups:
EmailService Group
LoggingService Group

Quick start (requires .NET SDK 8.0+):

```powershell
dotnet restore
dotnet build
dotnet run --project API
dotnet run --project Producer
dotnet run --project Worker
```
