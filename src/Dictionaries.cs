using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;

namespace DeskHand
{
    enum Lang { Other, Ru, En }

    sealed class WordSet
    {
        const int TopCount = 3000;

        readonly ulong[] all, top;

        public WordSet(IEnumerable<string> wordsByFrequency)
        {
            var list = new List<ulong>();
            var topList = new List<ulong>();
            foreach (string w in wordsByFrequency)
            {
                ulong h = Hash(w);
                list.Add(h);
                if (topList.Count < TopCount) topList.Add(h);
            }
            all = list.ToArray();
            top = topList.ToArray();
            Array.Sort(all);
            Array.Sort(top);
        }

        public bool Contains(string lowerWord) { return Array.BinarySearch(all, Hash(lowerWord)) >= 0; }
        public bool IsCommon(string lowerWord) { return Array.BinarySearch(top, Hash(lowerWord)) >= 0; }

        static ulong Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s)
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            return h;
        }
    }

    static class Dictionaries
    {
        static WordSet ru, en;
        static int loading;

        public static WordSet Get(Lang lang)
        {
            if (ru == null) Preload();
            return lang == Lang.Ru ? ru : lang == Lang.En ? en : null;
        }

        public static bool Ready { get { return ru != null && en != null; } }

        public static void Preload()
        {
            if (Interlocked.Exchange(ref loading, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate { LoadNow(); });
        }

        public static void LoadNow()
        {
            var r = Load("ru");
            var e = Load("en");
            ru = r;
            en = e;
        }

        static WordSet Load(string lang)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskHand.dict." + lang + ".txt.gz"))
            using (var gz = new GZipStream(s, CompressionMode.Decompress))
            using (var reader = new StreamReader(gz, Encoding.UTF8))
                return new WordSet(Lines(reader));
        }

        static IEnumerable<string> Lines(StreamReader reader)
        {
            string line;
            while ((line = reader.ReadLine()) != null)
                if (line.Length > 0) yield return line;
        }
    }
}
