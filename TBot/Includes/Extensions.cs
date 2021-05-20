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
            Random rnd = new();
            return source.OrderBy((item) => rnd.Next());
        }

        public static string FirstCharToUpper(this string input)
        {
            return input switch
            {
                null => throw new ArgumentNullException(nameof(input)),
                "" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
                _ => input.First().ToString().ToUpper() + input[1..]
            };
        }
    }
}
