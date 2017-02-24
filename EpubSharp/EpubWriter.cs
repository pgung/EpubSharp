﻿using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using EpubSharp.Format;
using EpubSharp.Format.Writers;

namespace EpubSharp
{
    public enum ImageFormat
    {
        Gif, Png, Jpeg, Svg
    }

    public class EpubWriter
    {
        private readonly string opfPath = "EPUB/package.opf";
        private readonly string ncxPath = "EPUB/toc.ncx";

        private readonly EpubFormat format;
        private readonly EpubResources resources;

        public EpubWriter()
        {
            var opf = new OpfDocument { EpubVersion = EpubVersion.Epub3 };
            opf.Metadata.Dates.Add(new OpfMetadataDate { Text = DateTimeOffset.UtcNow.ToString("o") });
            opf.Manifest.Items.Add(new OpfManifestItem { Id = "ncx", Href = "toc.ncx", MediaType = ContentType.ContentTypeToMimeType[EpubContentType.DtbookNcx] });
            opf.Spine.Toc = "ncx";

            format = new EpubFormat
            {
                Opf = opf,
                Nav = new NavDocument(),
                Ncx = new NcxDocument()
            };

            format.Nav.Head.Dom = new XElement(NavElements.Head);
            format.Nav.Body.Dom =
                new XElement(
                    NavElements.Body,
                        new XElement(NavElements.Nav, new XAttribute(NavNav.Attributes.Type, NavNav.Attributes.TypeValues.Toc),
                            new XElement(NavElements.Ol)));

            resources = new EpubResources();
        }

        public EpubWriter(EpubBook book)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (book.Format?.Opf == null) throw new ArgumentException("book opf instance == null", nameof(book));

            format = book.Format;
            resources = book.Resources;

            opfPath = format.Ocf.RootFilePath;
            ncxPath = format.Opf.FindNcxPath();

            if (ncxPath != null)
            {
                // Remove NCX file from the resources - Write() will format a new one.
                resources.Other = resources.Other.Where(e => e.FileName != ncxPath).ToList();

                ncxPath = PathExt.Combine(PathExt.GetDirectoryPath(opfPath), ncxPath);
            }
        }

        public static void Write(EpubBook book, string filename)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentNullException(nameof(filename));

