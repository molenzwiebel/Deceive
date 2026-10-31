using System;
using System.IO;
using System.Threading.Tasks;

namespace Deceive;

internal enum XmppUnitKind
{
    /// Whitespace between stanzas (XMPP keepalives).
    Whitespace,

    /// The XML declaration, &lt;stream:stream&gt; header, or &lt;/stream:stream&gt; footer. Never closed as an element.
    StreamFraming,

    /// A complete top-level element, e.g. a whole &lt;presence&gt;...&lt;/presence&gt; stanza.
    Stanza
}

/// Splits an XMPP byte stream into complete top-level units, no matter how TCP chunks the data.
///
/// All scanning is done on raw bytes: XML markup characters are ASCII, and UTF-8 continuation bytes
/// are always >= 0x80, so a multibyte character can never be mistaken for markup or split in half.
///
/// Units are returned as slices of the reader's internal buffer (no copies), so they are only valid until
/// the next call to <see cref="FillAsync" />. Usage:
/// <code>
/// while (await reader.FillAsync())
///     while (reader.TryReadUnit(out var unit)) { ... }
/// </code>
internal sealed class XmppStanzaReader
{
    private const int InitialBufferSize = 16 * 1024;

    // The roster can be large for players with many friends, but anything beyond this is not XMPP we understand.
    private const int MaxUnitSize = 16 * 1024 * 1024;

    private readonly Stream _stream;
    private byte[] _buffer = new byte[InitialBufferSize];
    private int _start; // first byte of the current (incomplete) unit
    private int _end; // one past the last valid byte

    // Resumable scan state for the unit starting at _start, so a large stanza arriving in many reads
    // is scanned once in total instead of once per read.
    private int _scanPos; // absolute buffer index; always at a construct boundary
    private int _depth;

    internal XmppStanzaReader(Stream stream) => _stream = stream;

    /// Reads more data from the stream. Invalidates all previously returned units.
    /// Returns false when the stream has ended.
    internal async ValueTask<bool> FillAsync()
    {
        if (_start == _end && _buffer.Length > InitialBufferSize)
        {
            // Nothing pending and the buffer grew for a large stanza (e.g. the roster): give the memory back.
            _buffer = new byte[InitialBufferSize];
            _start = _end = _scanPos = 0;
        }
        else if (_start > 0)
        {
            // Move the incomplete unit to the front.
            var pending = _end - _start;
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, pending);
            _scanPos -= _start;
            _start = 0;
            _end = pending;
        }

        if (_end == _buffer.Length)
        {
            if (_buffer.Length >= MaxUnitSize)
                throw new InvalidDataException($"XMPP stanza exceeds {MaxUnitSize} bytes.");
            Array.Resize(ref _buffer, Math.Min(_buffer.Length * 2, MaxUnitSize));
        }

        var read = await _stream.ReadAsync(_buffer.AsMemory(_end));
        if (read == 0)
            return false;

