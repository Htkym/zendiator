using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private void ApplyStreamValidators()
    {
        foreach (var entry in streamValidators.OrderBy(static entry => entry.Order))
        {
            ct.ThrowIfCancellationRequested();
            var type = entry.Type;
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || type.IsUnboundGenericType ||
                IsUnsupportedValidatorType(type) || !compilation.IsSymbolAccessibleWithin(type, accessContext))
            {
                ErrorAt(21, $"Validator {Name(type)} must be an accessible, non-abstract closed class.", entry.Location);
                continue;
            }
            var contracts = GetContracts(type, streamValidatorDefinition);
            if (contracts.Length == 0)
            {
                ErrorAt(21, $"Validator {Name(type)} must implement IStreamRequestValidator<TRequest>.", entry.Location);
                continue;
            }
            foreach (var contract in contracts)
            {
                var matches = streamRoutes.Where(route => !route.IsOpen && Same(route.Request, contract.TypeArguments[0])).ToArray();
                if (matches.Length == 0)
                    ErrorAt(21, $"Validator {Name(type)} contract {Name(contract)} has no matching closed Stream route.", entry.Location);
                foreach (var route in matches) route.Validators.Add(type);
            }
        }
    }

    private static bool IsUnsupportedValidatorType(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => IsUnsupportedValidatorType(array.ElementType),
        INamedTypeSymbol named => named.IsFileLocal || named.IsUnboundGenericType || named.TypeArguments.Any(IsUnsupportedValidatorType) ||
            (named.ContainingType is { } containing && IsUnsupportedValidatorType(containing)),
        _ => false
    };
}
