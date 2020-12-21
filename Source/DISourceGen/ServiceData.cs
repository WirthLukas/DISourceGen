using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DISourceGen
{
    internal class ServiceData
    {
        public INamedTypeSymbol BaseType { get; set; }
        // TODO: Remove Nullable Type
        public INamedTypeSymbol? ImplementationType { get; set; } = null;

        public List<ServiceData> ConstructorArguments { get; } = new ();
        public bool IsTransient { get; set; }
        public string? VariableName { get; set; } = null;
        
        public ServiceData(INamedTypeSymbol baseType)
        {
            BaseType = baseType;
        }
    }
}
