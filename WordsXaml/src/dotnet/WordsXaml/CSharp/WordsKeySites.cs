using System.Linq;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using WordsXaml.Keys;
using WordsXaml.Sites;

namespace WordsXaml.CSharp
{
    /// <summary>
    /// Recognises a C# string literal that is a words key: one bound to a target marked
    /// <c>[PatTech.Localization.WordsKey]</c>, as analyzer rule PTL002 recognises it. Method,
    /// constructor, indexer and attribute arguments (<c>Words.Known["main.title"]</c>,
    /// <c>Format("main.title", …)</c>, <c>[Words("main.title")]</c>), and assignments to a marked
    /// property or field, in an attribute (<c>Key = "…"</c>), an object initializer or a statement.
    /// A member that overrides or implements a marked one counts as marked. The key's span inside
    /// the quotes comes from <see cref="LiteralKeys"/> (SDK-free, tested).
    /// </summary>
    public static class WordsKeySites
    {
        public const string WordsKeyAttributeName = "PatTech.Localization.WordsKeyAttribute";

        /// <summary>
        /// The key in the string literal at or around <paramref name="node"/>, if it is bound to a
        /// [WordsKey] target and <paramref name="caret"/> is on it (inside the quotes); otherwise null.
        /// </summary>
        public static WordsKeyToken FindAt(ITreeNode node, DocumentOffset caret)
        {
            var literal = node?.GetContainingNode<ICSharpLiteralExpression>(returnThis: true);
            var token = TryGetKeyToken(literal);
            if (token == null || caret.Document != token.Range.Document)
                return null;
            return caret.Offset >= token.Range.StartOffset.Offset && caret.Offset <= token.Range.EndOffset.Offset
                ? token
                : null;
        }

        /// <summary>The key a string literal holds and its range, if the literal is bound to a [WordsKey] target.</summary>
        public static WordsKeyToken TryGetKeyToken(ICSharpLiteralExpression literal)
        {
            var tokenNode = literal?.Literal;
            if (tokenNode == null)
                return null;

            var span = LiteralKeys.Parse(tokenNode.GetText());
            if (span == null || !IsKeySite(literal))
                return null;

            var start = tokenNode.GetDocumentStartOffset();
            return new WordsKeyToken(span.Key, new DocumentRange(start.Shift(span.Start), start.Shift(span.End)));
        }

        private static bool IsKeySite(ICSharpExpression expression)
        {
            var argument = CSharpArgumentNavigator.GetByValue(expression);
            if (argument != null)
                return IsMarked(argument.MatchingParameter?.Element);

            var attributeProperty = PropertyAssignmentNavigator.GetBySource(expression);
            if (attributeProperty != null)
                return IsMarked(attributeProperty.Reference?.Resolve().DeclaredElement);

            var initializer = MemberInitializerNavigator.GetByExpression(expression);
            if (initializer != null)
                return IsMarked(initializer.Reference?.Resolve().DeclaredElement);

            var assignment = AssignmentExpressionNavigator.GetBySource(expression);
            if (assignment?.Dest is IReferenceExpression target)
                return IsMarked(target.Reference.Resolve().DeclaredElement);

            return false;
        }

        // Marked itself, or through a member it overrides or implements, at any remove.
        private static bool IsMarked(IDeclaredElement element)
        {
            switch (element)
            {
                case IParameter parameter:
                    if (HasWordsKey(parameter))
                        return true;
                    if (!(parameter.ContainingParametersOwner is IOverridableMember owner))
                        return false;
                    var ordinal = ((IParametersOwner)owner).Parameters.IndexOf(parameter);
                    return ordinal >= 0 && owner.GetAllSuperMembers(false).Any(super =>
                        super.Member is IParametersOwner superOwner
                        && ordinal < superOwner.Parameters.Count
                        && HasWordsKey(superOwner.Parameters[ordinal]));

                case IProperty property:
                    return HasWordsKey(property)
                        || property.GetAllSuperMembers(false).Any(super => HasWordsKey(super.Member));

                case IField field:
                    return HasWordsKey(field);

                default:
                    return false;
            }
        }

        private static bool HasWordsKey(IAttributesSet owner) =>
            owner.GetAttributeInstances(AttributesSource.Self)
                .Any(attribute => attribute.GetClrName().FullName == WordsKeyAttributeName);
    }
}
