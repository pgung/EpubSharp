using EpubSharp;

namespace AppConsoleTest
{
    public class Program
    {
        public static void Main(string[] args)
        {

            //var filePath = @"C:\ebooks\afrique_du_sud_2016_carnet_petit_fute.epub";
            //var epubBook = EpubReader.Read(filePath, null);

            var filePath = @"C:\ebooks\20140802-demo.epub";
            var epubBook = EpubReader.Read(filePath, null);
            var test = epubBook.GetTocLinks();
            var prepaginated = epubBook.IsPrePaginated();
            var wordCount = WordCountService.CountWords(epubBook, filePath, string.Empty);
        }
    }
}
