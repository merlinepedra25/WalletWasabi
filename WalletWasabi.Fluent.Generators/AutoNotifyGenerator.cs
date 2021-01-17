using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace WalletWasabi.Fluent.Generators
{
	[Generator]
	public class AutoNotifyGenerator : ISourceGenerator
	{
		private const string AttributeText = @"// <auto-generated />
#nullable enable
using System;

namespace WalletWasabi.Fluent
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    sealed class AutoNotifyAttribute : Attribute
    {
        public AutoNotifyAttribute()
        {
        }

        public string? PropertyName { get; set; }

		public AccessModifier SetterModifier { get; set; } = AccessModifier.Public;
    }
}";

		private const string ModifierText = @"// <auto-generated />

namespace WalletWasabi.Fluent
{
    public enum AccessModifier
    {
        None = 0,
        Public = 1,
        Protected = 2,
        Private = 3,
        Internal = 4
    }
}";
		public void Initialize(GeneratorInitializationContext context)
		{
			// System.Diagnostics.Debugger.Launch();
			context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
		}

		public void Execute(GeneratorExecutionContext context)
		{
			context.AddSource("AccessModifier", SourceText.From(ModifierText, Encoding.UTF8));
			context.AddSource("AutoNotifyAttribute", SourceText.From(AttributeText, Encoding.UTF8));

			if (context.SyntaxReceiver is not SyntaxReceiver receiver)
			{
				return;
			}

			var options = (context.Compilation as CSharpCompilation)?.SyntaxTrees[0].Options as CSharpParseOptions;
			var compilation = context.Compilation.AddSyntaxTrees(
				CSharpSyntaxTree.ParseText(SourceText.From(ModifierText, Encoding.UTF8), options),
				CSharpSyntaxTree.ParseText(SourceText.From(AttributeText, Encoding.UTF8), options));

			var attributeSymbol = compilation.GetTypeByMetadataName("WalletWasabi.Fluent.AutoNotifyAttribute");
			if (attributeSymbol is null)
			{
				return;
			}

			var notifySymbol = compilation.GetTypeByMetadataName("ReactiveUI.ReactiveObject");
			if (notifySymbol is null)
			{
				return;
			}

			List<IFieldSymbol> fieldSymbols = new ();

			foreach (FieldDeclarationSyntax field in receiver.CandidateFields)
			{
				var semanticModel = compilation.GetSemanticModel(field.SyntaxTree);

				foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
				{
					var fieldSymbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
					if (fieldSymbol is null)
					{
						continue;
					}

					var attributes = fieldSymbol.GetAttributes();
					if (attributes.Any(ad => ad?.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) ?? false))
					{
						fieldSymbols.Add(fieldSymbol);
					}
				}
			}

			// TODO: https://github.com/dotnet/roslyn/issues/49385
#pragma warning disable RS1024
			var groupedFields = fieldSymbols.GroupBy(f => f.ContainingType);
#pragma warning restore RS1024

			foreach (var group in groupedFields)
			{
				var classSource = ProcessClass(group.Key, group.ToList(), attributeSymbol, notifySymbol);
				if (classSource is null)
				{
					continue;
				}
				context.AddSource($"{group.Key.Name}_AutoNotify.cs", SourceText.From(classSource, Encoding.UTF8));
			}
		}

		private string? ProcessClass(INamedTypeSymbol classSymbol, List<IFieldSymbol> fields, ISymbol attributeSymbol, INamedTypeSymbol notifySymbol)
		{
			if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
			{
				return null;
			}

			string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

			var addNotifyInterface = !classSymbol.Interfaces.Contains(notifySymbol);
			var baseType = classSymbol.BaseType;
			while (true)
			{
				if (baseType is null)
				{
					break;
				}

				if (SymbolEqualityComparer.Default.Equals(baseType, notifySymbol))
				{
					addNotifyInterface = false;
					break;
				}

				baseType = baseType.BaseType;
			}

			var source = new StringBuilder();

            var format = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints | SymbolDisplayGenericsOptions.IncludeVariance
            );

			if (addNotifyInterface)
			{
				source.Append($@"// <auto-generated />
#nullable enable
using ReactiveUI;

namespace {namespaceName}
{{
    public partial class {classSymbol.ToDisplayString(format)} : {notifySymbol.ToDisplayString()}
    {{");
			}
			else
			{
				source.Append($@"// <auto-generated />
#nullable enable
using ReactiveUI;

namespace {namespaceName}
{{
    public partial class {classSymbol.ToDisplayString(format)}
    {{");
			}

			foreach (IFieldSymbol fieldSymbol in fields)
			{
				ProcessField(source, fieldSymbol, attributeSymbol);
			}

			source.Append($@"
    }}
}}");

			return source.ToString();
		}

		private static void ProcessField(StringBuilder source, IFieldSymbol fieldSymbol, ISymbol attributeSymbol)
		{
			var fieldName = fieldSymbol.Name;
			var fieldType = fieldSymbol.Type;
			var attributeData = fieldSymbol.GetAttributes().Single(ad => ad?.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) ?? false);
			var overridenNameOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;
			var propertyName = ChooseName(fieldName, overridenNameOpt);

			if (propertyName is null || propertyName.Length == 0 || propertyName == fieldName)
			{
				// Issue a diagnostic that we can't process this field.
				return;
			}

			var overridenSetterModifierOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "SetterModifier").Value;
			var setterModifier = ChooseSetterModifier(overridenSetterModifierOpt);
			if (setterModifier is null)
			{
				source.Append($@"
        public {fieldType} {propertyName}
        {{
            get => {fieldName};
        }}");
			}
			else
			{
				source.Append($@"
        public {fieldType} {propertyName}
        {{
            get => {fieldName};
            {setterModifier}set => this.RaiseAndSetIfChanged(ref {fieldName}, value);
        }}");
			}

			static string? ChooseSetterModifier(TypedConstant overridenSetterModifierOpt)
			{
				if (!overridenSetterModifierOpt.IsNull && overridenSetterModifierOpt.Value is not null)
				{
					var value = (int)overridenSetterModifierOpt.Value;
					return value switch
					{
						// None
						0 => null,
						// Public
						1 => "",
						// Protected
						2 => "protected ",
						// Private
						3 => "private ",
						// Internal
						4 => "internal ",
						// Default
						_ => ""
					};
				}
				else
				{
					return "";
				}
			}

			static string? ChooseName(string fieldName, TypedConstant overridenNameOpt)
			{
				if (!overridenNameOpt.IsNull)
				{
					return overridenNameOpt.Value?.ToString();
				}

				fieldName = fieldName.TrimStart('_');
				if (fieldName.Length == 0)
				{
					return string.Empty;
				}

				if (fieldName.Length == 1)
				{
					return fieldName.ToUpper();
				}

#pragma warning disable IDE0057 // Use range operator
				return fieldName.Substring(0, 1).ToUpper() + fieldName.Substring(1);
#pragma warning restore IDE0057 // Use range operator
			}
		}

		private class SyntaxReceiver : ISyntaxReceiver
		{
			public List<FieldDeclarationSyntax> CandidateFields { get; } = new List<FieldDeclarationSyntax>();

			public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
			{
				if (syntaxNode is FieldDeclarationSyntax fieldDeclarationSyntax
					&& fieldDeclarationSyntax.AttributeLists.Count > 0)
				{
					CandidateFields.Add(fieldDeclarationSyntax);
				}
			}
		}
	}
}