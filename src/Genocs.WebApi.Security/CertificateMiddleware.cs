using Genocs.WebApi.Security.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

namespace Genocs.WebApi.Security;

internal sealed class CertificateMiddleware : IMiddleware
{
    private readonly ICertificatePermissionValidator _certificatePermissionValidator;
    private readonly ILogger<CertificateMiddleware> _logger;
    private readonly SecurityOptions.CertificateOptions _options;
    private readonly HashSet<string> _allowedHosts;
    private readonly IDictionary<string, SecurityOptions.CertificateOptions.AclOptions> _acl = new Dictionary<string, SecurityOptions.CertificateOptions.AclOptions>();
    private readonly IDictionary<string, string> _subjects = new Dictionary<string, string>();
    private readonly bool _validateAcl;
    private readonly bool _skipRevocationCheck;

    public CertificateMiddleware(
                                    ICertificatePermissionValidator certificatePermissionValidator,
                                    SecurityOptions options,
                                    ILogger<CertificateMiddleware> logger)
    {
        _certificatePermissionValidator = certificatePermissionValidator;
        _logger = logger;

        if (options.Certificate is null)
        {
            throw new ArgumentNullException(nameof(options.Certificate));
        }

        _options = options.Certificate;

        _allowedHosts = new HashSet<string>(_options.AllowedHosts ?? Array.Empty<string>());
        _validateAcl = _options.Acl is not null && _options.Acl.Any();
        _skipRevocationCheck = options.Certificate.SkipRevocationCheck;

        if (!_validateAcl)
        {
            return;
        }

        if (_options.Acl != null)
        {
            foreach (var (key, acl) in _options.Acl)
            {
                if (!string.IsNullOrWhiteSpace(acl.ValidIssuer) && !acl.ValidIssuer.StartsWith("CN="))
                {
                    acl.ValidIssuer = $"CN={acl.ValidIssuer}";
                }

                string subject = key.StartsWith("CN=") ? key : $"CN={key}";
                if (_options.AllowSubdomains)
                {
                    foreach (string domain in options.Certificate.AllowedDomains ?? Enumerable.Empty<string>())
                    {
                        _subjects.Add($"{subject}.{domain}", key);
                    }
                }

                _acl.Add(_subjects.Any() ? key : subject, acl);
            }
        }
    }

    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (_options is null)
        {
            return next(context);
        }

        if (!_options.Enabled)
        {
            return next(context);
        }

        if (IsAllowedHost(context))
        {
            return next(context);
        }

        var certificate = context.Connection.ClientCertificate;
        if (certificate is null || !Verify(certificate))
        {
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        }

        if (!_validateAcl)
        {
            return next(context);
        }

        SecurityOptions.CertificateOptions.AclOptions acl;
        if (_subjects.TryGetValue(certificate.Subject, out string? subject))
        {
            if (!_acl.TryGetValue(subject, out var existingAcl))
            {
                context.Response.StatusCode = 403;
                return Task.CompletedTask;
            }

            acl = existingAcl;
        }
        else
        {
            if (!_acl.TryGetValue(certificate.Subject, out var existingAcl))
            {
                context.Response.StatusCode = 403;
                return Task.CompletedTask;
            }

            acl = existingAcl;
        }

        if (!string.IsNullOrWhiteSpace(acl.ValidIssuer) && certificate.Issuer != acl.ValidIssuer)
        {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(acl.ValidThumbprint) && certificate.Thumbprint != acl.ValidThumbprint)
        {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(acl.ValidSerialNumber) && certificate.SerialNumber != acl.ValidSerialNumber)
        {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
        }

        if (acl.Permissions is null || !acl.Permissions.Any())
        {
            return next(context);
        }

        if (_certificatePermissionValidator.HasAccess(certificate, acl.Permissions, context))
        {
            return next(context);
        }

        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    }

    private bool Verify(X509Certificate2 certificate)
    {
        var chain = new X509Chain
        {
            ChainPolicy = new X509ChainPolicy()
            {
                RevocationMode = _skipRevocationCheck ? X509RevocationMode.NoCheck : X509RevocationMode.Online,
            }
        };

        bool chainBuilt = chain.Build(certificate);

        foreach (var chainElement in chain.ChainElements)
        {
            chainElement.Certificate.Dispose();
        }

        if (chainBuilt)
        {
            return true;
        }

        _logger.LogError("Certificate validation failed");
        foreach (var chainStatus in chain.ChainStatus)
        {
            _logger.LogError($"Chain error: {chainStatus.Status} -> {chainStatus.StatusInformation}");
        }

        return false;
    }

    private bool IsAllowedHost(HttpContext context)
    {
        string host = context.Request.Host.Host;
        if (_allowedHosts.Contains(host))
        {
            return true;
        }

        return context.Request.Headers.TryGetValue("x-forwarded-for", out var forwardedFor) &&
               _allowedHosts.Contains(forwardedFor);
    }
}