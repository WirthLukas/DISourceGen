using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DISourceGen
{
    // ReSharper disable once InconsistentNaming
    [Generator]
    public class DISourceGenerator : ISourceGenerator
    {
        private const string ServicesNamespace = "DI";
        private const string ServicesTypeName = "Services";
        private const string ServicesResolveMethodName = "Resolve";
        private const string FileName = "DISourceGen.Services.cs";

        private INamedTypeSymbol? _transientAttribute;
        private INamedTypeSymbol? _primaryConstructorAttribute;
        private INamedTypeSymbol? _injectAttribute;

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var compilation = AddOwnTypesToCompilation(context.Compilation);

            context.AddSource("TransientAttribute.cs", Types.TransientAttribute);
            context.AddSource("PrimaryConstructorAttribute.cs", Types.PrimaryConstructorAttribute);

            _transientAttribute = compilation.GetTypeByMetadataName("DI.TransientAttribute");
            _primaryConstructorAttribute = compilation.GetTypeByMetadataName("DI.PrimaryConstructorAttribute");
            _injectAttribute = compilation.GetTypeByMetadataName("DI.InjectAttribute");
            var servicesClass = compilation.GetTypeByMetadataName("DI.Services");

            var services = new Dictionary<string, ServiceData>();

            foreach (var type in GetTypesToResolve(compilation, servicesClass))
            {
                MapToServiceData(type, services, compilation);
            }

            var orderedServices = services.Values
                .OrderBy(service => service.ConstructorArguments.Count)
                .ToList();

            context.AddSource(FileName, GenerateServices(orderedServices));
        }
        
        private static Compilation AddOwnTypesToCompilation(Compilation compilation)
        {
            var options = (compilation as CSharpCompilation)?.SyntaxTrees[0].Options as CSharpParseOptions;

            var tempCompilation = compilation
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(Types.ServicesStub, Encoding.UTF8), options))
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(Types.TransientAttribute, Encoding.UTF8),
                    options))
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(Types.PrimaryConstructorAttribute, Encoding.UTF8),
                    options));

            return tempCompilation;
        }

        private ServiceData MapToServiceData(INamedTypeSymbol type, Dictionary<string, ServiceData> services, Compilation compilation)
        {
            // If there is already a service for this type, we reached the end of the recursion
            if (services.ContainsKey(type.Name))
            {
                return services[type.Name];
            }

            var realType = type.IsAbstract ? TypeUtils.FindImplementation(type, compilation) : type;

            if (realType is null)
            {
                throw new SourceGenException("DIGEN001", "Type not found",
                    $"Could not find an implementation of type '{type}'.", "DI.Services");
            }

            var service = new ServiceData(type)
            {
                IsTransient = type.GetAttributes()
                    .Any(ad => SymbolEqualityComparer.Default.Equals(ad.AttributeClass, _transientAttribute)),
                ImplementationType = realType,
                VariableName = realType.Name
                    .ToLower()
                    .Replace("<", "")
                        .Replace(">", "")
                        .Replace("?", "")
            };
            
            services.Add(type.Name, service);

            if (realType.Constructors.Length is not 0)
            {
                var ctor = realType.Constructors
                    .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                    .FirstOrDefault(c => c.GetAttributes()
                        .Any(ad => SymbolEqualityComparer.Default.Equals(ad.AttributeClass, _primaryConstructorAttribute)
                    )) ?? realType.Constructors[0];

                foreach (var paramType in ctor.Parameters
                    .Select(p => p.Type)
                    .OfType<INamedTypeSymbol>())
                {
                    service.ConstructorArguments.Add(MapToServiceData(paramType, services, compilation));
                }
            }

            return service;
        }

        private static string GenerateServices(IList<ServiceData> services)
        {
            var sourceBuilder = new StringBuilder();
            sourceBuilder.AppendLine(@"
using System;

namespace DI
{
    public static class Services
    {");
            // Field Generation
            var fields = GenerateFields(services, sourceBuilder);

            sourceBuilder.AppendLine(@"
        public static T Resolve<T>()
        {");

            foreach (var service in services)
            {
                if (service != services.Last())
                {
                    sourceBuilder.AppendLine($"if (typeof(T) == typeof({service.BaseType}))");
                    sourceBuilder.AppendLine("{");
                }
                // TODO: Add Parameter Services bei Transient

                sourceBuilder.Append("  return (T)");

                if (service.IsTransient)
                {
                    sourceBuilder.Append($"(object)(new {service.ImplementationType}(");

                    GenerateConstructorCall(service, fields, sourceBuilder);

                    sourceBuilder.Append("))");
                }
                else
                {
                    sourceBuilder.Append(service.VariableName);
                }

                sourceBuilder.AppendLine(";");

                if (service != services.Last())
                {
                    sourceBuilder.AppendLine("}");
                }
            }

            if (services.Count == 0)
            {
                sourceBuilder.AppendLine("throw new System.InvalidOperationException(\"This code is unreachable.\");");
            }

            sourceBuilder.AppendLine(@"
        }
    }
}");
            return sourceBuilder.ToString();
        }

        private static List<ServiceData> GenerateFields(IEnumerable<ServiceData> services, StringBuilder sourceBuilder)
        {
            var currentGeneratedFields = new List<ServiceData>();

            foreach (var service in services)
            {
                if (!service.IsTransient && !currentGeneratedFields.Contains(service))
                {
                    sourceBuilder.Append(
                        $"private static {service.BaseType} {service.VariableName} = new {service.ImplementationType}(");

                    GenerateConstructorCall(service, currentGeneratedFields, sourceBuilder);

                    sourceBuilder.AppendLine(");");
                    currentGeneratedFields.Add(service);
                }
            }

            return currentGeneratedFields;
        }

        private static void GenerateConstructorCall(ServiceData service, List<ServiceData> fields, StringBuilder sourceBuilder)
        {
            bool first = true;

            foreach (var paramService in service.ConstructorArguments)
            {
                if (!paramService.IsTransient)
                {
                    if (!fields.Contains(paramService))
                    {
                        throw new SourceGenException("DIGEN002", "Field not found",
                            $"Can not find field with name '{paramService.VariableName}' for service '{service.ImplementationType}'´s constructor",
                            "DI.Services");
                    }

                    if (!first)
                    {
                        sourceBuilder.Append(", ");
                    }

                    sourceBuilder.Append($"{paramService.VariableName}");
                    first = false;
                }
                else
                {
                    if (!first)
                    {
                        sourceBuilder.Append(", ");
                    }

                    sourceBuilder.Append($"new {paramService.ImplementationType}()");
                    first = false;
                }
            }
        }
        
        private static List<INamedTypeSymbol> GetTypesToResolve(Compilation compilation, INamedTypeSymbol servicesClass)
        {
            var types = new List<INamedTypeSymbol>();

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {        
                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                // Get All "DI.Services.Resolve<...>()"
                var typesToCreate = syntaxTree.GetRoot().DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                    .Select(ie => ModelExtensions.GetSymbolInfo(semanticModel, ie).Symbol as IMethodSymbol)      // Get All Methods Calls
                    .Where(symbol => symbol is not null)
                    .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol!.ContainingType, servicesClass))   // Of Services class
                    .Select(symbol => symbol!.ReturnType as INamedTypeSymbol);

                types.AddRange(typesToCreate!);
            }

            return types;
        }
 
        // Not working now, because the generated Resolve Method call, is not in the compilation context
        // Therefore the generic parameter is not recognized as a Service
        // if adding this feature add, Inject attribute to the compilation SyntaxTrees and the context sources!!!
        /*private string GenerateInjectingClasses(Compilation compilation)
        {
            var injectingClasses = TypeUtils.GetAllTypes(compilation.GlobalNamespace.GetNamespaceMembers())
                .Where(typeSymbol => typeSymbol.IsReferenceType)
                .Where(typeSymbol => typeSymbol.GetAttributes().Any(ad => SymbolEqualityComparer.Default.Equals(ad.AttributeClass, _injectAttribute)))
                .Select(typeSymbol => new
                {
                    TypeSymbol = typeSymbol,
                    Injections = typeSymbol.GetAttributes()
                        .Where(ad => SymbolEqualityComparer.Default.Equals(ad.AttributeClass, _injectAttribute))
                        .ToList()
                });

            var sourceBuilder = new StringBuilder();

            foreach (var type in injectingClasses)
            {
                sourceBuilder.AppendLine($@"
namespace {type.TypeSymbol.ContainingNamespace.ToDisplayString()}
{{
    {type.TypeSymbol.DeclaredAccessibility.ToString().ToLower()} partial class {type.TypeSymbol.Name}
    {{");

                foreach (var injection in type.Injections)
                {
                    var injectionType = injection.NamedArguments.Single(kvp => kvp.Key is "FullTypePath").Value.Value as string;
                    var propertyName = injection.NamedArguments.SingleOrDefault(kvp => kvp.Key is "PropertyName").Value.Value as string ?? injectionType.Split('.').Last();

                    sourceBuilder.AppendLine($@"
        public {injectionType} {propertyName} {{ get; }} = DI.Services.Resolve<{injectionType}>();");
                }

                sourceBuilder.AppendLine(@"
    }
}");
            }

            return sourceBuilder.ToString();
        }*/
    }
}
