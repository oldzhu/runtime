// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;

namespace System.Text.Json
{
    public sealed partial class Utf8JsonWriter
    {
        /// <summary>
        /// Writes the <see cref="decimal"/> value (as a JSON number) as an element of a JSON array.
        /// </summary>
        /// <param name="value">The value to write.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if this would result in invalid JSON being written (while validation is enabled).
        /// </exception>
        /// <remarks>
        /// Writes the <see cref="decimal"/> using the default <see cref="StandardFormat"/> (that is, 'G').
        /// </remarks>
        public void WriteNumberValue(decimal value)
        {
            if (!_options.SkipValidation)
            {
                ValidateWritingValue();
            }

            if (_options.Indented)
            {
                WriteNumberValueIndented(value);
            }
            else
            {
                WriteNumberValueMinimized(value);
            }

            SetFlagToAddListSeparatorBeforeNextItem();
            _tokenType = JsonTokenType.Number;
        }

        private void WriteNumberValueMinimized(decimal value)
        {
            int maxRequired = JsonConstants.MaximumFormatDecimalLength + 1; // Optionally, 1 list separator

            if (_memory.Length - BytesPending < maxRequired)
            {
                Grow(maxRequired);
            }

            Span<byte> output = _memory.Span;

            if (_currentDepth < 0)
            {
                output[BytesPending++] = JsonConstants.ListSeparator;
            }

            bool result = Utf8Formatter.TryFormat(value, output.Slice(BytesPending), out int bytesWritten);
            Debug.Assert(result);
            BytesPending += bytesWritten;
        }

        private void WriteNumberValueIndented(decimal value)
        {
            int indent = Indentation;
            Debug.Assert(indent <= _indentLength * _options.MaxDepth);

            int maxRequired = indent + JsonConstants.MaximumFormatDecimalLength + 1 + _newLineLength; // Optionally, 1 list separator and 1-2 bytes for new line

            if (_memory.Length - BytesPending < maxRequired)
            {
                Grow(maxRequired);
            }

            Span<byte> output = _memory.Span;

            if (_currentDepth < 0)
            {
                output[BytesPending++] = JsonConstants.ListSeparator;
            }

            if (_tokenType != JsonTokenType.PropertyName)
            {
                if (_tokenType != JsonTokenType.None)
                {
                    WriteNewLine(output);
                }
                WriteIndentation(output.Slice(BytesPending), indent);
                BytesPending += indent;
            }

            bool result = Utf8Formatter.TryFormat(value, output.Slice(BytesPending), out int bytesWritten);
            Debug.Assert(result);
            BytesPending += bytesWritten;
        }

        internal void WriteNumberValueAsString(decimal value)
        {
            Span<byte> utf8Number = stackalloc byte[JsonConstants.MaximumFormatDecimalLength];
            bool result = Utf8Formatter.TryFormat(value, utf8Number, out int bytesWritten);
            Debug.Assert(result);
            WriteNumberValueAsStringUnescaped(utf8Number.Slice(0, bytesWritten));
        }
    }
}
