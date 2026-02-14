using System.ComponentModel.DataAnnotations;

namespace API.Models.DTOs;

public class CreateTaskRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "Title cannot be empty.")]
    public required string Title { get; set; }
    public string? Description { get; set; }
}