namespace Zendiator;

/// <summary>Describes the versioned, compile-time Handler export format of an assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ZendiatorGeneratedCatalogAttribute(int major, int minor, string producerIdentity, string fingerprintAlgorithm) : Attribute
{
    /// <summary>Gets the incompatible format version.</summary>
    public int Major { get; } = major;
    /// <summary>Gets the compatible format revision.</summary>
    public int Minor { get; } = minor;
    /// <summary>Gets the complete producer assembly identity.</summary>
    public string ProducerIdentity { get; } = producerIdentity;
    /// <summary>Gets the signature fingerprint algorithm identifier.</summary>
    public string FingerprintAlgorithm { get; } = fingerprintAlgorithm;
    /// <summary>Gets or sets required reader features. Version 1 defines none.</summary>
    public string[] RequiredFeatures { get; set; } = [];
}

/// <summary>Publishes a public Handler definition and its compile-time contract fingerprint.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ZendiatorHandlerExportAttribute(Type handler, string fingerprint) : Attribute
{
    /// <summary>Gets the exported Handler definition.</summary>
    public Type Handler { get; } = handler;
    /// <summary>Gets the versioned contract fingerprint.</summary>
    public string Fingerprint { get; } = fingerprint;
}
