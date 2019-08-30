﻿using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using System.IO;
using System.Linq;

//https://mhut.ch/journal/2015/06/30/build-time-code-generation-in-msbuild

namespace BlazorMobile.Build.Core.NativeBindings
{
    public static class BindingClassGenerator
    {
        private const string ProxyInterfaceAttributeUsing = "BlazorMobile.Common.Attributes";

        private const string AttributeToSearch = "ProxyInterface";

        private const string AttributeToSearchFull = ProxyInterfaceAttributeUsing + "." + AttributeToSearch;

        private const string BlazorMobileProxyNamespace = "BlazorMobile.Proxy";

        private const string BlazorMobileInteropNamespace = BlazorMobileProxyNamespace + ".Interop";

        private const string BlazorMobileProxyClass = "global::" + BlazorMobileInteropNamespace + ".Abstract.BlazorMobileProxyClass";

        private const string MethodNotSupported = "BlazorMobile.Proxy.Resource.NonAsyncMethodNotSupported";

        private const string MethodDispatcherFullQualifiedPath = "global::BlazorMobile.Common.Services.MethodDispatcher";

        private const string GetCurrentMethod = "global::System.Reflection.MethodBase.GetCurrentMethod()";

        private const string EditorBrowsableStateNever = "[EditorBrowsable(EditorBrowsableState.Never)]";

        private const string ObsoleteMessage = "[Obsolete(BlazorMobile.Proxy.Resource.ObsoleteMessage, true)]";

        private const string AutoGeneratedCodeMessage = @"//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by BlazorMobile.Build
//     Version:4.0.30319.296
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
";

        private static string OpenMyInterfaceFile(string sourceFile)
        {
            return new StreamReader(sourceFile).ReadToEnd();
        }

        /// <summary>
        /// Return true if the method is async, and a resultType if the asyn method expect a result
        /// </summary>
        /// <param name="method"></param>
        /// <param name="resultType"></param>
        /// <returns></returns>
        private static bool IsAsyncMethod(MethodDeclarationSyntax method, out string[] resultType)
        {
            bool isAsync = false;
            resultType = null;

            if (method.ReturnType is IdentifierNameSyntax)
            {
                //If returning Task
                if (((IdentifierNameSyntax)method.ReturnType).Identifier.ToString().Equals("Task", StringComparison.InvariantCultureIgnoreCase))
                {
                    isAsync = true;
                }
            }
            else if (method.ReturnType is GenericNameSyntax)
            {
                //If returning Task<>
                if (((GenericNameSyntax)method.ReturnType).Kind() == SyntaxKind.GenericName
                    && ((GenericNameSyntax)method.ReturnType).Identifier.ToString().Equals("Task", StringComparison.InvariantCultureIgnoreCase))
                {
                    isAsync = true;

                    resultType = ((GenericNameSyntax)method.ReturnType).TypeArgumentList.Arguments.Select(p => p.ToString()).ToArray();
                }
            }

            return isAsync;
        }

        private static bool HasGenericParameter(MethodDeclarationSyntax method, out string[] resultType)
        {
            resultType = null;
            bool hasGenericParameters = false;

            if (method.TypeParameterList == null)
            {
                hasGenericParameters = false;
            }
            else
            {
                hasGenericParameters = true;
                resultType = method.TypeParameterList.Parameters.Select(p => p.ToString()).ToArray();
            }

            return hasGenericParameters;
        }

        private static bool GetParametersWithoutType(MethodDeclarationSyntax method, out string[] resultType)
        {
            resultType = null;
            bool hasParameters = false;

            if (method.ParameterList.Parameters.Count <= 0)
            {
                hasParameters = false;
            }
            else
            {
                hasParameters = true;
                resultType = method.ParameterList.Parameters.Select(p => p.Identifier.ToString()).ToArray();
            }

            return hasParameters;
        }

        private static List<string> GetAllNamespaces(CompilationUnitSyntax root)
        {
            List<string> namespaces = new List<string>();

            foreach (var member in root.Members)
            {
                if (!(member is NamespaceDeclarationSyntax
                    && ((NamespaceDeclarationSyntax)member).Name.Kind() == SyntaxKind.QualifiedName))
                    continue;

                NamespaceDeclarationSyntax currentNamespace = (NamespaceDeclarationSyntax)member;
                namespaces.Add(currentNamespace.Name.ToString());
            }

            return namespaces;
        }

