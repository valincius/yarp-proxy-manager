using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProxyManager.Api.Controllers;

/// <summary>
/// Base for authenticated API controllers. Antiforgery validation for mutating requests
/// is performed by <see cref="Middleware.AntiforgeryValidationMiddleware"/>.
/// </summary>
[ApiController]
[Authorize]
public abstract class ApiControllerBase : ControllerBase;
