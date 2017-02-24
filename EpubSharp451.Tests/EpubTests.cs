﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EpubSharp.Format;
using NUnit.Framework;

namespace EpubSharp.Tests
{
    [TestFixture]
    public class EpubTests
    {
        [Test]
        public void ReadWriteEpub30Test()
        {
            var archives = Utils.ZipAndCopyEpubs(@"Samples/epub30");
            ReadWriteTest(archives);
        }

        [Test]
        public void ReadWriteEpub31Test()
        {
            var archives = Utils.ZipAndCopyEpubs(@"Samples/epub31");
            ReadWriteTest(archives);
        }

        [Test]
        public void ReadWriteEpubAssortedTest()
        {
            var archives = Utils.ZipAndCopyEpubs(@"Samples/epub-assorted");
            ReadWriteTest(archives);
        }

        private void ReadWriteTest(List<string> archives)
        {
            foreach (var archive in archives)
            {
                var originalEpub = EpubReader.Read(archive, null);

                var stream = new MemoryStream();
                EpubWriter.Write(originalEpub, stream);
                stream.Seek(0, SeekOrigin.Begin);
                var savedEpub = EpubReader.Read(stream, false, null);

                AssertEpub(originalEpub, savedEpub);
            }
        }

        private void AssertEpub(EpubBook expected, EpubBook actual)
        {
            Assert.IsNotNull(expected);
            Assert.IsNotNull(actual);

            Assert.AreEqual(expected.Title, actual.Title);

            Assert.AreEqual(expected.Author, actual.Author);
            AssertPrimitiveCollection(expected.Authors, actual.Authors, nameof(actual.Authors), "Author");

            Assert.AreEqual(expected.CoverImage == null, actual.CoverImage == null, nameof(actual.CoverImage));
            if (expected.CoverImage != null && actual.CoverImage != null)
            {
                Assert.IsTrue(expected.CoverImage.Length > 0, "Expected CoverImage.Length > 0");
                Assert.IsTrue(actual.CoverImage.Length > 0, "Actual CoverImage.Length > 0");
                Assert.AreEqual(expected.CoverImage.Length, actual.CoverImage.Length, "CoverImage.Length");
            }

            AssertContentFileCollection(expected.Resources.Css, actual.Resources.Css, nameof(actual.Resources.Css));
            AssertContentFileCollection(expected.Resources.Fonts, actual.Resources.Fonts, nameof(actual.Resources.Fonts));
            AssertContentFileCollection(expected.Resources.Html, actual.Resources.Html, nameof(actual.Resources.Html));
            AssertContentFileCollection(expected.Resources.Images, actual.Resources.Images, nameof(actual.Resources.Images));           
            AssertCollection(expected.SpecialResources.HtmlInReadingOrder, actual.SpecialResources.HtmlInReadingOrder, nameof(actual.SpecialResources.HtmlInReadingOrder), (old, @new) =>
            {
                AssertContentFile(old, @new, nameof(actual.SpecialResources.HtmlInReadingOrder));
            });

            AssertCollection(expected.TableOfContents.EpubChapters, actual.TableOfContents.EpubChapters, nameof(actual.TableOfContents), AssertChapter);

            AssertOcf(expected.Format.Ocf, actual.Format.Ocf);
            AssertOpf(expected.Format.Opf, actual.Format.Opf);
            AssertNcx(expected.Format.Ncx, actual.Format.Ncx);
            AssertNav(expected.Format.Nav, actual.Format.Nav);
        }

        private void AssertCollectionWithIndex<T>(IEnumerable<T> expected, IEnumerable<T> actual, string name, Action<List<T>, List<T>, int> assert)
        {
            Assert.AreEqual(expected == null, actual == null, name);
            if (expected != null && actual != null)
            {
                var old = expected.ToList();
                var @new = actual.ToList();

                Assert.AreEqual(old.Count, @new.Count, $"{name}.Count");

                for (var i = 0; i < @new.Count; ++i)
                {
                    assert(old, @new, i);
                }
            }
        }

