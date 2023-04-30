#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RogueGen.Mapping.Extensions
{
    public static class Helpers
    {
        public static string Join<T>(this IEnumerable<T> strs, Func<T, string> formmater, string? separator = "")
        {
            return string.Join(separator, strs?.Select(formmater) ?? Enumerable.Empty<string>());
        }

        public static string Join<T>(this IEnumerable<T> strs, string? separator = "")
        {
            return strs.Join(t => t?.ToString() ?? "", separator);
        }

        public static void AddNested<TList, TKey, TValueItem>(this Dictionary<TKey, TList> listHash, TKey key, TValueItem valueItem)
            where TList : ICollection<TValueItem>, new()
        {
            if (listHash.TryGetValue(key, out var valueItems))
                valueItems.Add(valueItem);
            else
                listHash.Add(key, new() { valueItem });
        }

        public static T[] ArrayFrom<T>(params T[] items) => items;

        public static string ToCamelCase(this string name)
        {
            StringBuilder builder = new();
            int current = -1;
            int length = name.Length;
            bool needUpper = true;
            char lastChar = char.MinValue;

            while (++current < length)
            {
                var ch = name[current];

                if (char.IsDigit(ch))
                {
                    if (builder.Length == 0)
                        builder.Append("_");

                    builder.Append(ch);
                    needUpper = true;
                }
                else if (char.IsSeparator(ch) || ch == '_' || ch == '-' || ch == '~')
                    needUpper = true;
                else if (builder.Length == 0 && char.IsUpper(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                    needUpper = false;
                }
                else if (builder.Length > 0 && (needUpper || char.IsLower(lastChar) && char.IsUpper(ch)))
                {
                    builder.Append(char.ToUpperInvariant(ch));
                    needUpper = false;
                }
                else
                {
                    builder.Append(ch);
                    needUpper = false;
                }

                lastChar = ch;
            }

            return builder.ToString();
        }

        public static string ToPascalCase(this string name)
        {
            StringBuilder builder = new();
            int current = -1;
            int length = name.Length;
            bool needUpper = true;
            char lastChar = char.MinValue;

            while (++current < length)
            {
                var ch = name[current];

                if (char.IsDigit(ch))
                {
                    if (builder.Length == 0)
                        builder.Append("_");

                    builder.Append(ch);
                    needUpper = true;
                }
                else if (char.IsSeparator(ch) || ch == '_' || ch == '-' || ch == '~')
                    needUpper = true;

                else if (needUpper || char.IsLower(lastChar) && char.IsUpper(ch))
                {
                    builder.Append(char.ToUpperInvariant(ch));
                    needUpper = false;
                }
                else
                {
                    builder.Append(ch);
                    needUpper = false;
                }

                lastChar = ch;
            }

            return builder.ToString();
        }

        public static string ToLowerSnakeCase(this string name)
        {
            StringBuilder builder = new();
            int current = -1;
            int length = name.Length;
            bool start = false;
            char lastChar = char.MinValue;

            while (++current < length)
            {
                var ch = name[current];
                if (char.IsSeparator(ch))
                {
                    if (!start) continue;

                    builder.Append('_');
                }
                else
                {
                    bool isUpper = char.IsUpper(ch);

                    if (char.IsLower(lastChar) && isUpper || char.IsDigit(ch))
                        builder.Append('_');

                    if (isUpper)
                        builder.Append(char.ToLowerInvariant(ch));
                    else
                        builder.Append(ch);

                    if (!start)
                        start = true;

                    lastChar = ch;
                }
            }

            return builder.ToString();
        }

        public static string ToUpperSnakeCase(this string name)
        {
            StringBuilder builder = new();
            int current = -1;
            int length = name.Length;
            bool start = false;
            char lastChar = char.MinValue;

            while (++current < length)
            {
                var ch = name[current];
                if (char.IsSeparator(ch))
                {
                    if (!start) continue;

                    builder.Append('_');
                }
                else
                {
                    bool isUpper = char.IsUpper(ch);

                    if (char.IsLower(lastChar) && isUpper || char.IsDigit(ch))
                        builder.Append('_');

                    if (!isUpper)
                        builder.Append(char.ToUpperInvariant(ch));
                    else
                        builder.Append(ch);

                    if (!start)
                        start = true;

                    lastChar = ch;
                }
            }

            return builder.ToString();
        }

        public static string ToCamelCase(this Enum value) => value.ToString().ToCamelCase();

        public static string ToPascalCase(this Enum value) => value.ToString().ToPascalCase();

        public static string ToLowerSnakeCase(this Enum value) => value.ToString().ToLowerSnakeCase();

        public static string ToUpperSnakeCase(this Enum value) => value.ToString().ToUpperSnakeCase();

    }
}
