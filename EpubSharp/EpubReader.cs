﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using EpubSharp.Format;
using EpubSharp.Format.Readers;

namespace EpubSharp
{
    public static class EpubReader
    {
        internal const string OcfPath = "META-INF/container.xml";

        private static readonly IReadOnlyDictionary<string, EpubContentType> MimeTypeToContentType = new Dictionary<string, EpubContentType>
        {
            { "application/xhtml+xml", EpubContentType.Xhtml11 },
            { "application/x-dtbook+xml", EpubContentType.Dtbook },
            { "application/x-dtbncx+xml", EpubContentType.DtbookNcx },
            { "text/x-oeb1-document", EpubContentType.Oeb1Document },
            { "application/xml", EpubContentType.Xml },
            { "text/css", EpubContentType.Css },
            { "text/x-oeb1-css", EpubContentType.Oeb1Css },
            { "image/gif", EpubContentType.ImageGif },
            { "image/jpeg", EpubContentType.ImageJpeg },
            { "image/png", EpubContentType.ImagePng },
            { "image/svg+xml", EpubContentType.ImageSvg },
            { "font/truetype", EpubContentType.FontTruetype },
            { "font/opentype", EpubContentType.FontOpentype },
            { "application/vnd.ms-opentype", EpubContentType.FontOpentype }
        };
        private static readonly IReadOnlyDictionary<EpubContentType, string> ContentTypeToMimeType = MimeTypeToContentType
            .Where(pair => pair.Key != "application/vnd.ms-opentype") // Because it's defined twice.
            .ToDictionary(pair => pair.Value, pair => pair.Key);


        public static EpubBook Read(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Specified epub file not found.", filePath);
            }

