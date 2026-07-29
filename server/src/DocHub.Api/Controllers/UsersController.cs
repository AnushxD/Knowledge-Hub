using DocHub.Api.Infrastructure.Auth;
using DocHub.DataAccess.Entities;
using DocHub.Services;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocHub.Api.Controllers;

/// <summary>
/// Account administration. There is no self-registration, so this is how
/// everyone but the seeded administrator and verified Google sign-ins gets in.
/// </summary>
[ApiController]
[Route("api/users")]
[Produces("application/json")]
[Authorize(Policy = Policies.Admin)]
public sealed class UsersController(
    UserManager<User> users,
    ICurrentUser currentUser,
    ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AccountViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AccountViewModel>> List(CancellationToken ct)
    {
        var accounts = await users.Users
            .OrderBy(user => user.Name)
            .ToListAsync(ct);

        return [.. accounts.Select(Describe)];
    }

    [HttpPost]
    [ProducesResponseType<AccountViewModel>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountViewModel>> Create(
        [FromBody] CreateAccountRequest request)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;

        if (email.Length == 0 || name.Length == 0)
            throw new ValidationException("A name and an email address are required.");

        if (!Roles.IsKnown(request.Role))
            throw new ValidationException($"Role must be one of: {string.Join(", ", Roles.All)}.");

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            // An administrator creating the account is the confirmation; there
            // is no mail server here to send a link through.
            EmailConfirmed = true,
            Name = name,
            Role = request.Role,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // No password creates a Google-only account. That is a real case, not
        // an oversight — it is the right shape for an org where the directory
        // owns credentials and this app should never hold one.
        var created = string.IsNullOrWhiteSpace(request.Password)
            ? await users.CreateAsync(user)
            : await users.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            throw new ValidationException(
                string.Join(" ", created.Errors.Select(error => error.Description)));
        }

        logger.LogInformation(
            "User {ActorId} created account {UserId} as {Role}",
            currentUser.Id, user.Id, user.Role);

        return CreatedAtAction(nameof(List), new { id = user.Id }, Describe(user));
    }

    [HttpPut("{id:guid}/role")]
    [ProducesResponseType<AccountViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<AccountViewModel> ChangeRole(Guid id, [FromBody] ChangeRoleRequest request)
    {
        if (!Roles.IsKnown(request.Role))
            throw new ValidationException($"Role must be one of: {string.Join(", ", Roles.All)}.");

        var user = await users.FindByIdAsync(id.ToString())
            ?? throw new NotFoundException("User", id);

        // Demoting yourself is how an installation ends up with no
        // administrator at all, and the only fix then is a database edit.
        if (user.Id == currentUser.Id && request.Role != Roles.Admin)
        {
            throw new ValidationException(
                "You cannot remove your own administrator role. Ask another administrator.");
        }

        user.Role = request.Role;

        var updated = await users.UpdateAsync(user);

        if (!updated.Succeeded)
        {
            throw new ValidationException(
                string.Join(" ", updated.Errors.Select(error => error.Description)));
        }

        // Existing sessions carry the old role in their cookie. Rolling the
        // stamp forces them to be revalidated, so a revoked permission takes
        // effect now rather than whenever the cookie happens to expire.
        await users.UpdateSecurityStampAsync(user);

        logger.LogInformation(
            "User {ActorId} changed {UserId} to {Role}", currentUser.Id, user.Id, user.Role);

        return Describe(user);
    }

    /// <summary>
    /// Locks an account out indefinitely.
    ///
    /// Deliberately not a delete: the row owns documents, folders and chat
    /// sessions, and deleting it would either cascade away a colleague's work
    /// or leave content with no owner. Someone who has left should stop being
    /// able to sign in, not stop having written anything.
    /// </summary>
    [HttpPost("{id:guid}/disable")]
    [ProducesResponseType<AccountViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<AccountViewModel> Disable(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString())
            ?? throw new NotFoundException("User", id);

        if (user.Id == currentUser.Id)
            throw new ValidationException("You cannot disable your own account.");

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(user);

        logger.LogInformation("User {ActorId} disabled {UserId}", currentUser.Id, user.Id);

        return Describe(user);
    }

    [HttpPost("{id:guid}/enable")]
    [ProducesResponseType<AccountViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<AccountViewModel> Enable(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString())
            ?? throw new NotFoundException("User", id);

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);

        logger.LogInformation("User {ActorId} re-enabled {UserId}", currentUser.Id, user.Id);

        return Describe(user);
    }

    private static AccountViewModel Describe(User user) =>
        new(
            user.Id,
            user.Name,
            user.Email ?? string.Empty,
            Roles.IsKnown(user.Role) ? user.Role : Roles.Viewer,
            user.PasswordHash is not null,
            user.LockoutEnd > DateTimeOffset.UtcNow,
            user.CreatedAt);
}
