using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using EpubSharp.Format;
using EpubSharp.Format.Readers;
using Ionic.Zip;
using System.Xml;
using System.Text;

namespace EpubSharp
{
    public static class EpubReader
    {
        public static EpubBook Read(string filePath, string password)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Specified epub file not found.", filePath);
            }
            using (var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read))
            {
                return Read(fileStream, false, password);
            }                
        }

        public static EpubBook Read(byte[] epubData, string password)
        {
            return Read(new MemoryStream(epubData), false, password);
        }

        


        public static EpubBook Read(Stream stream, bool leaveOpen, string password)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using (var archive = ZipFile.Read(stream))
            {
                // OCF
                var entryOCF = archive.Entries.SingleOrDefault(entry => entry.FileName.Equals(Constants.OcfPath));
                if (entryOCF == null)
                {
                    throw new EpubParseException("Epub OCF doesn't specify a root file.");
                }

                var textOCF = GetText(entryOCF, password);
                var format = new EpubFormat { Ocf = OcfReader.Read(XDocument.Parse(textOCF)) };
                
                var rootFilePath = format.Ocf.RootFilePath;
                if (rootFilePath == null)
                {
                    throw new EpubParseException("Epub OCF doesn't specify a root file.");
                }
                
                // OPF
                var entryOPF = archive.Entries.SingleOrDefault(entry => entry.FileName.Equals(rootFilePath));
                if (entryOPF == null)
                {
                    throw new EpubParseException("Epub OPF doesn't specify a root file.");
                }
                var textOPF = GetText(entryOPF, password);                

                format.Opf = OpfReader.Read(XDocument.Parse(textOPF));


                // Nav
                var navPath = format.Opf.FindNavPath();
                if (navPath != null)
                {
                    var absolutePath = PathExt.Combine(PathExt.GetDirectoryPath(rootFilePath), navPath);
                    var entryNav = archive.Entries.SingleOrDefault(entry => entry.FileName.Equals(absolutePath));
                    if (entryNav != null)
                    {
                        var textNav = GetText(entryNav, password);
                        format.Nav = NavReader.Read(XDocument.Parse(textNav));
                    }                    
                }

                // Ncx
                var ncxPath = format.Opf.FindNcxPath();
                if (ncxPath != null)
                {
                    var absolutePath = PathExt.Combine(PathExt.GetDirectoryPath(rootFilePath), ncxPath);

                    var entryNcx = archive.Entries.SingleOrDefault(entry => entry.FileName.Equals(absolutePath));
                    if (entryNcx != null)
                    {
                        var textNcx = GetText(entryNcx, password);

                        format.Ncx = NcxReader.Read(XDocument.Parse(textNcx));
                    }                    
                }

                var book = new EpubBook { Format = format };
                book.Resources = LoadResources(archive, book, password);
                book.SpecialResources = LoadSpecialResources(archive, book, password);
                book.CoverImage = LoadCoverImage(book);
                book.TableOfContents = new TableOfContents { EpubChapters = LoadChapters(book) };
                return book;
            }
        }

        private static byte[] LoadCoverImage(EpubBook book)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (book.Format == null) throw new ArgumentNullException(nameof(book.Format));

            var coverPath = book.Format.Opf.FindCoverPath();
            if (coverPath == null)
            {
                return null;
            }

            var coverImageFile = book.Resources.Images.SingleOrDefault(e => e.FileName == coverPath);
            return coverImageFile?.Content;
        }

        private static List<EpubChapter> LoadChapters(EpubBook book)
        {
            if (book.Format.Nav != null)
            {
                var tocNav = book.Format.Nav.Body.Navs.SingleOrDefault(e => e.Type == NavNav.Attributes.TypeValues.Toc);
                if (tocNav != null)
                {
                    return LoadChaptersFromNav(tocNav.Dom);
                }
            }

            if (book.Format.Ncx != null)
            {
                return LoadChaptersFromNcx(book.Format.Ncx.NavMap.NavPoints);
            }

            return new List<EpubChapter>();
        }

        private static List<EpubChapter> LoadChaptersFromNav(XElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            var ns = element.Name.Namespace;

            var result = new List<EpubChapter>();

            var ol = element.Element(ns + NavElements.Ol);
            if (ol == null)
            {
                return result;
            }

            foreach (var li in ol.Elements(ns + NavElements.Li))
            {
                var chapter = new EpubChapter();

                var link = li.Element(ns + NavElements.A);
                if (link != null)
                {
                    var url = link.Attribute("href")?.Value;
                    if (url != null)
                    {
                        chapter.ContentSrc = url;
                        var href = new Href(url);
                        chapter.FileName = href.Filename;
                        chapter.Anchor = href.Anchor;
                    }

                    var titleTextElement = li.Descendants().FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Value));
                    if (titleTextElement != null)
                    {
                        chapter.Title = titleTextElement.Value;
                    }

                    if (li.Element(ns + NavElements.Ol) != null)
                    {
                        chapter.SubChapters = LoadChaptersFromNav(li);
                    }
                    result.Add(chapter);
                }
            }

            return result;
        }

        private static List<EpubChapter> LoadChaptersFromNcx(IEnumerable<NcxNavPoint> navigationPoints)
        {
            var result = new List<EpubChapter>();
            foreach (var navigationPoint in navigationPoints)
            {
                var chapter = new EpubChapter { Title = navigationPoint.NavLabelText };
                chapter.Id = navigationPoint.Id;
                chapter.ContentSrc = navigationPoint.ContentSrc;
                var href = new Href(navigationPoint.ContentSrc);
                chapter.FileName = href.Filename;
                chapter.Anchor = href.Anchor;
                chapter.SubChapters = LoadChaptersFromNcx(navigationPoint.NavPoints);
                result.Add(chapter);
            }
            return result;
        }

        private static EpubResources LoadResources(ZipFile epubArchive, EpubBook book, string password)
        {
            var resources = new EpubResources();

            foreach (var item in book.Format.Opf.Manifest.Items)
            {
                var path = PathExt.Combine(Path.GetDirectoryName(book.Format.Ocf.RootFilePath), item.Href);
                var entry = epubArchive.Entries.FirstOrDefault(x => x.FileName.Equals(path));

                if (entry == null)
                {
                    throw new EpubParseException($"file {path} not found in archive.");
                }
                if (entry.UncompressedSize > int.MaxValue)
                {
                    throw new EpubParseException($"file {path} is bigger than 2 Gb.");
                }

                var fileName = item.Href;
                var mimeType = item.MediaType;

                EpubContentType contentType;
                contentType = ContentType.MimeTypeToContentType.TryGetValue(mimeType, out contentType)
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
                            var file = new EpubTextFile
                            {
                                FileName = fileName,
                                MimeType = mimeType,
                                ContentType = contentType
                            };


                            using (var stream = GetMemoryStream(entry, password))
                            {
                                file.Content = stream.ReadToEnd();
                            }

                            switch (contentType)
                            {
                                case EpubContentType.Xhtml11:
                                    resources.Html.Add(file);
                                    break;
                                case EpubContentType.Css:
                                    resources.Css.Add(file);
                                    break;
                                default:
                                    resources.Other.Add(file);
                                    break;
                            }
                            break;
                        }
                    default:
                        {
                            var file = new EpubByteFile
                            {
                                FileName = fileName,
                                MimeType = mimeType,
                                ContentType = contentType
                            };

                            using (var stream = GetMemoryStream(entry, password))
                            {
                                if (stream == null)
                                {
                                    throw new EpubException($"Incorrect EPUB file: content file \"{fileName}\" specified in manifest is not found");
                                }

                                using (var memoryStream = new MemoryStream())
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
                                    resources.Images.Add(file);
                                    break;
                                case EpubContentType.FontTruetype:
                                case EpubContentType.FontOpentype:
                                    resources.Fonts.Add(file);
                                    break;
                                default:
                                    resources.Other.Add(file);
                                    break;
                            }
                            break;
                        }
                }
            }

            return resources;
        }

        private static MemoryStream GetMemoryStream(ZipEntry entry, string password)
        {
            var stream = new MemoryStream();
            if (string.IsNullOrEmpty(password))
                entry.Extract(stream);
            else
                entry.ExtractWithPassword(stream, password);
            return stream;
        }

        private static EpubSpecialResources LoadSpecialResources(ZipFile epubArchive, EpubBook book, string password)
        {
            var entryOcf = epubArchive.Entries.FirstOrDefault(x => x.FileName.Equals(Constants.OcfPath));
            var entryOpf = epubArchive.Entries.FirstOrDefault(x => x.FileName.Equals(book.Format.Ocf.RootFilePath));

            var result = new EpubSpecialResources
            {
                Ocf = new EpubTextFile
                {
                    FileName = Constants.OcfPath,
                    ContentType = EpubContentType.Xml,
                    MimeType = ContentType.ContentTypeToMimeType[EpubContentType.Xml],
                    Content = GetExtract(entryOcf, password)
                },
                Opf = new EpubTextFile
                {
                    FileName = book.Format.Ocf.RootFilePath,
                    ContentType = EpubContentType.Xml,
                    MimeType = ContentType.ContentTypeToMimeType[EpubContentType.Xml],
                    Content = GetExtract(entryOpf, password)
                },
                HtmlInReadingOrder = new List<EpubTextFile>()
            };

            var htmlFiles = book.Format.Opf.Manifest.Items
                .Where(item => ContentType.MimeTypeToContentType.ContainsKey(item.MediaType) && ContentType.MimeTypeToContentType[item.MediaType] == EpubContentType.Xhtml11)
                .ToDictionary(item => item.Id, item => item.Href);

            foreach (var item in book.Format.Opf.Spine.ItemRefs)
            {
                string href;
                if (!htmlFiles.TryGetValue(item.IdRef, out href))
                {
                    continue;
                }

                var html = book.Resources.Html.SingleOrDefault(e => e.FileName == href);
                if (html != null)
                {
                    result.HtmlInReadingOrder.Add(html);
                }
            }

            return result;
        }


        private static byte[] GetExtract(ZipEntry entry, string password)
        {
            if (entry == null) return null;

            byte[] output;
            using (var outputStream = GetMemoryStream(entry, password))
            {
                outputStream.Flush();
                outputStream.Position = 0;
                output = outputStream.ToArray();
            }

            return output;
        }
        private static string GetText(ZipEntry entry, string password)
        {
            return Encoding.UTF8.GetString(GetExtract(entry, password));
        }
    }
}
