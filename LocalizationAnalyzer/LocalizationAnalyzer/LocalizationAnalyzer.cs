using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace LocalizationAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LocalizationAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "PTL001";
        public const string Title = "Expecting localized value";
        public const string Category = "PatTech.Localization";
        public const string Description = "Localized items should receive localized strings.";

        public static readonly string LocalizationAttributeName = "PatTech.Localization.LocalizedAttribute";

        public static readonly DiagnosticDescriptor MethodParameterDiagnostic
            = new DiagnosticDescriptor(
                    DiagnosticId,
                    Title,
                    "Parameter `{0}` in method `{1}` expects a localized value",
                    Category,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: Description);
        public static readonly DiagnosticDescriptor PropertyAssignmentDiagnostic
            = new DiagnosticDescriptor(
                    DiagnosticId,
                    Title,
                    "Property `{0}` expects a localized value",
                    Category,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: Description);
        public static readonly DiagnosticDescriptor FieldAssignmentDiagnostic
            = new DiagnosticDescriptor(
                    DiagnosticId,
                    Title,
                    "Field `{0}` expects a localized value",
                    Category,
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true,
                    description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
            = ImmutableArray.Create(
                    MethodParameterDiagnostic,
                    PropertyAssignmentDiagnostic,
                    FieldAssignmentDiagnostic);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(
                    AnalyzeInvocationExpression,
                    SyntaxKind.InvocationExpression);

            context.RegisterSyntaxNodeAction(
                    AnalyzeAssignmentExpression,
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxKind.AddAssignmentExpression);

            // NOTE: since we'll need to scan all declarations, might as well do everything in here
            //context.RegisterSyntaxNodeAction(
            //		AnalyzeDeclaration,
            //		SyntaxKind.MethodDeclaration,
            //		SyntaxKind.PropertyDeclaration);
        }

        /// <summary>Report diagnostics for all problem parameters.</summary>
        private static void AnalyzeInvocationExpression(
                SyntaxNodeAnalysisContext context)
        {
            var cancellationToken = context.CancellationToken;
            var invocation = (InvocationExpressionSyntax)context.Node;

            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
            if (!(symbolInfo.Symbol is IMethodSymbol methodSymbol))
            {
                return;
            }

            // gather metadata on parameters
            var parameters = methodSymbol.Parameters;
            var arguments = invocation.ArgumentList.Arguments;
            int variadicIndex;
            if (parameters.Length >= 1 && parameters[parameters.Length - 1].IsParams)
            {
                variadicIndex = parameters.Length - 1;
            }
            else
            {
                variadicIndex = int.MaxValue;
            }

            // process all arguments
            for (int argumentIdx = 0; argumentIdx < arguments.Count; argumentIdx++)
            {
                var argument = arguments[argumentIdx];
                IParameterSymbol parameterSymbol;

                // map to a method parameter
                if (argument.NameColon is NameColonSyntax nameColon)
                {
                    parameterSymbol = null;
                    for (int parameterIdx = 0; parameterIdx < parameters.Length; parameterIdx++)
                    {
                        IParameterSymbol param = parameters[parameterIdx];
                        if (param.Name == nameColon.Name.Identifier.ValueText)
                        {
                            parameterSymbol = param;
                            break;
                        }
                    }
                    if (parameterSymbol is null)
                    {
                        continue;
                    }
                }
                else if (argumentIdx > variadicIndex)
                {
                    parameterSymbol = parameters[variadicIndex];
                }
                else if (argumentIdx < parameters.Length)
                {
                    parameterSymbol = parameters[argumentIdx];
                }
                else
                {
                    // argument does not map to a parameter
                    continue;
                }

                if (parameterSymbol.RefKind is RefKind.Out
                        || !SymbolIsLocalized(parameterSymbol))
                {
                    // out parameters and untagged parameters do not need to be processed
                    continue;
                }

                // determine which diagnostics are warranted
                var problemNodes = FindLocalizationErrors(
                        context.SemanticModel,
                        argument.Expression,
                        cancellationToken);
                foreach (var node in problemNodes)
                {
                    context.ReportDiagnostic(
                            Diagnostic.Create(
                                    MethodParameterDiagnostic,
                                    node.GetLocation(),
                                    GetArgumentName(parameterSymbol),
                                    GetMethodName(methodSymbol)));
                }
            }
        }
        /// <summary>Report diagnostics in the expression.</summary>
        private static void AnalyzeAssignmentExpression(
                SyntaxNodeAnalysisContext context)
        {
            var cancellationToken = context.CancellationToken;
            var assignment = (AssignmentExpressionSyntax)context.Node;

            // determine if this the target needs to be localized
            var symbolInfo = context.SemanticModel.GetSymbolInfo(assignment.Left, cancellationToken);
            switch (symbolInfo.Symbol)
            {
                case IFieldSymbol _:
                case IPropertySymbol _:
                    if (!SymbolIsLocalized(symbolInfo.Symbol))
                    {
                        return;
                    }
                    break;
                case IParameterSymbol parameter:
                    if (!(parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref)
                            || !SymbolIsLocalized(parameter))
                    {
                        return;
                    }
                    break;
                default:
                    return;
            }

            // determine which diagnostics are warranted
            var problemNodes = FindLocalizationErrors(
                    context.SemanticModel,
                    assignment.Right,
                    cancellationToken);
            foreach (var node in problemNodes)
            {
                DiagnosticDescriptor descriptor;
                object[] args;
                switch (symbolInfo.Symbol)
                {
                    case IFieldSymbol field:
                        descriptor = FieldAssignmentDiagnostic;
                        args = new object[] {
                            field.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        };
                        break;
                    case IPropertySymbol property:
                        descriptor = PropertyAssignmentDiagnostic;
                        args = new object[] {
                            property.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        };
                        break;
                    case IParameterSymbol parameter:
                        descriptor = MethodParameterDiagnostic;
                        args = new object[] {
                            parameter.Name,
                            parameter.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        };
                        break;
                    default:
                        throw new InvalidOperationException("should never happen?!");
                }
                context.ReportDiagnostic(
                        Diagnostic.Create(
                                descriptor,
                                node.GetLocation(),
                                args));
            }
        }

        /// <summary>Scan <paramref name="expression"/> for all localization errors.</summary>
        /// <returns>A sequence of syntax nodes which require diagnostics.</returns>
        private static IEnumerable<SyntaxNode> FindLocalizationErrors(
                SemanticModel semanticModel,
                ExpressionSyntax expression,
                CancellationToken cancellationToken)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    foreach (var node in FindLocalizationErrors(semanticModel, parenthesized.Expression, cancellationToken))
                    {
                        yield return node;
                    }
                    break;
                case AwaitExpressionSyntax await:
                    foreach (var node in FindLocalizationErrors(semanticModel, await.Expression, cancellationToken))
                    {
                        yield return node;
                    }
                    break;
                case ConditionalExpressionSyntax conditional:
                    if (IsLocalizationError(semanticModel, conditional.WhenTrue, cancellationToken))
                    {
                        yield return conditional.WhenTrue;
                    }
                    if (IsLocalizationError(semanticModel, conditional.WhenFalse, cancellationToken))
                    {
                        yield return conditional.WhenFalse;
                    }
                    break;
                case SwitchExpressionSyntax @switch:
                    {
                        var arms = @switch.Arms;
                        for (var i = 0; i < arms.Count; ++i)
                        {
                            foreach (var node in FindLocalizationErrors(semanticModel, arms[i].Expression, cancellationToken))
                            {
                                yield return node;
                            }
                        }
                        break;
                    }
                default:
                    if (IsLocalizationError(semanticModel, expression, cancellationToken))
                    {
                        yield return expression;
                    }
                    break;
            }
        }

        private static bool SymbolIsLocalized(ISymbol symbol)
        {
            return ContainsLocalizedAttribute(symbol.GetAttributes());
        }
        private static bool MethodReturnsLocalized(IMethodSymbol symbol)
        {
            return ContainsLocalizedAttribute(symbol.GetReturnTypeAttributes());
        }
        private static bool ContainsLocalizedAttribute(ImmutableArray<AttributeData> attributes)
        {
            for (int i = 0; i < attributes.Length; i++)
            {
                var type = attributes[i].AttributeClass;
                var typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (typeName == LocalizationAttributeName)
                {
                    return true;
                }
            }
            return false;
        }
        private static bool IsLocalizationError(
                SemanticModel semanticModel,
                ExpressionSyntax expression,
                CancellationToken cancellationToken)
        {
            switch (expression)
            {
                case NameSyntax identifier:
                    {
                        if (!(semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is ISymbol symbol))
                        {
                            return true;
                        }

                        return !SymbolIsLocalized(symbol);
                    }
                case MemberAccessExpressionSyntax memberAccess:
                    return IsLocalizationError(semanticModel, memberAccess.Name, cancellationToken);
                case ElementAccessExpressionSyntax elementAccess:
                    {
                        if (!(semanticModel.GetSymbolInfo(elementAccess, cancellationToken).Symbol is ISymbol symbol))
                        {
                            return true;
                        }

                        return !SymbolIsLocalized(symbol);
                    }
                case ParenthesizedExpressionSyntax parenthesized:
                    return IsLocalizationError(semanticModel, parenthesized.Expression, cancellationToken);
                case AwaitExpressionSyntax await:
                    return IsLocalizationError(semanticModel, await.Expression, cancellationToken);
                case InvocationExpressionSyntax invocation:
                    {
                        if (!(semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken).Symbol is IMethodSymbol symbol))
                        {
                            return true;
                        }

                        return !MethodReturnsLocalized(symbol.ReducedFrom ?? symbol);
                    }
                case ConditionalAccessExpressionSyntax conditional:
                    return IsLocalizationError(semanticModel, conditional.WhenNotNull, cancellationToken);
                case ConditionalExpressionSyntax conditional:
                    return IsLocalizationError(semanticModel, conditional.WhenTrue, cancellationToken)
                        || IsLocalizationError(semanticModel, conditional.WhenFalse, cancellationToken);
                case SwitchExpressionSyntax @switch:
                    {
                        var arms = @switch.Arms;
                        for (var i = 0; i < arms.Count; ++i)
                        {
                            if (IsLocalizationError(semanticModel, arms[i].Expression, cancellationToken))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                default:
                    return true;
            }
        }

        private static string GetArgumentName(IParameterSymbol parameterSymbol)
        {
            return OrQuestionMark(parameterSymbol.Name);
        }
        private static string GetMethodName(IMethodSymbol methodSymbol)
        {
            return OrQuestionMark(methodSymbol.Name);
        }
        private static string OrQuestionMark(string text)
        {
            if (string.IsNullOrEmpty(text)) return "??";
            return text;
        }
    }
}