        private static string WriteOutputfile(StringBuilder resultString, string outputDir, string filename)
        {
            string output = Path.Combine(outputDir, filename);

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            File.WriteAllText(output, resultString.ToString());

            return output;
        }

        private static void GenerateProxyClass(StringBuilder sb, IEnumerable<MemberDeclarationSyntax> members, string currentNamespaceName, bool hasProxyInterfaceAttributeUsing)
        {
            //Search for interfaces
            foreach (var interfaceMember in members)
            {
                if (!IsProxyInterface(interfaceMember, hasProxyInterfaceAttributeUsing))
                    continue;

                InterfaceDeclarationSyntax currentInterface = (InterfaceDeclarationSyntax)interfaceMember;
                string currentInterfaceName = currentInterface.Identifier.ToString();

                //If currentNamespaceName is Empty, that mean the interface is in the global namespace
                string interfaceNamespace = string.IsNullOrEmpty(currentNamespaceName) ? $"global::{currentInterfaceName}" : $"global::{currentNamespaceName}.{currentInterfaceName}";

                sb.AppendLine($"\t{EditorBrowsableStateNever}");
                sb.AppendLine($"\t{ObsoleteMessage}");
                sb.AppendLine($"\tpublic class {currentInterfaceName}Proxy : {BlazorMobileProxyClass}, {interfaceNamespace}");
                sb.AppendLine("\t{");

                //Search methods
                foreach (var methodMember in currentInterface.Members)
                {
                    if (!(methodMember is MethodDeclarationSyntax))
                        continue;

                    MethodDeclarationSyntax methodDeclaration = (MethodDeclarationSyntax)methodMember;
                    string methodSignature = methodDeclaration.ToString();

                    sb.AppendLine($"\t\t{EditorBrowsableStateNever}");
                    sb.AppendLine($"\t\t{ObsoleteMessage}");
                    sb.AppendLine($"\t\tpublic {methodSignature.TrimEnd(';')}");
                    sb.AppendLine("\t\t{");

                    bool methodIsAsync = IsAsyncMethod(methodDeclaration, out string[] baseType);

                    bool methodHasGenericParameters = HasGenericParameter(methodDeclaration, out string[] genericParameters);

                    bool methodHasParameters = GetParametersWithoutType(methodDeclaration, out string[] methodParameters);

                    if (methodIsAsync)
                    {
                        sb.AppendLine("\t\t\ttry {");

                        sb.Append($"\t\t\t\treturn {MethodDispatcherFullQualifiedPath}.");
                        if (baseType == null)
                        {
                            //This should return void (so a simple Task)
                            sb.Append($"CallVoidMethodAsync({GetCurrentMethod}");
                        }
                        else
                        {
                            //This should return a type value (Task<bool> etc.)
                            sb.Append($"CallMethodAsync<{string.Join(", ", baseType)}>({GetCurrentMethod}");
                        }

                        if (methodHasGenericParameters || methodHasParameters)
                        {
                            sb.Append(", ");

                            if (methodHasGenericParameters)
                            {
                                sb.Append("new Type[] { ");

                                sb.Append(string.Join(", ", genericParameters.Select(p => $"typeof({p})")));

                                sb.Append(" }, ");
                            }

                            sb.Append("new object[] { ");

                            if (methodHasParameters)
                            {
                                sb.Append(string.Join(", ", methodParameters));
                            }

                            sb.Append(" }");
                        }

                        sb.Append(");");

                        sb.AppendLine(string.Empty);
                        sb.AppendLine("\t\t\t} catch (Exception) { throw; }");
                    }
                    else
                    {

                        //Non async method will be deprecated in BlazorMobile 3.0.9
                        //Should write the const value in the BlazorMobile.Web assembly, and not copy/pasting it from generator.
                        //This should be a refernce
                        sb.AppendLine($"\t\t\tthrow new NotImplementedException({MethodNotSupported});");
                    }

                    sb.AppendLine("\t\t}");
                }

                sb.AppendLine("\t}");
            }
        }