        private void AssertCollection<T>(IEnumerable<T> expected, IEnumerable<T> actual, string name, Action<T, T> assert)
        {
            AssertCollectionWithIndex(expected, actual, name, (a, b, i) =>
            {
                assert(a[i], b[i]);
            });
        }

        private void AssertPrimitiveCollection<T>(IEnumerable<T> expected, IEnumerable<T> actual, string collectionName, string unitName)
        {
            AssertCollectionWithIndex(expected, actual, collectionName, (a, b, i) =>
            {
                Assert.IsTrue(a.Contains(b[i]), unitName);
            });
        }

        private void AssertContentFileCollection<TContent>(Dictionary<string, TContent> expected, Dictionary<string, TContent> actual, string collectionName)
            where TContent : EpubFile
        {
            AssertCollection(expected, actual, collectionName, (a, b) =>
            {
                Assert.AreEqual(a.Key, b.Key, $"{collectionName}.Key");
                AssertContentFile(a.Value, b.Value, collectionName);
            });
        }

        private void AssertContentFileCollection<TContent>(IEnumerable<TContent> expected, IEnumerable<TContent> actual, string collectionName)
            where TContent : EpubFile
        {
            AssertCollection(expected, actual, collectionName, (a, b) =>
            {
                AssertContentFile(a, b, collectionName);
            });
        }

        private void AssertContentFile(EpubFile expected, EpubFile actual, string name)
        {
            Assert.IsTrue(expected.Content.SequenceEqual(actual.Content), $"{name}.Content");
            Assert.AreEqual(expected.ContentType, actual.ContentType, $"{name}.ContentType");
            Assert.AreEqual(expected.FileName, actual.FileName, $"{name}.FileName");
            Assert.AreEqual(expected.MimeType, actual.MimeType, $"{name}.MimeType");

            var castedOld = expected as EpubTextFile;
            var castedNew = actual as EpubTextFile;
            Assert.AreEqual(castedOld == null, castedNew == null);
            if (castedOld != null && castedNew != null)
            {
                Assert.AreEqual(castedOld.TextContent, castedNew.TextContent, $"{name}.TextContent");
            }
        }

        private void AssertChapter(EpubChapter expected, EpubChapter actual)
        {
            Assert.AreEqual(expected.Anchor, actual.Anchor);
            Assert.AreEqual(expected.FileName, actual.FileName);
            Assert.AreEqual(expected.Title, actual.Title);

            Assert.AreEqual(expected.SubChapters.Count, actual.SubChapters.Count);
            for (var i = 0; i < expected.SubChapters.Count; ++i)
            {
                AssertChapter(expected.SubChapters[i], actual.SubChapters[i]);
            }
        }

        private void AssertOcf(OcfDocument expected, OcfDocument actual)
        {
            // There are some epubs with multiple root files.
            // i.e. 1 normal and 1 for braille.
            // We don't have multiple root file support, therefore Take(1) for now.
            // Currently it is also assumed that the first root file is the main root file.
            // This is a dangerous assumption.
            AssertCollection(expected.RootFiles.Take(1), actual.RootFiles, nameof(actual.RootFiles), (a, b) =>
            {
                Assert.AreEqual(a.FullPath, b.FullPath, nameof(b.FullPath));
                Assert.AreEqual(a.MediaType, b.MediaType, nameof(b.MediaType));
            });
            Assert.AreEqual(expected.RootFilePath, actual.RootFilePath);
        }

