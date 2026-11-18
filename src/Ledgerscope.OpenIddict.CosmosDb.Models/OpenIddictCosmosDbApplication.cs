/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Collections.Immutable;
using System.Diagnostics;

namespace Ledgerscope.OpenIddict.CosmosDb.Models;

/// <summary>
/// Represents an OpenIddict application.
/// </summary>
[DebuggerDisplay("Id = {Id,nq} ; ClientId = {ClientId,nq} ; ClientType = {ClientType,nq}")]
public class OpenIddictCosmosDbApplication
{
    /// <summary>
    /// Gets or sets the application type of the application.
    /// </summary>
    [JsonPropertyName("applicationType")]
    public virtual string? ApplicationType { get; set; }

    /// <summary>
    /// Gets or sets the client identifier of the application.
    /// </summary>
    [JsonPropertyName("clientId")]
    public virtual string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret of the application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note: depending on the application manager used to create this instance,
    /// this property may be hashed or encrypted for security reasons.
    /// </para>
    /// <para>
    /// Note: client authentication based on shared secrets is not recommended and should
    /// only be used for backward compatibility with legacy applications that only support
    /// client secrets. When possible, consider using public/private key pairs or TLS client
    /// certificates instead, as these client authentication methods are significantly safer.
    /// </para>
    /// </remarks>
    [JsonPropertyName("clientSecret")]
    public virtual string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the client type of the application.
    /// </summary>
    [JsonPropertyName("clientType")]
    public virtual string? ClientType { get; set; }

    /// <summary>
    /// Gets or sets the concurrency token of the application.
    /// </summary>
    /// <remarks>
    /// Note: this property is mapped to the "_etag" system property managed by Azure Cosmos DB.
    /// </remarks>
    [JsonPropertyName("_etag")]
    public virtual string? ConcurrencyToken { get; set; }

    /// <summary>
    /// Gets or sets the consent type of the application.
    /// </summary>
    [JsonPropertyName("consentType")]
    public virtual string? ConsentType { get; set; }

    /// <summary>
    /// Gets or sets the display name of the application.
    /// </summary>
    [JsonPropertyName("displayName")]
    public virtual string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the localized display names of the application.
    /// </summary>
    [JsonPropertyName("displayNames")]
    public virtual ImmutableDictionary<string, string>? DisplayNames { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the application.
    /// </summary>
    [JsonPropertyName("id")]
    public virtual string? Id { get; set; }

    /// <summary>
    /// Gets or sets the JSON Web Key Set of the application.
    /// </summary>
    [JsonPropertyName("jsonWebKeySet")]
    public virtual JsonElement? JsonWebKeySet { get; set; }

    /// <summary>
    /// Gets or sets the permissions of the application.
    /// </summary>
    [JsonPropertyName("permissions")]
    public virtual ImmutableArray<string>? Permissions { get; set; }

    /// <summary>
    /// Gets or sets the post-logout redirect URIs of the application.
    /// </summary>
    [JsonPropertyName("postLogoutRedirectUris")]
    public virtual ImmutableArray<string>? PostLogoutRedirectUris { get; set; }

    /// <summary>
    /// Gets or sets the additional properties of the application.
    /// </summary>
    [JsonPropertyName("properties")]
    public virtual JsonElement? Properties { get; set; }

    /// <summary>
    /// Gets or sets the redirect URIs of the application.
    /// </summary>
    [JsonPropertyName("redirectUris")]
    public virtual ImmutableArray<string>? RedirectUris { get; set; }

    /// <summary>
    /// Gets or sets the requirements of the application.
    /// </summary>
    [JsonPropertyName("requirements")]
    public virtual ImmutableArray<string>? Requirements { get; set; }

    /// <summary>
    /// Gets or sets the settings of the application.
    /// </summary>
    [JsonPropertyName("settings")]
    public virtual ImmutableDictionary<string, string>? Settings { get; set; }
}
