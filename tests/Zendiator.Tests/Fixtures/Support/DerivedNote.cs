using global::Zendiator;

namespace Zendiator.Tests;

public record DerivedNote(int Id, string Extra) : BaseNote(Id);
