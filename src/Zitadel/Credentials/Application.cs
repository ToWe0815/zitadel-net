﻿using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jose;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace Zitadel.Credentials;

/// <summary>
/// Application for ZITADEL. An application is an OIDC application type
/// that allows a backend (for example for some single page application) api to
/// check if sent credentials from a client are valid or not.
/// </summary>
public record Application
{
    /// <summary>
    /// The application type.
    /// </summary>
    public const string Type = "application";

    /// <summary>
    /// The client id associated with this application.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// The application id.
    /// </summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>
    /// This is unique ID (on ZITADEL) of the key.
    /// </summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// The private key generated by ZITADEL for this <see cref="Application"/>.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Load an <see cref="Application"/> from a file at a given (relative or absolute) path.
    /// </summary>
    /// <param name="pathToJson">The relative or absolute filepath to the json file.</param>
    /// <returns>The parsed <see cref="Application"/>.</returns>
    /// <exception cref="FileNotFoundException">When the file does not exist.</exception>
    /// <exception cref="InvalidDataException">When the deserializer returns 'null'.</exception>
    /// <exception cref="JsonException">
    /// Thrown when the JSON is invalid,
    /// the <see cref="Application"/> type is not compatible with the JSON,
    /// or when there is remaining data in the Stream.
    /// </exception>
    public static async Task<Application> LoadFromJsonFileAsync(string pathToJson)
    {
        var path = Path.GetFullPath(
            Path.IsPathRooted(pathToJson)
                ? pathToJson
                : Path.Join(Directory.GetCurrentDirectory(), pathToJson));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}.", path);
        }

        await using var stream = File.OpenRead(path);
        return await LoadFromJsonStreamAsync(stream);
    }

    /// <inheritdoc cref="LoadFromJsonFileAsync"/>
    public static Application LoadFromJsonFile(string pathToJson) => LoadFromJsonFileAsync(pathToJson).Result;

    /// <summary>
    /// Load an <see cref="Application"/> from a given stream (FileStream, MemoryStream, ...).
    /// </summary>
    /// <param name="stream">The stream to read the json from.</param>
    /// <returns>The parsed <see cref="Application"/>.</returns>
    /// <exception cref="InvalidDataException">When the deserializer returns 'null'.</exception>
    /// <exception cref="JsonException">
    /// Thrown when the JSON is invalid,
    /// the <see cref="Application"/> type is not compatible with the JSON,
    /// or when there is remaining data in the Stream.
    /// </exception>
    public static async Task<Application> LoadFromJsonStreamAsync(Stream stream) =>
        await JsonSerializer.DeserializeAsync<Application>(
            stream,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ??
        throw new InvalidDataException("The json file yielded a 'null' result for deserialization.");

    /// <inheritdoc cref="LoadFromJsonStreamAsync"/>
    public static Application LoadFromJsonStream(Stream stream) => LoadFromJsonStreamAsync(stream).Result;

    /// <summary>
    /// Load an <see cref="Application"/> from a string that contains json.
    /// </summary>
    /// <param name="json">Json string.</param>
    /// <returns>The parsed <see cref="Application"/>.</returns>
    /// <exception cref="InvalidDataException">When the deserializer returns 'null'.</exception>
    /// <exception cref="JsonException">
    /// Thrown when the JSON is invalid,
    /// the <see cref="Application"/> type is not compatible with the JSON,
    /// or when there is remaining data in the Stream.
    /// </exception>
    public static async Task<Application> LoadFromJsonStringAsync(string json)
    {
        await using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(json), 0, json.Length);
        return await LoadFromJsonStreamAsync(memoryStream);
    }

    /// <inheritdoc cref="LoadFromJsonStringAsync"/>
    public static Application LoadFromJsonString(string json) => LoadFromJsonStringAsync(json).Result;

    /// <inheritdoc cref="GetSignedJwtAsync"/>
    public string GetSignedJwt(string issuer, TimeSpan? lifeSpan = null) =>
        GetSignedJwtAsync(issuer, lifeSpan).Result;

    /// <summary>
    /// Create a signed JWT token. It is signed with the RSA private
    /// key loaded from the key file / key content. The signed
    /// JWT contains the required information for ZITADEL to verify
    /// the application.
    /// </summary>
    /// <param name="audience">The audience that is targeted to verify the credentials.</param>
    /// <param name="lifeSpan">The lifetime of the jwt token. Min: 1 second. Max: 1 hour. Defaults to 1 hour.</param>
    /// <returns>A string with a signed JWT token.</returns>
    /// <exception cref="ArgumentException">When the lifeSpan param is not within its bounds.</exception>
    public async Task<string> GetSignedJwtAsync(string audience, TimeSpan? lifeSpan = null)
    {
        using var rsa = new RSACryptoServiceProvider();
        rsa.ImportParameters(await GetRsaParametersAsync());

        if (lifeSpan != null && (lifeSpan < TimeSpan.FromSeconds(1) || lifeSpan > TimeSpan.FromHours(1)))
        {
            throw new ArgumentException("The lifespan is below 1 second or above 1 hour.", nameof(lifeSpan));
        }

        return JWT.Encode(
            new Dictionary<string, object>
            {
                { "iss", ClientId },
                { "sub", ClientId },
                { "iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "exp", (DateTimeOffset.UtcNow + (lifeSpan ?? TimeSpan.FromHours(1))).ToUnixTimeSeconds() },
                { "aud", audience },
            },
            rsa,
            JwsAlgorithm.RS256,
            new Dictionary<string, object>
            {
                { "kid", KeyId },
            });
    }

    private async Task<RSAParameters> GetRsaParametersAsync()
    {
        var bytes = Encoding.UTF8.GetBytes(Key);
        await using var ms = new MemoryStream(bytes);
        using var sr = new StreamReader(ms);
        var pemReader = new PemReader(sr);

        if (pemReader.ReadObject() is not AsymmetricCipherKeyPair keyPair)
        {
            throw new("RSA Keypair could not be read.");
        }

        return DotNetUtilities.ToRSAParameters(keyPair.Private as RsaPrivateCrtKeyParameters);
    }
}