        private static bool IsProxyInterface(MemberDeclarationSyntax member, bool hasProxyInterfaceAttributeUsing)
        {
            return member is InterfaceDeclarationSyntax

               && ((InterfaceDeclarationSyntax)member).Modifiers.Any(p => p.Kind() == SyntaxKind.PublicKeyword)
               && ((InterfaceDeclarationSyntax)member).AttributeLists
               .Any(p => p.Attributes.ToString().Equals(AttributeToSearch, StringComparison.OrdinalIgnoreCase) && hasProxyInterfaceAttributeUsing
               || p.Attributes.ToString().Equals(AttributeToSearchFull, StringComparison.OrdinalIgnoreCase));
        }

        public static bool HasProxyInterfaces(string sourceFile)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(OpenMyInterfaceFile(sourceFile));
            var root = (CompilationUnitSyntax)tree.GetRoot();
            List<string> allUsings = root.Usings.Select(p => p.Name.ToString()).ToList();

            bool hasProxyInterfaceAttributeUsing = allUsings.Contains(ProxyInterfaceAttributeUsing);

            foreach (var member in root.Members)
            {
                //Interface with namespace
                if (member is NamespaceDeclarationSyntax
                    && ((NamespaceDeclarationSyntax)member).Name.Kind() == SyntaxKind.QualifiedName)
                {
                    NamespaceDeclarationSyntax currentNamespace = (NamespaceDeclarationSyntax)member;

                    foreach (var interfaceMember in currentNamespace.Members)
                    {
                        if (IsProxyInterface(interfaceMember, hasProxyInterfaceAttributeUsing))
                            return true;
                    }
                }
                else if (IsProxyInterface(member, hasProxyInterfaceAttributeUsing))
                {
                    return true;
                }
            }

            return false;
        }

        public static string GenerateBindingClass(string sourceFile, string outputDir)
        {
            string filename = Path.GetFileName(sourceFile);

            StringBuilder sb = new StringBuilder();

            //Write auto-generated code message
            sb.AppendLine(AutoGeneratedCodeMessage);

            SyntaxTree tree = CSharpSyntaxTree.ParseText(OpenMyInterfaceFile(sourceFile));

            var root = (CompilationUnitSyntax)tree.GetRoot();

            List<string> allUsings = root.Usings.Select(p => p.Name.ToString()).ToList();
            allUsings.AddRange(GetAllNamespaces(root));
            allUsings.Add("BlazorMobile.Common.Services");
            allUsings.Add("System.ComponentModel");

            allUsings.ForEach(p => sb.AppendLine($"using {p};"));
            sb.AppendLine(string.Empty);

            bool hasProxyInterfaceAttributeUsing = allUsings.Contains(ProxyInterfaceAttributeUsing);

            foreach (var member in root.Members)
            {
                //Interface with namespace
                if (member is NamespaceDeclarationSyntax
                    && ((NamespaceDeclarationSyntax)member).Name.Kind() == SyntaxKind.QualifiedName)
                {
                    NamespaceDeclarationSyntax currentNamespace = (NamespaceDeclarationSyntax)member;

                    string currentNamespaceName = currentNamespace.Name.ToString();

                    sb.AppendLine($"namespace {currentNamespaceName}.ProxyGenerated");
                    sb.AppendLine("{");

                    GenerateProxyClass(sb, currentNamespace.Members, currentNamespaceName, hasProxyInterfaceAttributeUsing);

                    sb.AppendLine("}");
                }
                else if (IsProxyInterface(member, hasProxyInterfaceAttributeUsing))
                {
                    //Specific case for Interface without specific namespaces
                    sb.AppendLine($"namespace {BlazorMobileProxyNamespace}.ProxyGenerated");
                    sb.AppendLine("{");

                    GenerateProxyClass(sb, new List<MemberDeclarationSyntax>() { member }, string.Empty, hasProxyInterfaceAttributeUsing);

                    sb.AppendLine("}");
                }

                sb.AppendLine();
            }

            return WriteOutputfile(sb, outputDir, filename);
        }
    }
}