        private void AssertOpf(OpfDocument expected, OpfDocument actual)
        {
            Assert.AreEqual(expected == null, actual == null, nameof(actual));
            if (expected != null && actual != null)
            {
                Assert.AreEqual(expected.EpubVersion, actual.EpubVersion, nameof(actual.EpubVersion));

                Assert.AreEqual(expected.Metadata == null, actual.Metadata == null, nameof(actual.Metadata));
                if (expected.Metadata != null && actual.Metadata != null)
                {
                    AssertCreators(expected.Metadata.Creators, actual.Metadata.Creators, nameof(actual.Metadata.Creators));
                    AssertCreators(expected.Metadata.Contributors, actual.Metadata.Contributors, nameof(actual.Metadata.Contributors));

                    AssertCollection(expected.Metadata.Dates, actual.Metadata.Dates, nameof(actual.Metadata.Dates), (a, b) =>
                    {
                        Assert.AreEqual(a.Text, b.Text, "Date.Text");
                        Assert.AreEqual(a.Event, b.Event, "Date.Event");
                    });

                    AssertCollection(expected.Metadata.Identifiers, actual.Metadata.Identifiers, nameof(actual.Metadata.Identifiers), (a, b) =>
                    {
                        Assert.AreEqual(a.Id, b.Id, "Identifier.Id");
                        Assert.AreEqual(a.Scheme, b.Scheme, "Identifier.Scheme");
                        Assert.AreEqual(a.Text, b.Text, "Identifier.Text");
                    });

                    AssertCollection(expected.Metadata.Metas, actual.Metadata.Metas, nameof(actual.Metadata.Metas), (a, b) =>
                    {
                        Assert.AreEqual(a.Id, b.Id, "Meta.Id");
                        Assert.AreEqual(a.Name, b.Name, "Meta.Name");
                        Assert.AreEqual(a.Property, b.Property, "Meta.Property");
                        Assert.AreEqual(a.Refines, b.Refines, "Meta.Refines");
                        Assert.AreEqual(a.Scheme, b.Scheme, "Meta.Scheme");
                        Assert.AreEqual(a.Text, b.Text, "Meta.Text");
                    });

                    AssertPrimitiveCollection(expected.Metadata.Coverages, actual.Metadata.Coverages, "Coverages", "Coverage");
                    AssertPrimitiveCollection(expected.Metadata.Descriptions, actual.Metadata.Descriptions, "Descriptions", "Description");
                    AssertPrimitiveCollection(expected.Metadata.Languages, actual.Metadata.Languages, "Languages", "Language");
                    AssertPrimitiveCollection(expected.Metadata.Publishers, actual.Metadata.Publishers, "Publishers", "Publisher");
                    AssertPrimitiveCollection(expected.Metadata.Relations, actual.Metadata.Relations, "Relations", "Relation");
                    AssertPrimitiveCollection(expected.Metadata.Rights, actual.Metadata.Rights, "Rights", "Right");
                    AssertPrimitiveCollection(expected.Metadata.Sources, actual.Metadata.Sources, "Sources", "Source");
                    AssertPrimitiveCollection(expected.Metadata.Subjects, actual.Metadata.Subjects, "Subjects", "Subject");
                    AssertPrimitiveCollection(expected.Metadata.Titles, actual.Metadata.Titles, "Titles", "Title");
                    AssertPrimitiveCollection(expected.Metadata.Types, actual.Metadata.Types, "Types", "Type");
                }

                Assert.AreEqual(expected.Guide == null, actual.Guide == null, nameof(actual.Guide));
                if (expected.Guide != null && actual.Guide != null)
                {
                    AssertCollection(expected.Guide.References, actual.Guide.References, nameof(actual.Guide.References), (a, b) =>
                    {
                        Assert.AreEqual(a.Title, b.Title, "Reference.Title");
                        Assert.AreEqual(a.Type, b.Type, "Reference.Type");
                        Assert.AreEqual(a.Href, b.Href, "Reference.Href");
                    });
                }

                Assert.AreEqual(expected.Manifest == null, actual.Manifest == null, nameof(actual.Manifest));
                if (expected.Manifest != null && actual.Manifest != null)
                {
                    AssertCollection(expected.Manifest.Items, actual.Manifest.Items, nameof(actual.Manifest.Items), (a, b) =>
                    {
                        Assert.AreEqual(a.Fallback, b.Fallback, "Item.Fallback");
                        Assert.AreEqual(a.FallbackStyle, b.FallbackStyle, "Item.FallbackStyle");
                        Assert.AreEqual(a.Href, b.Href, "Item.Href");
                        Assert.AreEqual(a.Id, b.Id, "Item.Id");
                        Assert.AreEqual(a.MediaType, b.MediaType, "Item.MediaType");
                        Assert.AreEqual(a.RequiredModules, b.RequiredModules, "Item.RequiredModules");
                        Assert.AreEqual(a.RequiredNamespace, b.RequiredNamespace, "Item.RequiredNamespace");
                        AssertPrimitiveCollection(a.Properties, b.Properties, "Item.Properties", "Item.Property");
                    });
                }

                Assert.AreEqual(expected.Spine == null, actual.Spine == null, nameof(actual.Spine));
                if (expected.Spine != null && actual.Spine != null)
                {
                    Assert.AreEqual(expected.Spine.Toc, actual.Spine.Toc, nameof(actual.Spine.Toc));
                    AssertCollection(expected.Spine.ItemRefs, actual.Spine.ItemRefs, nameof(actual.Spine.ItemRefs), (a, b) =>
                    {
                        Assert.AreEqual(a.Id, b.Id, "ItemRef.Id");
                        Assert.AreEqual(a.IdRef, b.IdRef, "ItemRef.IdRef");
                        Assert.AreEqual(a.Linear, b.Linear, "ItemRef.Linear");
                        AssertPrimitiveCollection(a.Properties, b.Properties, "ItemRef.Properties", "ItemRef.Property");
                    });
                }
            }
        }