            using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Read, System.Text.Encoding.UTF8))
            {
                var format = new EpubFormat();
                format.Ocf = OcfReader.Read(archive.LoadXml(OcfPath));
                format.Package = PackageReader.Read(archive.LoadXml(format.Ocf.RootFile));

                // TODO: Implement epub 3.0 nav support and load ncx only if nav is not present.
                if (!string.IsNullOrWhiteSpace(format.Package.NcxPath))
                {
                    var absolutePath = PathExt.Combine(PathExt.GetDirectoryPath(format.Ocf.RootFile), format.Package.NcxPath);
                    format.Ncx = NcxReader.Read(archive.LoadXml(absolutePath));
                }

                var book = new EpubBook { Format = format };
                book.Resources = LoadResources(archive, book);
                book.LazyCoverImage = LazyLoadCoverImage(book);
                book.TableOfContents = LoadChapters(book, archive);
                return book;
            }
        }

        private static Lazy<Image> LazyLoadCoverImage(EpubBook book)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (book.Format == null) throw new ArgumentNullException(nameof(book.Format));

            return new Lazy<Image>(() =>
            {
                EpubByteContentFile coverImageContentFile;
                if (!book.Resources.Images.TryGetValue(book.Format.Package.CoverPath, out coverImageContentFile))
                {
                    return null;
                }

                using (var coverImageStream = new MemoryStream(coverImageContentFile.Content))
                {
                    return Image.FromStream(coverImageStream);
                }
            });
        }

        private static List<EpubChapter> LoadChapters(EpubBook book, ZipArchive epubArchive)
        {
            if (book.Format.Ncx != null)
            {
                return LoadChaptersFromNcx(book, book.Format.Ncx.NavigationMap, epubArchive);
            }
            
            return new List<EpubChapter>();
        }

        private static List<EpubChapter> LoadChaptersFromNcx(EpubBook book, IReadOnlyCollection<NcxNavigationPoint> navigationPoints, ZipArchive epubArchive)
        {
            var result = new List<EpubChapter>();
            foreach (var navigationPoint in navigationPoints)
            {
                var chapter = new EpubChapter { Title = navigationPoint.LabelText };
                var contentSourceAnchorCharIndex = navigationPoint.ContentSrc.IndexOf('#');
                if (contentSourceAnchorCharIndex == -1)
                {
                    chapter.FileName = navigationPoint.ContentSrc;
                }
                else
                {
                    chapter.FileName = navigationPoint.ContentSrc.Substring(0, contentSourceAnchorCharIndex);
                    chapter.Anchor = navigationPoint.ContentSrc.Substring(contentSourceAnchorCharIndex + 1);
                }

                chapter.SubChapters = LoadChaptersFromNcx(book, navigationPoint.NavigationPoints, epubArchive);
                result.Add(chapter);
            }
            return result;
        }

        public static EpubResources LoadResources(ZipArchive epubArchive, EpubBook book)
        {
            var result = new EpubResources
            {
                Ocf = new EpubTextContentFile
                {
                    FileName = OcfPath,
                    ContentType = EpubContentType.Xml,
                    MimeType = ContentTypeToMimeType[EpubContentType.Xml],
                    Content = epubArchive.LoadBytes(OcfPath)
                },
                Opf = new EpubTextContentFile
                {
                    FileName = book.Format.Ocf.RootFile,
                    ContentType = EpubContentType.Xml,
                    MimeType = ContentTypeToMimeType[EpubContentType.Xml],
                    Content = epubArchive.LoadBytes(book.Format.Ocf.RootFile)
                },
                Html = new Dictionary<string, EpubTextContentFile>(),
                Css = new Dictionary<string, EpubTextContentFile>(),
                Images = new Dictionary<string, EpubByteContentFile>(),
                Fonts = new Dictionary<string, EpubByteContentFile>(),
                AllFiles = new Dictionary<string, EpubContentFile>(),
                HtmlInReadingOrder = new List<EpubTextContentFile>()
            };

            // Saved items for creating reading order from spine.
            var idToHtmlItems = new Dictionary<string, EpubTextContentFile>();

            foreach (var item in book.Format.Package.Manifest.Items)
            {
                var path = PathExt.Combine(Path.GetDirectoryName(book.Format.Ocf.RootFile), item.Href);
                var entry = epubArchive.GetEntryIgnoringSlashDirection(path);

                if (entry == null)
                {
                    throw new EpubParseException($"file {path} not found in archive.");
                }
                if (entry.Length > int.MaxValue)
                {
                    throw new EpubParseException($"file {path} is bigger than 2 Gb.");
                }

                var fileName = item.Href;
                var mimeType = item.MediaType;

                EpubContentType contentType;
                contentType = MimeTypeToContentType.TryGetValue(mimeType, out contentType)
                    ? contentType
                    : EpubContentType.Other;

                switch (contentType)
                {
                    case EpubContentType.Xhtml11:
                    case EpubContentType.Css:
                    case EpubContentType.Oeb1Document:
                    case EpubContentType.Oeb1Css:
                    case EpubContentType.Xml:
                    case EpubContentType.Dtbook:
                    case EpubContentType.DtbookNcx:
                    {
                        var file = new EpubTextContentFile
                        {
                            FileName = fileName,
                            MimeType = mimeType,
                            ContentType = contentType
                        };

                        using (var stream = entry.Open())
                        {
                            file.Content = stream.ReadToEnd();
                        }

                        switch (contentType)
                        {
                            case EpubContentType.Xhtml11:
                                idToHtmlItems.Add(item.Id, file);
                                result.Html.Add(fileName, file);
                                break;
                            case EpubContentType.Css:
                                result.Css.Add(fileName, file);
                                break;
                        }
                        result.AllFiles.Add(fileName, file);
                        break;
                    }
                    default:
                    {
                        var file = new EpubByteContentFile
                        {
                            FileName = fileName,
                            MimeType = mimeType,
                            ContentType = contentType
                        };

                        using (var stream = entry.Open())
                        {
                            if (stream == null)
                            {
                                throw new EpubException($"Incorrect EPUB file: content file \"{fileName}\" specified in manifest is not found");
                            }

                            using (var memoryStream = new MemoryStream((int) entry.Length))
                            {
                                stream.CopyTo(memoryStream);
                                file.Content = memoryStream.ToArray();
                            }
                        }

                        switch (contentType)
                        {
                            case EpubContentType.ImageGif:
                            case EpubContentType.ImageJpeg:
                            case EpubContentType.ImagePng:
                            case EpubContentType.ImageSvg:
                                result.Images.Add(fileName, file);
                                break;
                            case EpubContentType.FontTruetype:
                            case EpubContentType.FontOpentype:
                                result.Fonts.Add(fileName, file);
                                break;
                        }
                        result.AllFiles.Add(fileName, file);
                        break;
                    }
                }
            }

            foreach (var item in book.Format.Package.Spine.ItemRefs)
            {
                EpubTextContentFile html;
                if (idToHtmlItems.TryGetValue(item.IdRef, out html))
                {
                    result.HtmlInReadingOrder.Add(html);
                }
            }

            return result;
        }
    }
}
