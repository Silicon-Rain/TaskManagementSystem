using System;
using Shared.Models;

Console.WriteLine("Producer starting...");
var t = new TaskItem { Title = "Sample task from Producer", Description = "Created by Producer" };
Console.WriteLine($"Produced task: {t.Id} - {t.Title}");
