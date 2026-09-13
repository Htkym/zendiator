using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Zendiator.SourceGenerator;

/// <summary>An immutable sequence whose equality compares elements, not backing storage.</summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _items;
    public EquatableArray(IEnumerable<T> items) => _items = items.ToImmutableArray();
    public int Count => _items.IsDefault ? 0 : _items.Length;
    public T this[int index] => _items[index];
    public bool Equals(EquatableArray<T> other)
    {
        if (Count != other.Count) return false;
        for (var i = 0; i < Count; i++)
            if (!EqualityComparer<T>.Default.Equals(this[i], other[i])) return false;
        return true;
    }
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);
    public override int GetHashCode()
    {
        var hash = 0;
        for (var i = 0; i < Count; i++)
            hash = unchecked(hash * 31 + (this[i] is null ? 0 : EqualityComparer<T>.Default.GetHashCode(this[i])));
        return hash;
    }
    public ImmutableArray<T>.Enumerator GetEnumerator() => (_items.IsDefault ? ImmutableArray<T>.Empty : _items).GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)(_items.IsDefault ? ImmutableArray<T>.Empty : _items)).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();
}