        _end += read;
        return true;
    }

    /// Returns the next complete unit from the buffered data, or false if more data is needed.
    internal bool TryReadUnit(out XmppUnit unit)
    {
        if (!TryScan(out var length, out var kind))
        {
            unit = default;
            return false;
        }

        unit = new XmppUnit(kind, _buffer, _start, length);
        _start += length;
        _scanPos = _start;
        _depth = 0;
        return true;
    }

    private bool TryScan(out int length, out XmppUnitKind kind)
    {
        length = 0;
        kind = XmppUnitKind.Stanza;
        if (_start == _end)
            return false;

        var data = _buffer.AsSpan(_start, _end - _start);

        // Text at the top level (whitespace keepalives): forward whatever we have up to the next tag.
        if (data[0] != (byte)'<')
        {
            var next = data.IndexOf((byte)'<');
            length = next < 0 ? data.Length : next;
            kind = XmppUnitKind.Whitespace;
            return true;
        }

        var pos = _scanPos - _start;
        var depth = _depth;
        try
        {
            while (pos < data.Length)
            {
                if (data[pos] != (byte)'<')
                {
                    // Character data inside an element. All of it can be skipped, even if the next tag hasn't arrived.
                    var next = data[pos..].IndexOf((byte)'<');
                    if (next < 0)
                    {
                        pos = data.Length;
                        return false;
                    }

                    pos += next;
                    continue;
                }

                var rest = data[pos..];

                // <?xml ... ?> declaration, <!-- comment -->, <![CDATA[ ... ]]>
                if (TrySkipSpecial(rest, "<?"u8, "?>"u8, out var special) ||
                    TrySkipSpecial(rest, "<!--"u8, "-->"u8, out special) ||
                    TrySkipSpecial(rest, "<![CDATA["u8, "]]>"u8, out special))
                {
                    if (special < 0)
                        return false;
                    pos += special;
                    if (depth == 0)
                    {
                        length = pos;
                        kind = XmppUnitKind.StreamFraming;
                        return true;
                    }

                    continue;
                }

                var tagLength = ScanTag(rest);
                if (tagLength < 0)
                    return false;

                var tag = rest[..tagLength];
                pos += tagLength;

                if (tag.Length > 1 && tag[1] == (byte)'/')
                {
                    // Closing tag. At depth 0 this can only be </stream:stream>.
                    if (depth == 0)
                    {
                        length = pos;
                        kind = XmppUnitKind.StreamFraming;
                        return true;
                    }

                    depth--;
                }
                else if (tag[^2] == (byte)'/')
                {
                    // Self-closing tag; depth unchanged.
                }
                else if (depth == 0 && IsStreamHeader(tag))
                {
                    // <stream:stream ...> is opened once and only closed when the connection ends.
                    length = pos;
                    kind = XmppUnitKind.StreamFraming;
                    return true;
                }
                else
                {
                    depth++;
                }

                if (depth == 0)
                {
                    length = pos;
                    kind = XmppUnitKind.Stanza;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            // Remember where we got to; pos is always at a construct boundary here.
            _scanPos = _start + pos;
            _depth = depth;
        }
    }

    /// Returns the length of the tag starting at data[0] (including the closing '>'), or -1 if incomplete.
    /// Quoted attribute values may legally contain '>' and are skipped over.
    private static int ScanTag(ReadOnlySpan<byte> data)
    {
        byte quote = 0;
        for (var i = 1; i < data.Length; i++)
        {
            var c = data[i];
            if (quote != 0)
            {
                if (c == quote)
                    quote = 0;
            }
            else if (c is (byte)'"' or (byte)'\'')
            {
                quote = c;
            }
            else if (c == (byte)'>')
            {
                return i + 1;
            }
        }

        return -1;
    }

    /// If data starts with <paramref name="open" />, sets <paramref name="length" /> to the length up to and including
    /// <paramref name="close" /> (or -1 if not yet available) and returns true.
    private static bool TrySkipSpecial(ReadOnlySpan<byte> data, ReadOnlySpan<byte> open, ReadOnlySpan<byte> close, out int length)
    {
        length = 0;
        if (data.Length < open.Length)
        {
            // Might be a prefix of this construct; only claim it if what we have matches.
            if (!open.StartsWith(data))
                return false;
            length = -1;
            return true;
        }

        if (!data.StartsWith(open))
            return false;

        var end = data[open.Length..].IndexOf(close);
        length = end < 0 ? -1 : open.Length + end + close.Length;
        return true;
    }

    private static bool IsStreamHeader(ReadOnlySpan<byte> tag) =>
        tag.StartsWith("<stream:stream"u8) && tag.Length > 14 && tag[14] is (byte)' ' or (byte)'>' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}

/// A unit inside the reader's buffer. Only valid until the reader's next <see cref="XmppStanzaReader.FillAsync" />.
internal readonly struct XmppUnit(XmppUnitKind kind, byte[] buffer, int offset, int length)
{
    internal XmppUnitKind Kind { get; } = kind;
    internal byte[] Buffer { get; } = buffer;
    internal int Offset { get; } = offset;
    internal int Length { get; } = length;

    internal ReadOnlySpan<byte> Span => Buffer.AsSpan(Offset, Length);

    /// True if this is a stanza whose top-level element has the given name, e.g. "presence".
    internal bool IsElement(ReadOnlySpan<byte> name)
    {
        var span = Span;
        return Kind == XmppUnitKind.Stanza &&
               span.Length > name.Length + 1 &&
               span[1..].StartsWith(name) &&
               span[name.Length + 1] is (byte)' ' or (byte)'>' or (byte)'/' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    internal bool Contains(ReadOnlySpan<byte> value) => Span.IndexOf(value) >= 0;
}
