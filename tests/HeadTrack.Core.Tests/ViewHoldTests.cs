using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class ViewHoldTests
{
    [Fact]
    public void A_button_pressed_over_the_world_holds_the_view_until_it_is_released()
    {
        var hold = new ViewHold();

        bool press = hold.Update(false, false, anyButtonHeld: true, pressedThisFrame: true, overWorld: true);
        bool drag = hold.Update(false, false, anyButtonHeld: true, pressedThisFrame: false, overWorld: false);
        bool release = hold.Update(false, false, anyButtonHeld: false, pressedThisFrame: false, overWorld: true);

        Assert.True(press);
        Assert.True(drag);
        Assert.False(release);
    }

    [Fact]
    public void A_button_pressed_over_the_ui_does_not_hold_the_view()
    {
        var hold = new ViewHold();

        bool press = hold.Update(false, false, anyButtonHeld: true, pressedThisFrame: true, overWorld: false);
        bool drag = hold.Update(false, false, anyButtonHeld: true, pressedThisFrame: false, overWorld: true);

        Assert.False(press);
        Assert.False(drag);
    }

    [Fact]
    public void A_drag_that_begins_while_the_hold_key_is_down_still_holds_after_the_key()
    {
        var hold = new ViewHold();

        hold.Update(false, holdKey: true, anyButtonHeld: true, pressedThisFrame: true, overWorld: true);
        bool afterKey = hold.Update(false, holdKey: false, anyButtonHeld: true, pressedThisFrame: false, overWorld: true);

        Assert.True(afterKey, "the drag that began under the hold key was missed");
    }

    [Fact]
    public void A_world_drag_ended_under_the_hold_key_does_not_hold_after_it()
    {
        var hold = new ViewHold();
        hold.Update(false, false, anyButtonHeld: true, pressedThisFrame: true, overWorld: true);

        // With the hold key down the button is let go, then pressed over the UI.
        hold.Update(false, holdKey: true, anyButtonHeld: false, pressedThisFrame: false, overWorld: true);
        hold.Update(false, holdKey: true, anyButtonHeld: true, pressedThisFrame: true, overWorld: false);
        bool afterKey = hold.Update(false, holdKey: false, anyButtonHeld: true, pressedThisFrame: false, overWorld: false);

        Assert.False(afterKey, "an old world drag still held the view");
    }

    [Fact]
    public void A_forced_freeze_does_not_hide_a_drag_from_the_tracker()
    {
        var hold = new ViewHold();

        hold.Update(forced: true, false, anyButtonHeld: true, pressedThisFrame: true, overWorld: true);
        bool afterForce = hold.Update(forced: false, false, anyButtonHeld: true, pressedThisFrame: false, overWorld: true);

        Assert.True(afterForce);
    }
}
