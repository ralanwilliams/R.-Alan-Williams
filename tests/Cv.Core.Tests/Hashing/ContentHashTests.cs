using System.Text;
using Cv.Core.Hashing;
using Cv.Core.Model;

namespace Cv.Core.Tests.Hashing;

public class ContentHashTests
{
    private static readonly Guid Root = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid Bullet = Guid.Parse("00000000-0000-7000-8000-000000000002");

    private static CvDocument Document(string text = "Hello", string sortKey = "a0", Sha256Digest? sourceHash = null, bool reversed = false)
    {
        DocumentNode[] nodes =
        [
            new(Root, null, "root", "a0", DocumentNode.NoAttrs),
            new(Bullet, Root, "bullet", sortKey, DocumentNode.NoAttrs.Add("b", "2").Add("a", "1")),
        ];
        DocumentText[] texts =
        [
            new(Bullet, "en", text, false, null),
            new(Bullet, "nb", "Hei", false, sourceHash),
        ];
        return reversed ? new CvDocument(nodes.Reverse(), texts.Reverse()) : new CvDocument(nodes, texts);
    }

    [Fact]
    public void Canonical_form_is_exactly_the_documented_format()
    {
        var json = Encoding.UTF8.GetString(ContentHash.Canonicalize(Document()));

        Assert.Equal(
            """{"format":"cv-content/1","nodes":[""" +
            """{"id":"00000000-0000-7000-8000-000000000001","parent":null,"type":"root","sortKey":"a0","attrs":{}},""" +
            """{"id":"00000000-0000-7000-8000-000000000002","parent":"00000000-0000-7000-8000-000000000001","type":"bullet","sortKey":"a0","attrs":{"a":"1","b":"2"}}],"texts":[""" +
            """{"node":"00000000-0000-7000-8000-000000000002","locale":"en","content":"Hello","omitted":false,"sourceHash":null},""" +
            """{"node":"00000000-0000-7000-8000-000000000002","locale":"nb","content":"Hei","omitted":false,"sourceHash":null}]}""",
            json);
    }

    [Fact]
    public void Hash_does_not_depend_on_row_order() =>
        Assert.Equal(ContentHash.Compute(Document()), ContentHash.Compute(Document(reversed: true)));

    [Fact]
    public void Hash_changes_with_text_order_and_source_hash()
    {
        var original = ContentHash.Compute(Document());

        Assert.NotEqual(original, ContentHash.Compute(Document(text: "Hello!")));
        Assert.NotEqual(original, ContentHash.Compute(Document(sortKey: "a1")));
        Assert.NotEqual(original, ContentHash.Compute(Document(sourceHash: Sha256Digest.OfText("Hello"))));
    }

    [Fact]
    public void Text_hash_matches_PostgreSQL_sha256_of_convert_to_UTF8()
    {
        // Value from PostgreSQL 17: SELECT encode(sha256(convert_to('Erfaring på Åland', 'UTF8')), 'hex');
        // The database computes staleness this way, so the editor must hash identically.
        Assert.Equal(
            "4dd814fff32f7c0c269a20b8e8df1ef7b269b94f097605ea1c1c2e61bc39dc90",
            Sha256Digest.OfText("Erfaring på Åland").Hex);
    }

    [Fact]
    public void Digest_round_trips_through_bytes_and_compares_by_value()
    {
        var digest = Sha256Digest.OfText("x");

        Assert.Equal(digest, Sha256Digest.FromBytes(digest.ToBytes()));
        Assert.Throws<ArgumentException>(() => Sha256Digest.FromBytes(new byte[31]));
    }
}
