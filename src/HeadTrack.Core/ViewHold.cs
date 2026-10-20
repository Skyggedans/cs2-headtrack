namespace HeadTrack.Core
{
    /// <summary>
    /// Whether the view must hold still this frame: a forced freeze (the
    /// debug channel), the hold key, or a mouse button held since a press
    /// over the world rather than the UI (a tool drag, a camera turn), so
    /// the world stays put under the cursor.
    /// </summary>
    public sealed class ViewHold
    {
        private bool m_DragOverWorld;

        /// <param name="forced">The view is held whatever the input does.</param>
        /// <param name="holdKey">The hold key is down.</param>
        /// <param name="anyButtonHeld">A mouse button is down.</param>
        /// <param name="pressedThisFrame">A mouse button went down this frame.</param>
        /// <param name="overWorld">The cursor is over the world, not the UI.</param>
        public bool Update(bool forced, bool holdKey, bool anyButtonHeld, bool pressedThisFrame, bool overWorld)
        {
            // Every frame, whatever else holds the view: a press missed while
            // the hold key is down would end the hold in the middle of a drag.
            bool dragging = Drag(anyButtonHeld, pressedThisFrame, overWorld);
            return forced || holdKey || dragging;
        }

        private bool Drag(bool anyButtonHeld, bool pressedThisFrame, bool overWorld)
        {
            if (!anyButtonHeld)
            {
                m_DragOverWorld = false;
                return false;
            }
            if (pressedThisFrame)
            {
                m_DragOverWorld = overWorld;
            }
            return m_DragOverWorld;
        }
    }
}
