using System;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

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
    /// left alone. Method/constructor arguments and property/field assignments are covered.
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

        /// <summary>The PTL002 descriptors this analyzer can report (exact key and leading-prefix).</summary>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
            = ImmutableArray.Create(UnknownKeyDiagnostic, UnknownKeyPrefixDiagnostic);

        /// <summary>
        /// Loads the key set once per compilation from the <c>*words.ini</c> AdditionalFiles, then (only
        /// if any keys were found) registers callbacks for invocations, object creations, and assignments.
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
                start.RegisterSyntaxNodeAction(c => AnalyzeAssignment(c, keys), SyntaxKind.SimpleAssignmentExpression);
            });
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, ImmutableHashSet<string> keys)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)
            {
                AnalyzeArguments(context, method, invocation.ArgumentList, keys);
            }
        }

        private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context, ImmutableHashSet<string> keys)
        {
            var creation = (ObjectCreationExpressionSyntax)context.Node;
            if (creation.ArgumentList != null
                    && context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is IMethodSymbol ctor)
            {
                AnalyzeArguments(context, ctor, creation.ArgumentList, keys);
            }
        }

        private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context, ImmutableHashSet<string> keys)
        {
            var assignment = (AssignmentExpressionSyntax)context.Node;
            var target = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;

            if ((target is IPropertySymbol || target is IFieldSymbol) && HasWordsKeyAttribute(target))
            {
                CheckExpression(context, assignment.Right, keys);
            }
        }

        /// <summary>Maps each argument to its parameter (named, positional, params) and checks [WordsKey] ones.</summary>
        private static void AnalyzeArguments(
                SyntaxNodeAnalysisContext context,
                IMethodSymbol method,
                ArgumentListSyntax argumentList,
                ImmutableHashSet<string> keys)
        {
            var parameters = method.Parameters;
            var arguments = argumentList.Arguments;
            var variadicIndex = parameters.Length >= 1 && parameters[parameters.Length - 1].IsParams
                ? parameters.Length - 1
                : int.MaxValue;

            for (var argIdx = 0; argIdx < arguments.Count; argIdx++)
            {
                var argument = arguments[argIdx];
                IParameterSymbol parameter;

                if (argument.NameColon is NameColonSyntax nameColon)
                {
                    parameter = null;
                    foreach (var p in parameters)
                    {
                        if (p.Name == nameColon.Name.Identifier.ValueText)
                        {
                            parameter = p;
                            break;
                        }
                    }
                    if (parameter is null)
                    {
                        continue;
                    }
                }
                else if (argIdx > variadicIndex)
                {
                    parameter = parameters[variadicIndex];
                }
                else if (argIdx < parameters.Length)
                {
                    parameter = parameters[argIdx];
                }
                else
                {
                    continue;
                }

                if (HasWordsKeyAttribute(parameter))
                {
                    CheckExpression(context, argument.Expression, keys);
                }
            }
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
                if (!keys.Contains(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnknownKeyDiagnostic, expression.GetLocation(), key));
                }
                return;
            }

            // Not constant: built at runtime. Don't flag the value itself (we can't know it), but if there
            // is a fixed leading segment that matches no key at all, that prefix is probably wrong.
            var prefix = LeadingLiteralPrefix(context.SemanticModel, expression, context.CancellationToken);
            if (prefix.Length > 0 && !AnyKeyStartsWith(keys, prefix))
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

        private static bool HasWordsKeyAttribute(ISymbol symbol)
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
