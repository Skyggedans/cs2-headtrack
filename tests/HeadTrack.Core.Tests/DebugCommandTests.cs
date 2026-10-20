using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class DebugCommandTests
{
    [Theory]
    [InlineData("shot centre-01", DebugVerb.Shot, "centre-01")]
    [InlineData("  SHOT   a_b.c  ", DebugVerb.Shot, "a_b.c")]
    [InlineData("dump", DebugVerb.Dump, "")]
    [InlineData("enable 1", DebugVerb.Enable, "1")]
    [InlineData("enable 0", DebugVerb.Enable, "0")]
    [InlineData("pause 1", DebugVerb.Pause, "1")]
    [InlineData("freeze 1", DebugVerb.Freeze, "1")]
    [InlineData("freeze 0", DebugVerb.Freeze, "0")]
    [InlineData("display", DebugVerb.Display, "")]
    [InlineData("display 1", DebugVerb.Display, "1")]
    [InlineData("centre", DebugVerb.Centre, "")]
    [InlineData("quit", DebugVerb.Quit, "")]
    public void Parses_each_verb(string text, DebugVerb verb, string argument)
    {
        Assert.True(DebugCommand.TryParse(text, out DebugCommand command, out string? error), error);
        Assert.Equal(verb, command.Verb);
        Assert.Equal(argument, command.Argument);
    }

    [Fact]
    public void Set_takes_a_name_and_a_value()
    {
        Assert.True(DebugCommand.TryParse("set gain 3.5", out DebugCommand command, out _));

        Assert.Equal(DebugVerb.Set, command.Verb);
        Assert.Equal("gain", command.Name);
        Assert.Equal("3.5", command.Value);
    }

    [Fact]
    public void View_takes_the_pivot_the_zoom_and_the_angles()
    {
        Assert.True(DebugCommand.TryParse("view 1200.5 -340 250 45 60.5", out DebugCommand command, out string? error), error);

        Assert.Equal(DebugVerb.View, command.Verb);
        Assert.Equal(1200.5, command.View.PivotX);
        Assert.Equal(-340.0, command.View.PivotZ);
        Assert.Equal(250.0, command.View.Zoom);
        Assert.Equal(45.0, command.View.Yaw);
        Assert.Equal(60.5, command.View.Pitch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("view 1 2 3 4")]
    [InlineData("view 1 2 3 4 5 6")]
    [InlineData("view 1 2 3 4 NaN")]
    [InlineData("view 1 2 Infinity 4 5")]
    [InlineData("view 1 2 0 4 5")]
    [InlineData("view a b c d e")]
    [InlineData("freeze yes")]
    [InlineData("display -1")]
    [InlineData("display left")]
    [InlineData("display 1 2")]
    [InlineData("frobnicate")]
    [InlineData("shot")]
    [InlineData("shot ../etc/passwd")]
    [InlineData("shot a/b")]
    [InlineData("shot c:evil")]
    [InlineData("enable maybe")]
    [InlineData("pause 2")]
    [InlineData("set gain")]
    [InlineData("dump extra")]
    public void Rejects_what_it_does_not_understand(string text)
    {
        Assert.False(DebugCommand.TryParse(text, out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}

public class KeyValueConfigTests
{
    [Fact]
    public void Reads_keys_and_values_ignoring_comments_blanks_and_case()
    {
        const string text = "# HeadTrack debug settings\n\n debug_port = 4250 \nShots_Dir=Z:\\tmp\\shots\n# gain = 9\ngain = 2.5\n";

        KeyValueConfig config = KeyValueConfig.Parse(text);

        Assert.Empty(config.Errors);
        Assert.Equal("4250", config.Get("DEBUG_PORT"));
        Assert.Equal("Z:\\tmp\\shots", config.Get("shots_dir"));
        Assert.Equal("2.5", config.Get("gain"));
        Assert.Null(config.Get("missing"));
    }

    [Fact]
    public void A_later_line_overrides_an_earlier_one()
    {
        KeyValueConfig config = KeyValueConfig.Parse("gain = 1\ngain = 4\n");

        Assert.Equal("4", config.Get("gain"));
    }

    [Fact]
    public void Malformed_lines_are_reported_with_their_number()
    {
        KeyValueConfig config = KeyValueConfig.Parse("gain = 1\nthis is not a pair\n= no key\n");

        Assert.Equal(2, config.Errors.Count);
        Assert.Contains("line 2", config.Errors[0]);
        Assert.Contains("line 3", config.Errors[1]);
    }

    [Fact]
    public void Typed_getters_fall_back_on_missing_or_bad_values()
    {
        KeyValueConfig config = KeyValueConfig.Parse("port = 4250\ngain = 2,5\nflag = yes\n");

        Assert.Equal(4250, config.GetInt("port", 1));
        Assert.Equal(7, config.GetInt("missing", 7));
        Assert.Equal(1.0, config.GetDouble("gain", 1.0));
        Assert.True(config.GetBool("flag", false));
        Assert.False(config.GetBool("missing", false));
    }
}
