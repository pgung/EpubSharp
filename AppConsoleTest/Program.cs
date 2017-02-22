using EpubSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AppConsoleTest
{
    public class Program
    {
        public static void Main(string[] args)
        {

            //var filePath = @"C:\ebooks\afrique_du_sud_2016_carnet_petit_fute.epub";
            //var epubBook = EpubReader.Read(filePath, null);

            var filePath = @"C:\ebooks\all_in_3_under_the_gun.epub";
            var epubBook = EpubReader.Read(filePath, null);
            var test = epubBook.GetTocLinks();
        }
    }
}
