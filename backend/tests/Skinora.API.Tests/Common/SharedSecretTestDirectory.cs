using System;
using System.Collections.Generic;
using System.IO;
using Skinora.API.Configuration;

namespace Skinora.API.Tests.Common;

/// <summary>
/// T171a — a temporary directory standing in for <c>/run/secrets</c>. Outside
/// Development the backend reads its shared secrets only from files there
/// (<see cref="SharedSecretFiles"/>), so a host started as Production needs one.
/// </summary>
public sealed class SharedSecretTestDirectory : IDisposable
{
    /// <summary>Distinct ≥32-character values for every required secret.</summary>
    public static IReadOnlyDictionary<string, string> RequiredValues { get; } = new Dictionary<string, string>
    {
        ["jwt_secret"] = "t171a-test-jwt-secret-0123456789abcdef-0123",
        ["blockchain_webhook_secret"] = "t171a-test-tron-webhook-secret-0123456789ab",
        ["steam_sidecar_internal_key"] = "t171a-test-steam-internal-key-0123456789abc",
        ["blockchain_sidecar_internal_key"] = "t171a-test-tron-internal-key-0123456789abcd",
    };

    public SharedSecretTestDirectory(bool withRequired = true)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skinora-t171a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        if (withRequired)
        {
            foreach (var (name, value) in RequiredValues) Write(name, value + "\n");
        }
    }

    public string Path { get; }

    public string FilePath(string name) => System.IO.Path.Combine(Path, name);

    public void Write(string name, string content) => File.WriteAllText(FilePath(name), content);

    public void Delete(string name) => File.Delete(FilePath(name));

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
