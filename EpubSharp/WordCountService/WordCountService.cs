using EpubSharp.Format;
using Ionic.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace EpubSharp
{
    public class WordCountService
    {
        private static string[] txtExtensions = { ".html", ".htm", ".xhtml", ".xml" };

        public static WordCountItem CountWords(EpubBook epubBook, string fileName, string password)
        {
            var countSpines = new WordCountItem();
            var spin = epubBook.Spine();

            if (spin == null)
                return countSpines;

            var models = spin.Spines(epubBook.Format.Opf.Manifest);
            var counts = CountHtmlWords(fileName, password).ToDictionary(t => t.Item1, t => t.Item2);

            var result = models.Select(x =>
            {
                var packageDir = Path.GetDirectoryName(epubBook.Format.Ocf.RootFilePath);
                var fullPath = "/" + FileHelper.FormatPath(packageDir, x.IdRef);                
                if (counts.ContainsKey(fullPath))
                    return new WordCountItemElement { SpineItemRef = x, Count = counts[fullPath] };
                return new WordCountItemElement { SpineItemRef = x, Count = 0 };
            });
            var wordCountItem = new WordCountItem();
            wordCountItem.AddRange(result);
            return wordCountItem;
        }


        private static IEnumerable<Tuple<string, int>> CountHtmlWords(string fileName, string password)
        {
            using (var zipFile = ZipFile.Read(fileName))
            {
                foreach (var entry in zipFile.Entries)
                {
                    var fullFileName = !entry.FileName.StartsWith("/", StringComparison.CurrentCulture) ? "/" + entry.FileName : entry.FileName;
                    if (txtExtensions.Any(extention => fullFileName.EndsWith(extention, StringComparison.CurrentCulture)))
                    {
                        using (var outputStream = new MemoryStream())
                        {
                            if (string.IsNullOrEmpty(password))
                                entry.Extract(outputStream);
                            else
                                entry.ExtractWithPassword(outputStream, password);

                            var output = outputStream.ReadToEnd();
                            var text = Encoding.UTF8.GetString(output, 0, output.Length);
                            var parts = text.Split(' ', ';', '\r', '\n', '\t', ',', '.', '!', '?');
                            yield return new Tuple<string, int>(fullFileName, parts.Length);
                        }                                
                    }
                }
            }                
        }
    }

    public class WordCountItem : List<WordCountItemElement>, IComparable
    {
        public int CompareTo(object obj)
        {
            return 1;
        }
    }

    public class WordCountItemElement
    {
        public OpfSpineItemRef SpineItemRef { get; internal set; }
        public int Count { get; internal set; }
    }

    public class ReadingState
    {
        public int SpineCount { get; internal set; }
        public WordCountItem WordCount { get; internal set; }
        public bool PrePaginated { get; internal set; }
        public int SpinePageCount { get; internal set; }
        public int PageIndex { get; internal set; }
        public int SpineIndex { get; set; }

        public ReadingState(int spineCount, int spinePageCount, int pageIndex, int spineIndex, WordCountItem wordCount, bool prePaginated)
        { SpineCount = spineCount; SpinePageCount = spinePageCount; PageIndex = pageIndex; SpineIndex = spineIndex; WordCount = wordCount; PrePaginated = prePaginated; }

        public int Progress()
        {
            if (PrePaginated)
                return SpineIndex;
            if (SpineIndex == 0 && PageIndex == 0)
                return 0;

            var totalWordCount = WordCount.Sum(x => x.Count);
            var pastWords = SpineIndex == 0 ? 0 : WordCount.Take(SpineIndex).Sum(x => x.Count);
            var count = WordCount[SpineIndex].Count;
            var pi = PageIndex + 1;
            var c = (count / SpinePageCount) * pi;
            var shownWords = pastWords + c;
            return 1000 * shownWords / totalWordCount;
        }

        public double Percent()
        {
            if (PrePaginated)
            {
                double count = Math.Max(1, SpineCount - 1);
                return (SpineIndex / count) * 100.0;
            }
            return (Progress() / 999.0) * 100.0;
        }
    }

    public class ReadingProgressServiceHelper
    {
        public static ProgressSpine SpineFromProgress(WordCountItem wordsCount, float position)
        {
            float totalWordCount = wordsCount.Sum(x => x.Count);
            int wp = (int)(position * totalWordCount / 1000);
            WordCountItemElement result = null;

            foreach (var currentWordsCount in wordsCount)
            {
                if (result == null)
                    result = currentWordsCount;
                else if (result.Count >= wp)
                    break;
                else
                    result = new WordCountItemElement { SpineItemRef = currentWordsCount.SpineItemRef, Count = result.Count + currentWordsCount.Count };
            }

            return new ProgressSpine { TotalWordCount = totalWordCount, Result = result, WordPosition = wp };
        }

        public static ReadingState FindNextStateFromProgress(ReadingState readingState, float progress)
        {
            var progressSpine = SpineFromProgress(readingState.WordCount, progress);
            if (progressSpine == null)
                return readingState;

            var spineItem = progressSpine.Result.SpineItemRef;

            var wordCountItem = readingState.WordCount.Find(x => x.SpineItemRef.Id == spineItem.Id);

            var index = readingState.WordCount.IndexOf(wordCountItem);

            var pageProgress = new List<Tuple<ReadingState, int>>();
            for (int i = 0; i < readingState.SpinePageCount - 1; i++)
            {
                var s = new ReadingState(readingState.SpineCount, readingState.SpinePageCount, i, index, readingState.WordCount, readingState.PrePaginated);
                pageProgress.Add(new Tuple<ReadingState, int>(s, s.Progress()));
            }

            return FindNearestValue(pageProgress, (int)progress).Item1;
        }


        private static Tuple<ReadingState, int> FindNearestValue(List<Tuple<ReadingState, int>> values, int target)
        {
            //            let diff a b =
            //  if a > b
            //  then a -b
            //  else b - a

            //let findNearestValue (values:('t * int) list) target =
            //  values
            //  |> List.sortBy(fun(_, v)->diff v target)
            //  |> List.head


            //values.Sort(x => x.)
            // TODO : supprimer bouchon & implémenter code ci-dessus
            return values.First();
        }
    }

    public class ProgressSpine
    {
        public float TotalWordCount { get; set; }
        public WordCountItemElement Result { get; set; }
        public int WordPosition { get; set; }
    }

    public static class FileHelper
    {
        // equivalent F# de />>
        public static string FormatPath(string p1, string p2)
        {
            var path = p2.StartsWith("/", StringComparison.CurrentCulture) && p2 != "/" ? Path.Combine(p1, p2.Substring(1)) : Path.Combine(p1, p2);
            return path.Replace("\\", "/");
        }
    }
}
