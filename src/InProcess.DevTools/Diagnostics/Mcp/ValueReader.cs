using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace InProcess.DevTools.Mcp
{
    /// <summary>
    /// Read-only access to public instance properties by (dotted) name, plus JSON-friendly rendering of the values.
    /// Only property getters are evaluated; no methods are ever invoked.
    /// </summary>
    internal static class ValueReader
    {
        private const int MaxPathSegments = 6;
        private const int MaxEnumeratedItems = 100_000;

        public static bool TryReadPath(object source, string path, out object? value)
        {
            var result = Read(source, path);
            value = result.Value;
            return result.Found;
        }

        /// <summary>
        /// Reads <paramref name="path"/> from <paramref name="source"/>. The special segments <c>$type</c> (type full name)
        /// and <c>Count</c> (on any enumerable) are available at every level.
        /// </summary>
        public static ReadResult Read(object source, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ReadResult.Failed("Property name is empty.");
            }

            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > MaxPathSegments)
            {
                return ReadResult.Failed($"Property path '{path}' has more than {MaxPathSegments} segments.");
            }

            object? current = source;
            var walked = string.Empty;

            foreach (var segment in segments)
            {
                if (NodeInfo.IsSensitiveName(segment))
                {
                    return ReadResult.Hidden();
                }

                if (current is null)
                {
                    return ReadResult.Failed($"'{walked}' is null, cannot read '{segment}'.");
                }

                if (segment == "$type")
                {
                    current = current.GetType().FullName;
                }
                else
                {
                    var property = FindProperty(current.GetType(), segment);
                    if (property is not null)
                    {
                        try
                        {
                            current = property.GetValue(current);
                        }
                        catch (TargetInvocationException ex)
                        {
                            return ReadResult.Failed($"Reading '{segment}' threw {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
                        }
                    }
                    else if (segment == "Count" && current is IEnumerable enumerable and not string)
                    {
                        current = Count(enumerable);
                    }
                    else
                    {
                        return ReadResult.Failed($"No readable public property '{segment}' on {current.GetType().FullName}.");
                    }
                }

                walked = walked.Length == 0 ? segment : walked + "." + segment;
            }

            return ReadResult.Ok(current);
        }

        public static string[] ReadablePropertyNames(Type type)
        {
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .Select(p => p.Name)
                .Where(name => !NodeInfo.IsSensitiveName(name))
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        public static int Count(IEnumerable enumerable)
        {
            if (enumerable is ICollection collection)
            {
                return collection.Count;
            }

            var count = 0;
            foreach (var _ in enumerable)
            {
                if (++count >= MaxEnumeratedItems)
                {
                    break;
                }
            }

            return count;
        }

        /// <summary>Renders a value as something System.Text.Json can always serialise.</summary>
        public static object? Describe(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string or bool or char or byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                    return value;
                case double d:
                    return double.IsFinite(d) ? d : d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case float f:
                    return float.IsFinite(f) ? f : f.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case Enum e:
                    return e.ToString();
                case DateTime or DateTimeOffset or TimeSpan or Guid:
                    return value.ToString();
                case IEnumerable enumerable:
                    return new { type = value.GetType().FullName, count = Count(enumerable) };
                default:
                    return new { type = value.GetType().FullName, text = value.ToString() };
            }
        }

        private static PropertyInfo? FindProperty(Type type, string name)
        {
            PropertyInfo? found = null;
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                if (property.Name == name)
                {
                    // Prefer the most derived declaration when a property is hidden with 'new'.
                    if (found is null || property.DeclaringType!.IsSubclassOf(found.DeclaringType!))
                    {
                        found = property;
                    }
                }
            }

            return found;
        }

        internal readonly record struct ReadResult(bool Found, object? Value, bool IsHidden, string? Error)
        {
            public static ReadResult Ok(object? value) => new(true, value, false, null);

            public static ReadResult Hidden() => new(true, null, true, null);

            public static ReadResult Failed(string error) => new(false, null, false, error);
        }
    }
}
