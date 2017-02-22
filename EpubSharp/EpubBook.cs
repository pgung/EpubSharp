using EpubSharp.Format;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EpubSharp
{

    public class EpubBook
    {
        internal const string AuthorsSeparator = ", ";
        

        /// <summary>
        /// Read-only raw epub format structures.
        /// </summary>
        public EpubFormat Format { get; internal set; }

        public string Title => Format.Opf.Metadata.Titles.FirstOrDefault();

        public IList<string> Authors => Format.Opf.Metadata.Creators.Select(creator => creator.Text).ToList();

        /// <summary>
        /// Comma-separated authors.
        /// </summary>
        public string Author => Authors.Any() ? string.Join(AuthorsSeparator, Authors) : null;

        /// <summary>
        /// All files within the EPUB.
        /// </summary>
        public EpubResources Resources { get; internal set; }

        /// <summary>
        /// EPUB format specific resources.
        /// </summary>
        public EpubSpecialResources SpecialResources { get; internal set; }

        public byte[] CoverImage { get; internal set; }

        public List<EpubChapter> TableOfContents { get; internal set; }

        public bool IsPrePaginated()
        {
            return Format.Opf.Metadata.Metas.Any(x => x.Property.Equals("rendition:layout") && x.Text.Equals("pre-paginated"));
        }

        public List<TocLink> GetTocLinks()
        {
            var spine = Spine();
            if (spine == null)
                return new List<TocLink>();

            // If Toc == NCX -> Epub 2
            // If Toc == Nav -> Epub 3
            var navPoints = spine.TOCEpub2.NavigationItems();
                        
            var spines = AllSpines();

            if (navPoints.Any())
                return MapItemsWithSpines(navPoints, 0, spines).ToList();

            return spines.Select((currentSpine, index) =>
                    new TocLink
                    {
                        IdHref = currentSpine.Id,
                        Title = FormatPageOrChapter(index),
                        Anchor = currentSpine.IdRef,
                        Children = new List<TocLink>(),
                        Depth = 0
                    }).ToList();
        }

        public OpfSpine Spine()
        {
            return Format.Opf.Spine;
        }

        public IList<OpfSpineItemRef> AllSpines()
        {
            var spine = Spine();
            return spine == null ? new List<OpfSpineItemRef>() : spine.Spines(Format.Opf.Manifest).ToList();
        }

        public string ToPlainText()
        {
            var builder = new StringBuilder();
            foreach (var html in SpecialResources.HtmlInReadingOrder)
            {
                builder.Append(HtmlProcessor.GetContentAsPlainText(html.TextContent));
                builder.Append('\n');
            }
            return builder.ToString().Trim();
        }

        private string FormatPageOrChapter(int numberPageOrChapter)
        {
            var fixedLayout = IsPrePaginated();

            return string.Format("{0} {1}", fixedLayout ? "page" : "chapter", numberPageOrChapter);
        }

        


        private IEnumerable<TocLink> MapItemsWithSpines(IEnumerable<OpfSpineItemRef> items, int depth, IEnumerable<OpfSpineItemRef> spines)
        {
            return items.Select((navpoint, index) =>
            {
                var r = spines.FirstOrDefault(x => CompareLinks(x.IdRef, navpoint.IdRef));

                IEnumerable<TocLink> ci;
                if (navpoint.Children.Any())
                {
                    var childrenItems = MapItemsWithSpines(navpoint.Children, depth + 1, spines);
                    ci = childrenItems.Count() > 1 ? childrenItems : new List<TocLink>();
                }
                else
                    ci = new List<TocLink>();

                if (r != null)
                    return new TocLink { IdHref = r.Id, Title = navpoint.IdRef, Anchor = Anchor(navpoint.IdRef), Children = ci, Depth = depth };
                return new TocLink { IdHref = navpoint.Id, Title = FormatPageOrChapter(index), Anchor = Anchor(navpoint.IdRef), Children = ci, Depth = depth };
            }).GroupBy(x => x.IdHref).Select(x => x.First());
        }

        private bool CompareLinks(string link1, string link2)
        {
            string l1 = CleanLink(link1);
            string l2 = CleanLink(link2);
            return l1.EndsWith(l2, StringComparison.CurrentCulture) || l2.EndsWith(l1, StringComparison.CurrentCulture);
        }

        private string CleanLink(string linkProvided)
        {
            if (linkProvided.Contains('#'))
            {
                var index = linkProvided.IndexOf('#');
                return linkProvided.Substring(0, index);
            }
            return linkProvided;
        }

        private string Anchor(string pa)
        {
            var index = pa.IndexOf('#');
            switch (index)
            {
                case -1: return pa;
                default:
                    return pa.Substring(index);
            }
        }
    }

    public class EpubChapter
    {
        public string Title { get; set; }
        public string FileName { get; set; }
        public string Anchor { get; set; }
        public IList<EpubChapter> SubChapters { get; set; } = new List<EpubChapter>();

        public override string ToString()
        {
            return $"Title: {Title}, Subchapter count: {SubChapters.Count}";
        }
    }

    public class EpubResources
    {
        public ICollection<EpubTextFile> Html { get; internal set; } = new List<EpubTextFile>();
        public ICollection<EpubTextFile> Css { get; internal set; } = new List<EpubTextFile>();
        public ICollection<EpubByteFile> Images { get; internal set; } = new List<EpubByteFile>();
        public ICollection<EpubByteFile> Fonts { get; internal set; } = new List<EpubByteFile>();
        public ICollection<EpubFile> Other { get; internal set; } = new List<EpubFile>();
    }

    public class EpubSpecialResources
    {
        public EpubTextFile Ocf { get; internal set; }
        public EpubTextFile Opf { get; internal set; }
        public List<EpubTextFile> HtmlInReadingOrder { get; internal set; } = new List<EpubTextFile>();
    }

    public abstract class EpubFile
    {
        public string FileName { get; set; }
        public EpubContentType ContentType { get; set; }
        public string MimeType { get; set; }
        public byte[] Content { get; set; }
    }

    public class EpubByteFile : EpubFile
    {
        internal EpubTextFile ToTextFile()
        {
            return new EpubTextFile
            {
                Content = Content,
                ContentType = ContentType,
                FileName = FileName,
                MimeType = MimeType
            };
        }
    }

    public class EpubTextFile : EpubFile
    {
        public string TextContent
        {
            get { return Constants.DefaultEncoding.GetString(Content, 0, Content.Length); }
            set { Content = Constants.DefaultEncoding.GetBytes(value); }
        }
    }


    public class TocLink
    {
        public string IdHref { get; set; }
        public string Title { get; set; }
        public string Anchor { get; set; }
        public IEnumerable<TocLink> Children { get; set; }
        public int Depth { get; set; }
    }
}
