#nullable enable

using System;
using System.Linq;

namespace Vidyano
{
    /// <summary>
    /// Client-side representation of a <see cref="DataTypes.BinaryFile"/> attribute value: a file name
    /// paired with its bytes. The Vidyano wire format for such an attribute is the single string
    /// <c>"&lt;fileName&gt;|&lt;base64&gt;"</c> (the file name, a pipe, then the base64-encoded data); this type
    /// is the round-trip between that service string and the (name, bytes) pair. It is the format
    /// contract shared by every Core consumer — keep <see cref="ToString"/> and
    /// <see cref="FromServiceString"/> inverses of each other.
    /// <para>An <c>Image</c> attribute is NOT a BinaryFile: its service string is the bare base64 with no
    /// file name and no pipe. Use <see cref="Convert.ToBase64String(byte[])"/> directly for those.</para>
    /// </summary>
    public sealed class BinaryFile : IEquatable<BinaryFile>
    {
        /// <summary>Creates an empty instance (no name, no data).</summary>
        public BinaryFile()
            : this(string.Empty, Array.Empty<byte>())
        {
        }

        /// <summary>Creates a named instance without data.</summary>
        public BinaryFile(string fileName)
            : this(fileName, Array.Empty<byte>())
        {
        }

        /// <summary>Creates a named instance with data.</summary>
        public BinaryFile(string fileName, byte[] data)
        {
            FileName = fileName;
            Data = data;
        }

        /// <summary>The file name (the part before the <c>|</c> in the service string).</summary>
        public string FileName { get; set; }

        /// <summary>The file bytes (base64-decoded from the part after the <c>|</c>).</summary>
        public byte[] Data { get; set; }

        /// <summary>Deconstructs into its <paramref name="fileName"/> and <paramref name="data"/>.</summary>
        public void Deconstruct(out string fileName, out byte[] data)
        {
            fileName = FileName;
            data = Data;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => ReferenceEquals(this, obj) || (obj is BinaryFile other && Equals(other));

        /// <inheritdoc />
        public bool Equals(BinaryFile? other) => other != null && other.FileName == FileName && other.Data.SequenceEqual(Data);

        /// <inheritdoc />
        public override int GetHashCode()
        {
#if NETSTANDARD2_0
            // System.HashCode isn't available on netstandard2.0; combine the name with the byte count
            // (cheap, and enough to spread keys — equality still compares the full byte sequence).
            unchecked
            {
                return ((FileName?.GetHashCode() ?? 0) * 397) ^ Data.Length;
            }
#else
            return HashCode.Combine(FileName, Data.Length);
#endif
        }

        /// <summary>The Vidyano service string: <c>"&lt;fileName&gt;|&lt;base64&gt;"</c>, or
        /// <c>"&lt;fileName&gt;|"</c> when there is no data.</summary>
        public override string ToString() => FileName + "|" + (Data.Length > 0 ? Convert.ToBase64String(Data) : string.Empty);

        /// <summary>Parses a <c>"&lt;fileName&gt;|&lt;base64&gt;"</c> service string. Returns <c>null</c> for a
        /// null/empty input. A trailing <c>|</c> (name without data) yields a named, data-less instance.
        /// The split is on the LAST <c>|</c> so a file name may itself contain pipes.</summary>
        public static BinaryFile? FromServiceString(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            if (value![value.Length - 1] == '|')
                return new BinaryFile(value.Substring(0, value.Length - 1));

            var fileName = string.Empty;
            var index = value.LastIndexOf('|');
            if (index >= 0)
            {
                fileName = value.Substring(0, index);
                value = value.Substring(index + 1);
            }

            return new BinaryFile(fileName, Convert.FromBase64String(value));
        }

        /// <summary>Tries to parse a service string into a <see cref="BinaryFile"/> without throwing on
        /// malformed base64 — the error-free counterpart of <see cref="FromServiceString"/>.</summary>
        public static bool TryParse(string? value, out BinaryFile? result)
        {
            try
            {
                result = FromServiceString(value);
                return result != null;
            }
            catch (FormatException)
            {
                result = null;
                return false;
            }
        }

        /// <summary>Implicitly converts to the service string, so a <see cref="BinaryFile"/> can be assigned
        /// straight to a string attribute value. A null file becomes a null string.</summary>
        public static implicit operator string?(BinaryFile? file) => file?.ToString();
    }
}
