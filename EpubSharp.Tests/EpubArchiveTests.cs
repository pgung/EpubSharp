using NUnit.Framework;

namespace EpubSharp.Tests
{
    [TestFixture]
    public class EpubArchiveTests
    {
        [Test]
        public void FindEntryTest()
        {
            var archive = new EpubArchive("Samples/epub-assorted/afrique_du_sud_2016_carnet_petit_fute.epub");
            Assert.NotNull(archive.FindEntry("META-INF/container.xml"));
            Assert.Null(archive.FindEntry("UNEXISTING_ENTRY"));
        }
    }
}
