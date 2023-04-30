using Microsoft.CodeAnalysis;
using System.Security.Principal;
using RogueGen.UnitTests;

namespace RogueGen
{
    public static partial class GlobalMappers
    {
        public static User MapToUser(this UserDto from)
        {
            return new User
            {
                Count = from.Count,
                Age = from.Age,
                Unwanted = from.Unwanted,
                DateOfBirth = from.DateOfBirth,
                Roles = from.Roles.Select(el => (Role)el).ToList(),
                MainRole = (Role)from.MainRole,
                FullName = from.FullName
            };
        }
        public static WindowsIdentity MapToWindowsIdentity(this WindowsUser from) 
        {
#pragma warning disable CA1416 // Validar la compatibilidad de la plataforma
            return new(from.Name);
#pragma warning restore CA1416 // Validar la compatibilidad de la plataforma
        }
        public static UserDto Map(this User from)
        {
            return new UserDto
            {
                Count = from.Count,
                Age = from.Age,
                Unwanted = from.Unwanted,
                DateOfBirth = from.DateOfBirth,
                Roles = from.Roles.Select(el => ((int, string))el).ToArray(),
                MainRole = ((int, string))from.MainRole!,
                FullName = from.FullName
            };
        }
    }
}
//namespace RoslynExample
//{
//    public class SyntaxTreeFinder
//    {
//        public static void FindSyntaxTree(string solutionPath, string projectName, string attributeName)
//        {
//            var workspace = MSBuildWorkspace.Create();
//            var solution = workspace.OpenSolutionAsync(solutionPath).Result;
//            var project = solution.Projects.First(p => p.Name == projectName);
//            var compilation = project.GetCompilationAsync().Result;

//            // Load the external assembly
//            var externalAssembly = Assembly.LoadFrom("path/to/external/assembly.dll");
//            var attributeType = externalAssembly.GetType(attributeName);

//            foreach (var syntaxTree in compilation.SyntaxTrees)
//            {
//                var semanticModel = compilation.GetSemanticModel(syntaxTree);
//                var root = syntaxTree.GetRoot();
//                var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
//                foreach (var @class in classes)
//                {
//                    var classSymbol = semanticModel.GetDeclaredSymbol(@class);
//                    if (classSymbol.GetAttributes().Any(a => a.AttributeClass == attributeType))
//                    {
//                        // Do something with the syntax tree
//                        System.Console.WriteLine(syntaxTree.ToString());
//                    }
//                }
//            }
//        }
//    }
//}