        private void AssertCreators(IEnumerable<OpfMetadataCreator> expected, IEnumerable<OpfMetadataCreator> actual, string name)
        {
            AssertCollection(expected, actual, name, (a, b) =>
            {
                Assert.AreEqual(a.AlternateScript, b.AlternateScript, $"{name}.AlternateScript");
                Assert.AreEqual(a.FileAs, b.FileAs, $"{name}.FileAs");
                Assert.AreEqual(a.Role, b.Role, $"{name}.Role");
                Assert.AreEqual(a.Text, b.Text, $"{name}.Text");
            });
        }

        private void AssertNcx(NcxDocument expected, NcxDocument actual)
        {
            Assert.AreEqual(expected == null, actual == null, nameof(actual));
            if (expected != null && actual != null)
            {
                Assert.AreEqual(expected.DocAuthor, actual.DocAuthor, nameof(actual.DocAuthor));
                Assert.AreEqual(expected.DocTitle, actual.DocTitle, nameof(actual.DocTitle));

                AssertCollection(expected.Meta, actual.Meta, nameof(actual.Meta), (a, b) =>
                {
                    Assert.AreEqual(a.Name, b.Name, "Metadata.Name");
                    Assert.AreEqual(a.Content, b.Content, "Metadata.Content");
                    Assert.AreEqual(a.Scheme, b.Scheme, "Metadata.Scheme");
                });

                Assert.AreEqual(expected.NavList == null, actual.NavList == null, "NavigationList");
                if (expected.NavList != null && actual.NavList != null)
                {
                    Assert.AreEqual(expected.NavList.Id, actual.NavList.Id, "NavigationList.Id");
                    Assert.AreEqual(expected.NavList.Class, actual.NavList.Class, "NavigationList.Class");
                    Assert.AreEqual(expected.NavList.Label, actual.NavList.Label, "NavigationList.Label");

                    AssertCollection(expected.NavList.NavTargets, actual.NavList.NavTargets, nameof(actual.NavList.NavTargets), (a, b) =>
                    {
                        Assert.AreEqual(a.Id, b.Id, "NavigationTarget.Id");
                        Assert.AreEqual(a.Class, b.Class, "NavigationTarget.Class");
                        Assert.AreEqual(a.Label, b.Label, "NavigationTarget.Label");
                        Assert.AreEqual(a.PlayOrder, b.PlayOrder, "NavigationTarget.PlayOrder");
                        Assert.AreEqual(a.ContentSource, b.ContentSource, "NavigationTarget.ContentSrc");
                    });
                }

                AssertCollection(expected.NavMap.NavPoints, actual.NavMap.NavPoints, nameof(actual.NavMap), (a, b) =>
                {
                    Assert.AreEqual(a.Id, b.Id, "NavigationMap.Id");
                    Assert.AreEqual(a.PlayOrder, b.PlayOrder, "NavigationMap.PlayOrder");
                    Assert.AreEqual(a.NavLabelText, b.NavLabelText, "NavigationMap.NavLabelText");
                    Assert.AreEqual(a.Class, b.Class, "NavigationMap.Class");
                    Assert.AreEqual(a.ContentSrc, b.ContentSrc, "NavigationMap.ContentSorce");
                    AssertNavigationPoints(a.NavPoints, b.NavPoints);
                });

                Assert.AreEqual(expected.PageList == null, actual.PageList == null, nameof(actual.PageList));
                if (expected.PageList != null && actual.PageList != null)
                {
                    AssertCollection(expected.PageList.PageTargets, actual.PageList.PageTargets, nameof(actual.PageList.PageTargets), (a, b) =>
                    {
                        Assert.AreEqual(a.Id, b.Id, "PageList.Id");
                        Assert.AreEqual(a.Class, b.Class, "PageList.Class");
                        Assert.AreEqual(a.ContentSrc, b.ContentSrc, "PageList.ContentSrc");
                        Assert.AreEqual(a.NavLabelText, b.NavLabelText, "PageList.Label");
                        Assert.AreEqual(a.Type, b.Type, "PageList.Type");
                        Assert.AreEqual(a.Value, b.Value, "PageList.Value");
                    });
                }
            }
        }

