namespace Zendiator;

/// <summary>Describes the versioned, compile-time Handler export format of an assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ZendiatorGeneratedCatalogAttribute(int major, int minor, string producerIdentity, string fingerprintAlgorithm) : Attribute
{
    /// <summary>Gets the incompatible format version.</summary>
    public int Major { get; } = major;
    /// <summary>Gets the compatible format revision.</summary>
    public int Minor { get; } = minor;
    public string ProducerIdentity { get; } = producerIdentity;
    public string FingerprintAlgorithm { get; } = fingerprintAlgorithm;
    /// <summary>Gets or sets required reader features. Version 1 defines none.</summary>
    public string[] RequiredFeatures { get; set; } = [];
}

/// <summary>Publishes a public Handler definition and its compile-time contract fingerprint.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ZendiatorHandlerExportAttribute(Type handler, string fingerprint) : Attribute
{
    public Type Handler { get; } = handler;
    public string Fingerprint { get; } = fingerprint;
}
