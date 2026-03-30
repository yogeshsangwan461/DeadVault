namespace DeadVault.Core.Models;

public class SemanticVersion : IComparable<SemanticVersion>
{
    public int Major { get; set; }
    public int Minor { get; set; }
    public int Patch { get; set; }

    public SemanticVersion() { }

    public SemanticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public static SemanticVersion Initial => new(1, 0, 0);

    public SemanticVersion BumpPatch() => new(Major, Minor, Patch + 1);
    public SemanticVersion BumpMinor() => new(Major, Minor + 1, 0);
    public SemanticVersion BumpMajor() => new(Major + 1, 0, 0);

    public override string ToString() => $"v{Major}.{Minor}.{Patch}";

    public static SemanticVersion Parse(string version)
    {
        var s = version.TrimStart('v', 'V');
        var parts = s.Split('.');
        return new SemanticVersion(
            parts.Length > 0 ? int.Parse(parts[0]) : 1,
            parts.Length > 1 ? int.Parse(parts[1]) : 0,
            parts.Length > 2 ? int.Parse(parts[2]) : 0);
    }

    public static bool TryParse(string version, out SemanticVersion? result)
    {
        try
        {
            result = Parse(version);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other == null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        return Patch.CompareTo(other.Patch);
    }

    public override bool Equals(object? obj) =>
        obj is SemanticVersion sv && Major == sv.Major && Minor == sv.Minor && Patch == sv.Patch;

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);
}

public enum VersionBumpKind
{
    Patch,
    Minor,
    Major,
}
