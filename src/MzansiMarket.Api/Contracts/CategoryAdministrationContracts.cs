using System.ComponentModel.DataAnnotations;

namespace MzansiMarket.Api.Contracts;

public sealed class CategoryAdministrationRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required, RegularExpression("^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$"), StringLength(140, MinimumLength = 1)]
    public string Slug { get; init; } = string.Empty;

    public Guid? ParentCategoryId { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed record AdminCategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    Guid? ParentCategoryId,
    bool IsActive,
    int ActiveProductCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);