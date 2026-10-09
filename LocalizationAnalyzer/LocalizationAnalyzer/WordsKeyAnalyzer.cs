using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LocalizationAnalyzer
{
    /// <summary>
    /// Roslyn analyzer for rule PTL002: a compile-time-constant string supplied to a
    /// <c>[PatTech.Localization.WordsKey]</c> target must be a key declared in a
    /// <c>*words.ini</c>.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="LocalizationAnalyzer"/> (which validates the value
    /// side): this validates the key side. Keys are read from the <c>*words.ini</c> files
    /// made available to the compilation as AdditionalFiles; when none are present the
    /// analyzer stays silent. Only constant strings are checked — runtime expressions are
    /// left alone. Method, constructor, indexer and attribute arguments and property/field
    /// assignments are covered. A member that overrides or implements a marked one counts
    /// as marked, so an <c>IWords</c> implementation need not repeat the attribute.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class WordsKeyAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID for PTL002 warnings.</summary>
        public const string DiagnosticId = "PTL002";
        /// <summary>The short title shown for PTL002 diagnostics.</summary>
        public const string Title = "Unknown words key";
        /// <summary>The diagnostic category under which PTL002 is grouped.</summary>
        public const string Category = "PatTech.Localization";
        /// <summary>The long-form description of what PTL002 enforces.</summary>
        public const string Description = "A string passed to a [WordsKey] target must be a key declared in a words.ini.";

        /// <summary>Fully qualified name of the attribute that marks a symbol as taking a words key.</summary>
        public static readonly string WordsKeyAttributeName = "PatTech.Localization.WordsKeyAttribute";

        /// <summary>Fires when a constant string bound to a <c>[WordsKey]</c> target is not a declared key.</summary>
        public static readonly DiagnosticDescriptor UnknownKeyDiagnostic
            = new DiagnosticDescriptor(
                    DiagnosticId,
                    Title,
                    "'{0}' is not a known words key",
                    Category,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: Description);

        /// <summary>
        /// Fires when a dynamically-built key (concatenation/interpolation) has a leading literal prefix
        /// that no declared key shares — a likely typo in the constant part. The dynamic tail is not
        /// checked, so a correct prefix never warns.
        /// </summary>
        public static readonly DiagnosticDescriptor UnknownKeyPrefixDiagnostic
            = new DiagnosticDescriptor(
                    DiagnosticId,
                    Title,
                    "No words key starts with '{0}'",
                    Category,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: Description);

        /// <summary>
        /// Fires when a key, or the fixed start of a built one, can't be a key's name at all: code names
        /// keys language-neutrally, so a plural form (<c>"some.key#other"</c>) is never named there — a
        /// count picks it — and no other character outside a key's grammar reaches a key either.
        /// </summary>
        public static readonly DiagnosticDescriptor InvalidKeyDiagnostic
            = new DiagnosticDescriptor(
                    DiagnosticId,
                    Title,
                    "'{0}' is not a words key name: keys are dotted segments of any script's letters and digits, '_' and '-', in NFC, and a plural form is picked by a count, never named",
                    Category,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: Description);

        /// <summary>The PTL002 descriptors this analyzer can report (exact key, leading-prefix, and no key name).</summary>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
            = ImmutableArray.Create(UnknownKeyDiagnostic, UnknownKeyPrefixDiagnostic, InvalidKeyDiagnostic);

        /// <summary>
        /// Loads the key set once per compilation from the <c>*words.ini</c> AdditionalFiles, then (only
        /// if any keys were found) registers callbacks for invocations, object creations, element accesses
        /// (indexers, <c>?[...]</c> included), attributes, and assignments.
        /// </summary>
        /// <param name="context">The analysis context to register callbacks on.</param>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(start =>
            {
                var keys = WordsIniKeys.Load(start.Options.AdditionalFiles, start.CancellationToken);
                if (keys.Count == 0)
                {
                    // No words.ini available to validate against — stay silent rather than warn on everything.
                    return;
                }

                start.RegisterSyntaxNodeAction(c => AnalyzeInvocation(c, keys), SyntaxKind.InvocationExpression);
                start.RegisterSyntaxNodeAction(c => AnalyzeObjectCreation(c, keys), SyntaxKind.ObjectCreationExpression);
                start.RegisterSyntaxNodeAction(
                        c => AnalyzeElementAccess(c, keys),
                        SyntaxKind.ElementAccessExpression,
                        SyntaxKind.ElementBindingExpression);
                start.RegisterSyntaxNodeAction(c => AnalyzeAttribute(c, keys), SyntaxKind.Attribute);
                start.RegisterSyntaxNodeAction(c => AnalyzeAssignment(c, keys), SyntaxKind.SimpleAssignmentExpression);
            });
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, ImmutableHashSet<string> keys)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)
            {
                AnalyzeArguments(context, method.Parameters, invocation.ArgumentList.Arguments, keys, ReceiverType(context, invocation));
            }
        }

        private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context, ImmutableHashSet<string> keys)
        {
            var creation = (ObjectCreationExpressionSyntax)context.Node;
            if (creation.ArgumentList != null
                    && context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is IMethodSymbol ctor)
            {
                AnalyzeArguments(context, ctor.Parameters, creation.ArgumentList.Arguments, keys);
            }
        }

        /// <summary><c>words["key"]</c> and <c>words?["key"]</c>: an indexer's bracketed arguments, as a method's.</summary>
        private static void AnalyzeElementAccess(SyntaxNodeAnalysisContext context, ImmutableHashSet<string> keys)
        {
            BracketedArgumentListSyntax argumentList;
            switch (context.Node)
            {
                case ElementAccessExpressionSyntax access:
                    argumentList = access.ArgumentList;
                    break;
                case ElementBindingExpressionSyntax binding:
                    argumentList = binding.ArgumentList;
                    break;
                default:
                    return;
            }

            if (context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is IPropertySymbol property
                    && property.IsIndexer)
            {
                AnalyzeArguments(context, property.Parameters, argumentList.Arguments, keys, ReceiverType(context, context.Node));
            }
        }

        /// <summary>
        /// <c>[Words("key")]</c>: an attribute's constructor arguments, as a method's, and its
        /// <c>Name = "key"</c> arguments, as assignments to that property or field.
        /// </summary>
        private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context, ImmutableHashSet<string> keys)
        {
            var attribute = (AttributeSyntax)context.Node;
            if (attribute.ArgumentList is null
                    || !(context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is IMethodSymbol ctor))
            {
                return;
            }

            var arguments = attribute.ArgumentList.Arguments;
            for (var argIdx = 0; argIdx < arguments.Count; argIdx++)
            {
                var argument = arguments[argIdx];
                if (argument.NameEquals is NameEqualsSyntax nameEquals)
                {
                    var member = context.SemanticModel.GetSymbolInfo(nameEquals.Name, context.CancellationToken).Symbol;
                    if ((member is IPropertySymbol || member is IFieldSymbol) && HasWordsKeyAttribute(member))
                    {
                        CheckExpression(context, argument.Expression, keys);
                    }
                    continue;
                }

                var parameter = ParameterFor(ctor.Parameters, argIdx, argument.NameColon);
                if (parameter != null && HasWordsKeyAttribute(parameter))
                {
                    CheckExpression(context, argument.Expression, keys);
                }
            }
        }

        private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context, ImmutableHashSet<string> keys)
        {
            var assignment = (AssignmentExpressionSyntax)context.Node;
            var target = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;

            if ((target is IPropertySymbol || target is IFieldSymbol) && HasWordsKeyAttribute(target, ReceiverType(context, assignment.Left)))
            {
                CheckExpression(context, assignment.Right, keys);
            }
        }

        /// <summary>Maps each argument to its parameter (named, positional, params) and checks [WordsKey] ones.</summary>
        private static void AnalyzeArguments(
                SyntaxNodeAnalysisContext context,
                ImmutableArray<IParameterSymbol> parameters,
                SeparatedSyntaxList<ArgumentSyntax> arguments,
                ImmutableHashSet<string> keys,
                ITypeSymbol receiver = null)
        {
            for (var argIdx = 0; argIdx < arguments.Count; argIdx++)
            {
                var argument = arguments[argIdx];
                var parameter = ParameterFor(parameters, argIdx, argument.NameColon);
                if (parameter != null && HasWordsKeyAttribute(parameter, receiver))
                {
                    CheckExpression(context, argument.Expression, keys);
                }
            }
        }

        /// <summary>
        /// The parameter the argument at <paramref name="argIdx"/> binds to: by name if it is named,
        /// else by position, every argument from a <c>params</c> parameter's position on binding to it;
        /// <see langword="null"/> if none does.
        /// </summary>
        private static IParameterSymbol ParameterFor(
                ImmutableArray<IParameterSymbol> parameters,
                int argIdx,
                NameColonSyntax nameColon)
        {
            if (nameColon != null)
            {
                foreach (var p in parameters)
                {
                    if (p.Name == nameColon.Name.Identifier.ValueText)
                    {
                        return p;
                    }
                }
                return null;
            }

            var variadicIndex = parameters.Length >= 1 && parameters[parameters.Length - 1].IsParams
                ? parameters.Length - 1
                : int.MaxValue;
            if (argIdx > variadicIndex)
            {
                return parameters[variadicIndex];
            }
            return argIdx < parameters.Length ? parameters[argIdx] : null;
        }

        /// <summary>
        /// Validates the expression bound to a <c>[WordsKey]</c> target. A compile-time-constant string is
        /// checked exactly. A dynamically-built string (concatenation/interpolation) can't be resolved, so
        /// it is only flagged when a determinable leading literal prefix matches no declared key.
        /// </summary>
        private static void CheckExpression(
                SyntaxNodeAnalysisContext context,
                ExpressionSyntax expression,
                ImmutableHashSet<string> keys)
        {
            var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constant.HasValue && constant.Value is string key)
            {
                // "" names no key on purpose: an inline or markup extension takes it to mean nothing
                if (key.Length == 0)
                {
                    return;
                }
                if (!WordsIniKeys.IsKeyName(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidKeyDiagnostic, expression.GetLocation(), key));
                }
                else if (!keys.Contains(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnknownKeyDiagnostic, expression.GetLocation(), key));
                }
                return;
            }

            // Not constant: built at runtime. Don't flag the value itself (we can't know it), but if there
            // is a fixed leading segment that no key name can start with, or that matches no key at all,
            // that prefix is probably wrong.
            var prefix = LeadingLiteralPrefix(context.SemanticModel, expression, context.CancellationToken);
            if (prefix.Length > 0 && !WordsIniKeys.CouldStartKeyName(prefix))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidKeyDiagnostic, expression.GetLocation(), prefix));
            }
            else if (prefix.Length > 0 && !AnyKeyStartsWith(keys, prefix))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnknownKeyPrefixDiagnostic, expression.GetLocation(), prefix));
            }
        }

        /// <summary>
        /// The fixed text at the start of a dynamically-built string, or "" if none can be determined.
        /// Handles a string concatenation whose left side is constant and a leading run of interpolated
        /// string text; anything more involved yields "" (skipped) to keep the heuristic simple.
        /// </summary>
        private static string LeadingLiteralPrefix(
                SemanticModel semanticModel,
                ExpressionSyntax expression,
                System.Threading.CancellationToken cancellationToken)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    return LeadingLiteralPrefix(semanticModel, parenthesized.Expression, cancellationToken);

                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    return literal.Token.ValueText;

                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                    {
                        var leftConstant = semanticModel.GetConstantValue(binary.Left, cancellationToken);
                        if (leftConstant.HasValue && leftConstant.Value is string leftText)
                        {
                            // Whole left side is constant — keep folding into the right.
                            return leftText + LeadingLiteralPrefix(semanticModel, binary.Right, cancellationToken);
                        }
                        return LeadingLiteralPrefix(semanticModel, binary.Left, cancellationToken);
                    }

                case InterpolatedStringExpressionSyntax interpolated:
                    {
                        var builder = new StringBuilder();
                        foreach (var content in interpolated.Contents)
                        {
                            if (content is InterpolatedStringTextSyntax text)
                            {
                                builder.Append(text.TextToken.ValueText);
                            }
                            else
                            {
                                break; // stop at the first {hole}
                            }
                        }
                        return builder.ToString();
                    }

                default:
                    return string.Empty;
            }
        }

        private static bool AnyKeyStartsWith(ImmutableHashSet<string> keys, string prefix)
        {
            foreach (var key in keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The type of the instance a call, indexer or member reference is made on, which may implement
        /// an interface through a member it inherits; <see langword="null"/> for a static one.
        /// </summary>
        private static ITypeSymbol ReceiverType(SyntaxNodeAnalysisContext context, SyntaxNode node)
        {
            switch (context.SemanticModel.GetOperation(node, context.CancellationToken))
            {
                case IInvocationOperation invocation:
                    return invocation.Instance?.Type;
                case IPropertyReferenceOperation property:
                    return property.Instance?.Type;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Whether <paramref name="symbol"/> (a parameter, property or field) is marked <c>[WordsKey]</c>,
        /// itself or through a member it overrides or implements, at any remove: the key parameter of a
        /// class's indexer that implements <c>IWords</c>' is marked as that one is.
        /// </summary>
        private static bool HasWordsKeyAttribute(ISymbol symbol, ITypeSymbol receiver = null)
        {
            if (HasOwnWordsKeyAttribute(symbol))
            {
                return true;
            }

            switch (symbol)
            {
                case IParameterSymbol parameter:
                    foreach (var member in BaseMembers(parameter.ContainingSymbol, receiver))
                    {
                        var parameters = member is IMethodSymbol method
                            ? method.Parameters
                            : ((IPropertySymbol)member).Parameters;
                        if (parameter.Ordinal < parameters.Length && HasWordsKeyAttribute(parameters[parameter.Ordinal], receiver))
                        {
                            return true;
                        }
                    }
                    return false;

                case IPropertySymbol property:
                    foreach (var member in BaseMembers(property, receiver))
                    {
                        if (HasWordsKeyAttribute(member, receiver))
                        {
                            return true;
                        }
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// The method or property <paramref name="member"/> overrides, and the interface members it
        /// implements, explicitly or implicitly, for its own type or for <paramref name="receiver"/>,
        /// which may implement an interface through it by inheritance; nothing for any other symbol.
        /// </summary>
        private static IEnumerable<ISymbol> BaseMembers(ISymbol member, ITypeSymbol receiver)
        {
            // a generic method called as Generic<int> implements as its definition, Generic<T>
            if (member is IMethodSymbol generic && !SymbolEqualityComparer.Default.Equals(generic, generic.ConstructedFrom))
            {
                member = generic.ConstructedFrom;
            }

            ImmutableArray<ISymbol> explicitImplementations;
            ISymbol overridden;
            switch (member)
            {
                case IMethodSymbol method:
                    explicitImplementations = ImmutableArray<ISymbol>.CastUp(method.ExplicitInterfaceImplementations);
                    overridden = method.OverriddenMethod;
                    break;
                case IPropertySymbol property:
                    explicitImplementations = ImmutableArray<ISymbol>.CastUp(property.ExplicitInterfaceImplementations);
                    overridden = property.OverriddenProperty;
                    break;
                default:
                    yield break;
            }

            if (overridden != null)
            {
                yield return overridden;
            }

            foreach (var implemented in explicitImplementations)
            {
                yield return implemented;
            }

            var type = member.ContainingType;
            // only a public member of a class or struct implements an interface's implicitly
            if (!explicitImplementations.IsEmpty
                    || member.DeclaredAccessibility != Accessibility.Public
                    || type is null
                    || type.TypeKind == TypeKind.Interface)
            {
                yield break;
            }
            var implementers = receiver is INamedTypeSymbol named && !SymbolEqualityComparer.Default.Equals(named, type)
                ? new[] { type, named }
                : new[] { type };
            foreach (var implementer in implementers)
            {
                foreach (var @interface in implementer.AllInterfaces)
                {
                    foreach (var candidate in @interface.GetMembers(member.Name))
                    {
                        if (SymbolEqualityComparer.Default.Equals(implementer.FindImplementationForInterfaceMember(candidate), member))
                        {
                            yield return candidate;
                        }
                    }
                }
            }
        }

        private static bool HasOwnWordsKeyAttribute(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass != null
                        && attributeClass.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == WordsKeyAttributeName)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
