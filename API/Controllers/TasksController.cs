using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Shared.Enums;
using Shared.Events;
using API.Data.Entities;
using API.Data.DTOs;
using Shared.Messages;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly PrimaryDbContext _db;

    public TasksController(PrimaryDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskItem>>> Get()
    {
        var items = await _db.Tasks.ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TaskItem>> Get(Guid id)
    {
        var item = await _db.Tasks.FindAsync(id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<TaskItem>> Post(CreateTaskRequest request)
    {
        var item = new TaskItem
        {
            Title = request.Title,
            Description = request.Description
        };

        var outbox = new OutboxMessage
        {
            AggregateId = item.Id,
            Type = EventType.TaskCreated,
            Data = JsonSerializer.Serialize(new TaskCreatedEvent { TaskId = item.Id, Title = item.Title, Description = item.Description })
        };

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            _db.Tasks.Add(item);
            _db.OutboxMessages.Add(outbox);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }
}