            var writer = new EpubWriter(book);
            writer.Write(filename);
        }

        public static void Write(EpubBook book, Stream stream)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var writer = new EpubWriter(book);
            writer.Write(stream);
        }

        /// <summary>
        /// Clones the book instance by writing and reading it from memory.
        /// </summary>
        /// <param name="book"></param>
        /// <returns></returns>
        public static EpubBook MakeCopy(EpubBook book)
        {
            var stream = new MemoryStream();
            var writer = new EpubWriter(book);
            writer.Write(stream);
            stream.Seek(0, SeekOrigin.Begin);
            var epub = EpubReader.Read(stream, false, string.Empty);
            return epub;
        }
        
        public void AddFile(string filename, byte[] content, EpubContentType type)
        {
            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentNullException(nameof(filename));
            if (content == null) throw new ArgumentNullException(nameof(content));

            var file = new EpubByteFile
            {
                FileName = filename,
                ContentType = type,
                Content = content
            };
            file.MimeType = ContentType.ContentTypeToMimeType[file.ContentType];

            switch (type)
            {
                case EpubContentType.Css:
                    resources.Css.Add(file.ToTextFile());
                    break;

                case EpubContentType.FontOpentype:
                case EpubContentType.FontTruetype:
                    resources.Fonts.Add(file);
                    break;

                case EpubContentType.ImageGif:
                case EpubContentType.ImageJpeg:
                case EpubContentType.ImagePng:
                case EpubContentType.ImageSvg:
                    resources.Images.Add(file);
                    break;

                case EpubContentType.Xml:
                case EpubContentType.Xhtml11:
                case EpubContentType.Other:
                    resources.Other.Add(file);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported file type: {type}");
            }

            format.Opf.Manifest.Items.Add(new OpfManifestItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Href = filename,
                MediaType = file.MimeType
            });
        }

        public void AddFile(string filename, string content, EpubContentType type)
        {
            AddFile(filename, Constants.DefaultEncoding.GetBytes(content), type);
        }

        public void AddAuthor(string author)
        {
            if (string.IsNullOrWhiteSpace(author)) throw new ArgumentNullException(nameof(author));
            format.Opf.Metadata.Creators.Add(new OpfMetadataCreator { Text = author });
        }

        public void ClearAuthors()
        {
            format.Opf.Metadata.Creators.Clear();
        }

        public void RemoveAuthor(string author)
        {
            if (string.IsNullOrWhiteSpace(author)) throw new ArgumentNullException(nameof(author));
            foreach (var entity in format.Opf.Metadata.Creators.Where(e => e.Text == author).ToList())
            {
                format.Opf.Metadata.Creators.Remove(entity);
            }
        }
        
        public void RemoveTitle()
        {
            format.Opf.Metadata.Titles.Clear();
        }

        public void SetTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentNullException(nameof(title));
            RemoveTitle();
            format.Opf.Metadata.Titles.Add(title);
        }

        public EpubChapter AddChapter(string title, string html)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentNullException(nameof(title));
            if (html == null) throw new ArgumentNullException(nameof(html));

            var fileId = Guid.NewGuid().ToString("N");
            var file = new EpubTextFile
            {
                FileName = fileId + ".xhtml",
                TextContent = html,
                ContentType = EpubContentType.Xhtml11
            };
            file.MimeType = ContentType.ContentTypeToMimeType[file.ContentType];
            resources.Html.Add(file);

            var manifestItem = new OpfManifestItem
            {
                Id = fileId,
                Href = file.FileName,
                MediaType = file.MimeType
            };
            format.Opf.Manifest.Items.Add(manifestItem);

            var spineItem = new OpfSpineItemRef { IdRef = manifestItem.Id };
            format.Opf.Spine.ItemRefs.Add(spineItem);

            FindNavTocOl()?.Add(new XElement(NavElements.Li, new XElement(NavElements.A, new XAttribute("href", file.FileName), title)));

            format.Ncx?.NavMap.NavPoints.Add(new NcxNavPoint
            {
                Id = Guid.NewGuid().ToString("N"),
                NavLabelText = title,
                ContentSrc = file.FileName,
                PlayOrder = format.Ncx.NavMap.NavPoints.Any() ? format.Ncx.NavMap.NavPoints.Max(e => e.PlayOrder) : 1
            });

            return new EpubChapter
            {
                Title = title,
                FileName = file.FileName
            };
        }

        public void ClearChapters()
        {
            var spineItems = format.Opf.Spine.ItemRefs.Select(spine => format.Opf.Manifest.Items.Single(e => e.Id == spine.IdRef));
            var otherItems = format.Opf.Manifest.Items.Where(e => !spineItems.Contains(e)).ToList();

            foreach (var item in spineItems)
            {
                var href = new Href(item.Href);
                if (otherItems.All(e => new Href(e.Href).Filename != href.Filename))
                {
                    // The HTML file is not referenced by anything outside spine item, thus can be removed from the archive.
                    var file = resources.Html.Single(e => e.FileName == href.Filename);
                    resources.Html.Remove(file);
                }

                format.Opf.Manifest.Items.Remove(item);
            }

            format.Opf.Spine.ItemRefs.Clear();
            format.Opf.Guide = null;
            format.Ncx?.NavMap.NavPoints.Clear();
            FindNavTocOl()?.Descendants().Remove();

            // Remove all images except the cover.
            // I can't think of a case where this is a bad idea.
            var coverPath = format.Opf.FindCoverPath();
            foreach (var item in format.Opf.Manifest.Items.Where(e => e.MediaType.StartsWith("image/") && e.Href != coverPath).ToList())
            {
                format.Opf.Manifest.Items.Remove(item);

                var image = resources.Images.Single(e => e.FileName == new Href(item.Href).Filename);
                resources.Images.Remove(image);
            }
        }

        //public void InsertChapter(string title, string html, int index, EpubChapter parent = null)
        //{
        //    throw new NotImplementedException("Implement me!");
        //}

        public void RemoveCover()
        {
            var path = format.Opf.FindAndRemoveCover();
            if (path == null) return;

            var resource = resources.Images.SingleOrDefault(e => e.FileName == path);
            if (resource != null)
            {
                resources.Images.Remove(resource);
            }
        }

        public void SetCover(byte[] data, ImageFormat imageFormat)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            RemoveCover();

            string filename;
            EpubContentType type;

            switch (imageFormat)
            {
                case ImageFormat.Gif:
                    filename = "cover.gif";
                    type = EpubContentType.ImageGif;
                    break;
                case ImageFormat.Jpeg:
                    filename = "cover.jpeg";
                    type = EpubContentType.ImageJpeg;
                    break;
                case ImageFormat.Png:
                    filename = "cover.png";
                    type = EpubContentType.ImagePng;
                    break;
                case ImageFormat.Svg:
                    filename = "cover.svg";
                    type = EpubContentType.ImageSvg;
                    break;
                default:
                    throw new ArgumentException($"Unsupported cover format: {format}", nameof(format));
            }

            var coverResource = new EpubByteFile
            {
                Content = data,
                FileName = filename,
                ContentType = type
            };
            coverResource.MimeType = ContentType.ContentTypeToMimeType[coverResource.ContentType];
            resources.Images.Add(coverResource);

            var coverItem = new OpfManifestItem
            {
                Id = OpfManifest.ManifestItemCoverImageProperty,
                Href = coverResource.FileName,
                MediaType = coverResource.MimeType
            };
            coverItem.Properties.Add(OpfManifest.ManifestItemCoverImageProperty);
            format.Opf.Manifest.Items.Add(coverItem);
        }
        
        public void Write(string filename)
        {
            using (var file = File.Create(filename))
            {
                Write(file);
            }
        }

        public void Write(Stream stream)
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                archive.CreateEntry("mimetype", MimeTypeWriter.Format());
                archive.CreateEntry(Constants.OcfPath, OcfWriter.Format(opfPath));
                archive.CreateEntry(opfPath, OpfWriter.Format(format.Opf));

                if (format.Ncx != null)
                {
                    archive.CreateEntry(ncxPath, NcxWriter.Format(format.Ncx));
                }

                var allFiles = new[]
                {
                    resources.Html.Cast<EpubFile>(),
                    resources.Css,
                    resources.Images,
                    resources.Fonts,
                    resources.Other
                }.SelectMany(collection => collection as EpubFile[] ?? collection.ToArray());
                var relativePath = PathExt.GetDirectoryPath(opfPath);
                foreach (var file in allFiles)
                {
                    var absolutePath = PathExt.Combine(relativePath, file.FileName);
                    archive.CreateEntry(absolutePath, file.Content);
                }
            }
        }

        private XElement FindNavTocOl()
        {
            if (format.Nav == null)
            {
                return null;
            }

            var ns = format.Nav.Body.Dom.Name.Namespace;
            var element = format.Nav.Body.Dom.Descendants(ns + NavElements.Nav)
                .SingleOrDefault(e => (string)e.Attribute(NavNav.Attributes.Type) == NavNav.Attributes.TypeValues.Toc)
                ?.Element(ns + NavElements.Ol);

            if (element == null)
            {
                throw new EpubWriteException(@"Missing ol: <nav type=""toc""><ol/></nav>");
            }

            return element;
        }
        
        // Old code to add toc.ncx
        /*
            if (opf.Spine.Toc != null)
            {
                var ncxPath = opf.FindNcxPath();
                if (ncxPath == null)
                {
                    throw new EpubWriteException("Spine TOC is set, but NCX path is not.");
                }
                manifest.Add(new XElement(OpfElements.Item, new XAttribute(OpfManifestItem.Attributes.Id, "ncx"), new XAttribute(OpfManifestItem.Attributes.MediaType, ContentType.ContentTypeToMimeType[EpubContentType.DtbookNcx]), new XAttribute(OpfManifestItem.Attributes.Href, ncxPath)));
            }
         */
    }
}
