using ContentReview.Domain.Entities;

namespace ContentReview.Api.Auth;

/// <summary>
/// Author resources are always queried through these filters, so an author can never read or change another
/// author's data. A resource that is not owned simply is not found, and the endpoint answers 404 (not 403) so
/// the response does not reveal that the id exists.
/// </summary>
public static class AuthorOwnership
{
    public static IQueryable<Article> OwnedBy(this IQueryable<Article> articles, Guid authorId) =>
        articles.Where(a => a.AuthorId == authorId);
}
