using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DISourceGen
{
    internal static class TypeUtils
    {
        internal static INamedTypeSymbol? FindImplementation(ITypeSymbol typeToFind, Compilation compilation)
            => FindImplementations(typeToFind, compilation).FirstOrDefault();

        internal static IEnumerable<INamedTypeSymbol> FindImplementations(ITypeSymbol typeToFind, Compilation compilation)
        {
            //foreach (var type in GetAllTypes(compilation.GlobalNamespace.GetNamespaceMembers()))
            //{
            //    if (!type.IsAbstract && type.Interfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, typeToFind)))
            //    {
            //        yield return type;
            //    }
            //}

            return GetAllTypes(compilation.GlobalNamespace.GetNamespaceMembers())
                .Where(type =>
                    !type.IsAbstract &&
                    type.Interfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, typeToFind))
                );
        }

        internal static IEnumerable<INamedTypeSymbol> GetAllTypes(IEnumerable<INamespaceSymbol> namespaces)
        {
            foreach (var ns in namespaces)
            {
                foreach (var type in ns.GetTypeMembers())
                {
                    yield return type;
                }


                foreach (var subType in GetAllTypes(ns.GetNamespaceMembers()))
                    yield return subType;
            }
        }
    }
}
