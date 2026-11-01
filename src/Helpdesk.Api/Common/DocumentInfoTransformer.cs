using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Helpdesk.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Helpdesk",
            Version = "1.0",
            Description = """
                A multi-tenant help desk: organizations sign up, invite their team and answer their customers' tickets.
                Every organization's data is kept apart twice, by the application and by PostgreSQL row-level security.

                **Getting started**

                1. Create an organization at `POST /api/auth/signup`; the response holds an access token.
                2. Paste the token into *Authentication*. Every request works inside the organization in the token.
                3. Belong to several organizations? `POST /api/auth/switch` gives a token for another one.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`
                or `plan_limit_reached`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/multi-tenant-saas/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
