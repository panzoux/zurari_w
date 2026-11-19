using System.Text;

namespace Zurari.Runtime.Migemo;

/// <summary>
/// Builds one regex alternation out of many words with a ternary (AA-balanced) tree, ported from
/// rustmigemo's <c>ternary_regex_generator.rs</c> with <c>RegexOperator::Default</c> only
/// (<c>|</c>, <c>(</c>, <c>)</c>, <c>[</c>, <c>]</c>, no newline operator). Words are Unicode scalars.
/// A word that merely extends an already added word is dropped, exactly as rustmigemo does.
/// Not thread-safe: use one instance per query.
/// </summary>
internal sealed class TernaryRegexGenerator
{
    private const string EscapeChars = "()[]{}?*+|^$.\\";

    private Node? _root;

    public void Add(int[] word)
    {
        if (word.Length == 0)
        {
            return;
        }

        _root = Insert(word, 0, _root);
    }

    public string Generate()
    {
        if (_root is null)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder();
        Generate(_root, buffer);
        return buffer.ToString();
    }

    private static Node? Skew(Node? t)
    {
        if (t?.Left is { } left && left.Level == t.Level)
        {
            t.Left = left.Right;
            left.Right = t;
            return left;
        }

        return t;
    }

    private static Node? Split(Node? t)
    {
        if (t?.Right is { Right: { } rightRight } right && t.Level == rightRight.Level)
        {
            t.Right = right.Left;
            right.Left = t;
            right.Level++;
            return right;
        }

        return t;
    }

    private static Node? Insert(int[] word, int offset, Node? t)
    {
        if (offset >= word.Length)
        {
            return t;
        }

        if (t is null)
        {
            var r = new Node(word[offset]) { Level = 1 };
            r.Child = Insert(word, offset + 1, null);
            return r;
        }

        int x = word[offset];
        if (x < t.Code)
        {
            t.Left = Insert(word, offset, t.Left);
        }
        else if (x > t.Code)
        {
            t.Right = Insert(word, offset, t.Right);
        }
        else
        {
            if (t.Child is not null)
            {
                t.Child = Insert(word, offset + 1, t.Child);
            }

            return t;
        }

        return Split(Skew(t));
    }

    private static void TraverseSiblings(Node? node, List<Node> buffer)
    {
        if (node is null)
        {
            return;
        }

        TraverseSiblings(node.Left, buffer);
        buffer.Add(node);
        TraverseSiblings(node.Right, buffer);
    }

    private static void AppendCode(StringBuilder buffer, int code)
    {
        if (code < 0x80 && EscapeChars.Contains((char)code, StringComparison.Ordinal))
        {
            buffer.Append('\\');
        }

        buffer.Append(char.ConvertFromUtf32(code));
    }

    private static void Generate(Node? node, StringBuilder buffer)
    {
        var siblings = new List<Node>();
        TraverseSiblings(node, siblings);
        int brother = siblings.Count;
        int hasChild = 0;
        foreach (Node n in siblings)
        {
            if (n.Child is not null)
            {
                hasChild++;
            }
        }

        int noChild = brother - hasChild;
        if (brother > 1 && hasChild > 0)
        {
            buffer.Append('(');
        }

        if (noChild > 0)
        {
            if (noChild > 1)
            {
                buffer.Append('[');
            }

            foreach (Node n in siblings)
            {
                if (n.Child is null)
                {
                    AppendCode(buffer, n.Code);
                }
            }

            if (noChild > 1)
            {
                buffer.Append(']');
            }
        }

        if (hasChild > 0)
        {
            if (noChild > 0)
            {
                buffer.Append('|');
            }

            bool isFirst = true;
            foreach (Node n in siblings)
            {
                if (n.Child is null)
                {
                    continue;
                }

                if (!isFirst)
                {
                    buffer.Append('|');
                }

                AppendCode(buffer, n.Code);
                Generate(n.Child, buffer);
                isFirst = false;
            }
        }

        if (brother > 1 && hasChild > 0)
        {
            buffer.Append(')');
        }
    }

    private sealed class Node
    {
        public Node(int code)
        {
            Code = code;
        }

        public int Code { get; }

        public Node? Child { get; set; }

        public Node? Left { get; set; }

        public Node? Right { get; set; }

        public int Level { get; set; }
    }
}
