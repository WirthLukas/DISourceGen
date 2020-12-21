using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DISourceGen.ConsoleTests
{
    class Program
    {
        static void Main(string[] args)
        {
            string source = @"
namespace Foo
{
    class C
    {
        void M()
        {
            DI.Services.Resolve<IFoo>();
            DI.Services.Resolve<IBar>();
        }
    }

    [DI.Transient]
    interface IFoo { }

    class Foo : IFoo {}

    interface IBar { }

    class Bar : IBar
    {
        Bar(IFoo foo)
        {

        }
    }
}
";

            var (diagnostics, output) = GetGeneratedOutput(source);

            if (diagnostics.Length > 0)
            {
                Console.WriteLine("Diagnostics:");
                foreach (var diag in diagnostics)
                {
                    Console.WriteLine("   " + diag.ToString());
                }
                Console.WriteLine();
                Console.WriteLine("Output:");
            }

            Console.WriteLine(output);
        }

        private static (ImmutableArray<Diagnostic>, string) GetGeneratedOutput(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);

            var references = new List<MetadataReference>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                if (!assembly.IsDynamic)
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            var compilation = CSharpCompilation.Create(
                "foo",
                new SyntaxTree[] {syntaxTree},
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            // TODO: Uncomment these lines if you want to return immediately if the injected program isn't valid _before_ running generators
            //
            // ImmutableArray<Diagnostic> compilationDiagnostics = compilation.GetDiagnostics();
            //
            // if (diagnostics.Any())
            // {
            //     return (diagnostics, "");
            // }

            ISourceGenerator generator = new DISourceGenerator();

            var driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var generateDiagnostics);

            return (generateDiagnostics, outputCompilation.SyntaxTrees.Last().ToString());
        }
    }
}
