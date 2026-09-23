using DotNetDevMCP.CodeIntelligence.Services;
using Microsoft.CodeAnalysis.Text;

namespace DotNetDevMCP.CodeIntelligence.Tests;

/// <summary>Edit tools format only the spans they changed; these pin the old-to-new span mapping.</summary>
public class ChangedSpansTests {
    [Fact]
    public void Maps_each_change_to_its_span_in_the_new_text() {
        var oldText = SourceText.From("Find(x); Find(y);");
        var changes = new[] {
            new TextChange(new TextSpan(9, 4), "FindAffected"), // second Find, listed first on purpose
            new TextChange(new TextSpan(0, 4), "FindAffected"),
        };
        var newText = oldText.WithChanges(changes).ToString();

        var spans = CodeModificationService.ChangedSpansInNewText(changes);

        Assert.Equal(new[] { "FindAffected", "FindAffected" }, spans.Select(s => newText.Substring(s.Start, s.Length)));
    }

    [Fact]
    public void Deletion_becomes_an_empty_span_at_the_shifted_position() {
        var changes = new[] {
            new TextChange(new TextSpan(0, 2), "abcd"),  // +2
            new TextChange(new TextSpan(10, 3), ""),     // deletion
        };

        var spans = CodeModificationService.ChangedSpansInNewText(changes);

        Assert.Equal(new TextSpan(0, 4), spans[0]);
        Assert.Equal(new TextSpan(12, 0), spans[1]);
    }
}
