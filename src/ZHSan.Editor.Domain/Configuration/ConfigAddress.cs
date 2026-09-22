namespace ZHSan.Editor.Domain.Configuration;

public readonly struct ConfigAddress : IEquatable<ConfigAddress>
{
    public ConfigAddress(ConfigScope scope, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Scope = scope;
        Key = key;
    }

    public ConfigScope Scope { get; }
    public string Key { get; }

    public bool Equals(ConfigAddress other) =>
        Scope == other.Scope &&
        string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is ConfigAddress other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        Scope,
        StringComparer.OrdinalIgnoreCase.GetHashCode(Key ?? string.Empty));

    public override string ToString() => $"{Scope}:{Key ?? string.Empty}";

    public static bool operator ==(ConfigAddress left, ConfigAddress right) => left.Equals(right);

    public static bool operator !=(ConfigAddress left, ConfigAddress right) => !left.Equals(right);
}