        private void AssertNavigationPoints(IEnumerable<NcxNavPoint> expected, IEnumerable<NcxNavPoint> actual)
        {
            AssertCollection(expected, actual, "NavigationPoint", (a, b) =>
            {
                Assert.AreEqual(a.Id, b.Id, "NavigationPoint.Id");
                Assert.AreEqual(a.Class, b.Class, "NavigationPoint.Class");
                Assert.AreEqual(a.ContentSrc, b.ContentSrc, "NavigationPoint.ContentSrc");
                Assert.AreEqual(a.NavLabelText, b.NavLabelText, "NavigationPoint.NavLabelText");
                Assert.AreEqual(a.PlayOrder, b.PlayOrder, "NavigationPoint.PlayOrder");
                Assert.AreEqual(a.NavPoints == null, b.NavPoints == null, "NavigationPoint.NavPoints");
                if (a.NavPoints != null && b.NavPoints != null)
                {
                    AssertNavigationPoints(a.NavPoints, b.NavPoints);
                }
            });
        }

        private void AssertNav(NavDocument expected, NavDocument actual)
        {
            Assert.AreEqual(expected == null, actual == null, nameof(actual));
            if (expected != null && actual != null)
            {
                Assert.AreEqual(expected.Head == null, actual.Head == null, nameof(actual.Head));
                if (expected.Head != null && actual.Head != null)
                {
                    Assert.AreEqual(expected.Head.Title, actual.Head.Title);
                    AssertCollection(expected.Head.Links, actual.Head.Links, nameof(actual.Head.Links), (a, b) =>
                    {
                        Assert.AreEqual(a.Class, b.Class, "Link.Class");
                        Assert.AreEqual(a.Href, b.Href, "Link.Href");
                        Assert.AreEqual(a.Rel, b.Rel, "Link.Rel");
                        Assert.AreEqual(a.Title, b.Title, "Link.Title");
                        Assert.AreEqual(a.Type, b.Type, "Link.Type");
                        Assert.AreEqual(a.Media, b.Media, "Link.Media");
                    });

                    AssertCollection(expected.Head.Metas, actual.Head.Metas, nameof(actual.Head.Metas), (a, b) =>
                    {
                        Assert.AreEqual(a.Charset, b.Charset, "Meta.Charset");
                        Assert.AreEqual(a.Name, b.Name, "Meta.Name");
                        Assert.AreEqual(a.Content, b.Content, "Meta.Content");
                    });
                }

                Assert.AreEqual(expected.Body == null, actual.Body == null, nameof(actual.Body));
                if (expected.Body != null && actual.Body != null)
                {
                    AssertCollection(expected.Body.Navs, actual.Body.Navs, nameof(actual.Body.Navs), (a, b) =>
                    {
                        Assert.AreEqual(a.Class, b.Class, "Nav.Class");
                        Assert.AreEqual(a.Hidden, b.Hidden, "Nav.Hidden");
                        Assert.AreEqual(a.Id, b.Id, "Nav.Id");
                        Assert.AreEqual(a.Type, b.Type, "Nav.Type");
                    });
                }
            }
        }
    }
}
