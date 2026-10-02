using System.Security.Claims;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AudiobookServer.Api.Auth;

/// <summary>
/// Which books a user may see: an admin sees every library; everyone else sees public
/// libraries plus any library they've been granted (LibraryGrant).
///
/// This is the one place that rule lives. Every endpoint that touches a book (list,
/// detail, cover, stream, all three progress endpoints) starts from VisibleBooks
/// rather than db.Books.
///
/// Explicit rather than an EF global query filter. A global filter is harder to forget,
/// but it can't be seen at the call site, and the scanner (which must see every book)
/// would need IgnoreQueryFilters everywhere. VisibilityTests is what catches a
/// forgotten call: it walks every book endpoint as both kinds of user.
///
/// A book the user can't see answers 404, exactly like one that doesn't exist, so
/// the response never confirms that a private book is there.
/// </summary>
public static class Visibility
{
    public static IQueryable<Book> VisibleBooks(this AudiobookDbContext db, ClaimsPrincipal principal)
    {
        var books = db.Books.AsNoTracking();
        if (principal.IsAdmin()) return books;

        // Grants are read here, per request, rather than carried in a claim: a revoke
        // takes effect on the next request instead of at the next stamp check.
        var userId = principal.GetUserId();
        return books.Where(b =>
            b.Library!.IsPublic
            || db.LibraryGrants.Any(g => g.UserId == userId && g.LibraryId == b.LibraryId));
    }
}
