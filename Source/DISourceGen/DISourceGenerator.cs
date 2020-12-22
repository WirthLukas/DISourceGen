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
        private const string FileName = "DISourceGen.Services.cs";

        private INamedTypeSymbol? _transientAttribute;
        private INamedTypeSymbol? _primaryConstructorAttribute;

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                string servicesClassSourceCode = BuildServicesClass(context);

                // Adding source code that should be compiled and added to the .dll result
                context.AddSource("TransientAttribute.cs", Types.TransientAttribute);
                context.AddSource("PrimaryConstructorAttribute.cs", Types.PrimaryConstructorAttribute);
                context.AddSource(FileName, servicesClassSourceCode);
            }
            catch (SourceGenException ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(ex.Id, ex.Title, ex.MessageFormat, ex.Category, DiagnosticSeverity.Error, true),
                        Location.Create(FileName, new TextSpan(), new LinePositionSpan())                  
                    ));
            }
        }

        private string BuildServicesClass(GeneratorExecutionContext context)
        {
            var compilation = AddOwnTypesToCompilation(context.Compilation);

            _transientAttribute = compilation.GetTypeByMetadataName("DI.TransientAttribute")
                ?? throw new SourceGenException("INTER001", "Type not found",
                    $"Could not find 'DI.TransientAttribute' after adding it to the compilation.", "DISourceGenerator.cs");

            _primaryConstructorAttribute = compilation.GetTypeByMetadataName("DI.PrimaryConstructorAttribute")
                ?? throw new SourceGenException("INTER001", "Type not found",
                    $"Could not find 'DI.PrimaryConstructorAttribute' after adding it to the compilation.", "DISourceGenerator.cs");

            var servicesClass = compilation.GetTypeByMetadataName("DI.Services")
                ?? throw new SourceGenException("INTER001", "Type not found",
                    $"Could not find 'DI.Services' after adding it to the compilation.", "DISourceGenerator.cs");

            // all services that are needed in the program
            // Key: Service Class name
            // value: service data 
            var services = new Dictionary<string, ServiceData>();

            // Get all type paramter types of all Services.Resolve<...>() calls
            foreach (var type in GetTypesToResolve(compilation, servicesClass))
            {
                // Get Information for that service types 
                MapToServiceData(type, services, compilation);
            }

            // order services by their constructor argument number
            // in order to avoid dependency issues, because needed field
            // could be generated after the current service if the list is not sorted
            var orderedServices = services.Values
                .OrderBy(service => service.ConstructorArguments.Count)
                .ToList();

            return GenerateServices(orderedServices);
        }

        /// <summary>
        /// Adds custom Types (Services.cs, Attributes) to the compilation
        /// </summary>
        /// <param name="compilation">compilation, where the types should be added</param>
        /// <returns>new Compilation with types added</returns>
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

        /// <summary>
        /// Returns all types, that are used as a type paramter in Services.Resolve<...> calls
        /// (Duplicates possible)
        /// </summary>
        /// <param name="compilation">current compilation data</param>
        /// <param name="servicesClass">Symbol for the services Class from the compilation</param>
        /// <returns>List of service types (as Symbols)</returns>
        private static IEnumerable<INamedTypeSymbol> GetTypesToResolve(Compilation compilation, INamedTypeSymbol servicesClass)
        {
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                // Get All "DI.Services.Resolve<...>()"
                var typesToCreate = syntaxTree.GetRoot().DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                    .Select(ie => ModelExtensions.GetSymbolInfo(semanticModel, ie).Symbol as IMethodSymbol)      // Get All Methods Calls
                    .Where(symbol => symbol is not null)     // exlude all non Method Symbols ('cause as returns null if it can not be casted)
                    .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol!.ContainingType, servicesClass))   // Of Services class
                    .Select(symbol => symbol!.ReturnType as INamedTypeSymbol);

                foreach (var type in typesToCreate)
                {
                    yield return type!;
                }
            }

        }

        /// <summary>
        /// Collects recursivly the data (constructor arguments, implementaion, ...) for the given service type
        /// 
        /// if there is already data for this type in the services dictionary, then this data will be returned.
        /// </summary>
        /// <param name="type">type, where data should be collected</param>
        /// <param name="services">Already created Services</param>
        /// <param name="compilation"></param>
        /// <returns>service data from the given type</returns>
        private ServiceData MapToServiceData(INamedTypeSymbol type, Dictionary<string, ServiceData> services, Compilation compilation)
        {
            // If there is already a service for this type, we reached the end of the recursion
            if (services.ContainsKey(type.Name))
            {
                return services[type.Name];
            }

            // Is the type already a implementation?
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

            // Collect Contructor arguments
            if (realType.Constructors.Length is not 0)
            {
                // first public contructor in a class or a constructor with the [PrimaryConstructor] attribute
                var ctor = realType.Constructors
                    .Where(c => c.DeclaredAccessibility == Accessibility.Public)        // public contructors
                    .FirstOrDefault(c => c.GetAttributes()
                        .Any(ad => SymbolEqualityComparer.Default.Equals(ad.AttributeClass, _primaryConstructorAttribute)
                    )) ?? realType.Constructors[0];

                foreach (var paramType in ctor.Parameters
                    .Select(p => p.Type)
                    .OfType<INamedTypeSymbol>())
                {
                    // Get the ServiceData from the parameter
                    service.ConstructorArguments.Add(MapToServiceData(paramType, services, compilation));
                }
            }

            return service;
        }

        /// <summary>
        /// Creates source code for the Services class depending on the given services data
        /// </summary>
        /// <param name="services">data of the services that sould be supported</param>
        /// <returns>source code for the Services class</returns>
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

            // Method Resolve<T>
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
