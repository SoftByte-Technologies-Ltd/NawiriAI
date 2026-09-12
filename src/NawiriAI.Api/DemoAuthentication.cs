using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NawiriAI.Abstractions;
using NawiriAI.Connectors;

namespace NawiriAI.Api;

public sealed class DemoAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = configuration["NAWIRIAI_DEMO_TOKEN"];
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(expected) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());
        var supplied = header[7..];
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            return Task.FromResult(AuthenticateResult.Fail("Invalid demo credential."));
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, SyntheticBusinessDataProvider.SampleUser)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

/// <summary>Replace this host adapter with a context derived from your production identity/ACL system.</summary>
public interface IBusinessSecurityContextAccessor
{
    BusinessSecurityContext GetContext(HttpContext httpContext);
}

public sealed class DemoSecurityContextAccessor : IBusinessSecurityContextAccessor
{
    public BusinessSecurityContext GetContext(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true ||
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) != SyntheticBusinessDataProvider.SampleUser)
            throw new BusinessAccessException("An authenticated demo user is required.");
        return SyntheticBusinessDataProvider.CreateDemoContext(httpContext.TraceIdentifier);
    }
}
