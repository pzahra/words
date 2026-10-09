using JetBrains.Application.UI.Components.Theming;
using JetBrains.ReSharper.Feature.Services.QuickDoc;
using JetBrains.ReSharper.Feature.Services.QuickDoc.Render;
using JetBrains.ReSharper.Psi;
using JetBrains.UI.RichText;

namespace WordsXaml.Keys
{
    /// <summary>
    /// The quick-doc popup for a words key: <see cref="Ini.WordsQuickDoc.Html"/>'s body in the
    /// standard quick-doc frame. Built the way JetBrains' own JSON schema tooltip
    /// (<c>JsonQuickDocProvider.StubQuickDocPresenter</c>) builds its page: <c>XmlDocHtmlUtil.FullHtml</c>
    /// with the renderer's head and body. Nothing to navigate to.
    /// </summary>
    public sealed class WordsQuickDocPresenter : IQuickDocPresenter
    {
        private readonly string _key;
        private readonly string _html;
        private readonly ITheming _theming;
        private readonly IXmlDocHtmlRenderer _renderer;

        public WordsQuickDocPresenter(string key, string html, ITheming theming, IXmlDocHtmlRenderer renderer)
        {
            _key = key;
            _html = html;
            _theming = theming;
            _renderer = renderer;
        }

        public QuickDocTitleAndText GetHtml(PsiLanguageType presentationLanguage)
        {
            var page = XmlDocHtmlUtil.FullHtml(
                new RichText(),
                head => _renderer.BuildHead(head),
                body => _renderer.BuildFullBody(
                    new XmlDocHtmlUtil.HTMLBuilder(_renderer),
                    body,
                    (_, output) => output.Append(_html),
                    XmlDocHtmlUtil.NavigationStyle.None,
                    string.Empty),
                _theming);
            return new QuickDocTitleAndText(page.ToString(), _key);
        }

        public string GetId() => null;

        public IQuickDocPresenter Resolve(string id) => null;

        public void OpenInEditor(string navigationId = null)
        {
        }

        public void ReadMore(string navigationId = null)
        {
        }
    }
}
