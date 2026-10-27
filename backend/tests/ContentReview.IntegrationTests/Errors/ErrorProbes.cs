using System.ComponentModel.DataAnnotations;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;
using ContentReview.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidationException = ContentReview.Domain.Exceptions.ValidationException;
using DomainUser = ContentReview.Domain.Entities.User;

namespace ContentReview.IntegrationTests.Errors;

public sealed class NestedDto
{
    [Required]
    public string? Name { get; set; }
}

public sealed class ModelDto
{
    [Required]
    public string? FullName { get; set; }

    [Range(1, 10)]
    public int Count { get; set; }

    public NestedDto? Nested { get; set; }
}

/// <summary>Test-only endpoints that raise each kind of failure the API has to translate.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/_probe/errors")]
public sealed class ErrorProbeController(ContentReviewDbContext db) : ControllerBase
{
    public const string SecretText = "password=hunter2;Host=db.internal";

    [HttpGet("ok")]
    public IActionResult Ok_() => Ok(new { fine = true });

    [HttpGet("validation")]
    public IActionResult Validation() => throw new ValidationException(
        new Dictionary<string, string[]> { ["FullName"] = ["Full name is required."], ["Bio"] = ["Too long.", "Really too long."] });

    [HttpGet("notfound")]
    public IActionResult NotFoundCase() => throw new NotFoundException($"article 42 of someone else ({SecretText})");

    [HttpGet("forbidden")]
    public IActionResult Forbidden() => throw new ForbiddenException($"internal reason ({SecretText})");

    [HttpGet("conflict")]
    public IActionResult Conflict_() => throw new ConflictException("The invitation was already used.");

    [HttpGet("transition")]
    public IActionResult Transition() => throw new InvalidTransitionException("A Draft article cannot be approved.");

    [HttpGet("concurrency")]
    public IActionResult Concurrency() => throw new DbUpdateConcurrencyException(SecretText);

    [HttpGet("unique")]
    public async Task<IActionResult> Unique()
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(DomainUser.Create("dup@demo.local", "hash", "First", UserRole.Author, now));
        db.Users.Add(DomainUser.Create("dup@demo.local", "hash", "Second", UserRole.Author, now));
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("crash")]
    public IActionResult Crash() => throw new InvalidOperationException($"boom {SecretText}");

    [HttpPost("model")]
    public IActionResult Model(ModelDto dto) => Ok(dto);
}
