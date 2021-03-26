using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tbot.Model;

namespace Tbot.Includes
{
    public static class Extensions
    {
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source)
        {
            Random rnd = new Random();
            return source.OrderBy((item) => rnd.Next());
        }
    }
}
