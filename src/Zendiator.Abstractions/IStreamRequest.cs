namespace Zendiator;

/// <summary>A streaming request producing zero or more items. One request maps to exactly one stream handler.</summary>
public interface IStreamRequest<TItem>;
