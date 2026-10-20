using Game.Input;
using HeadTrack.Options;

namespace HeadTrack.Systems
{
    /// <summary>
    /// The options page's keys, read once a frame by the apply system:
    /// toggle flips the Enabled option (saved, so the page shows it), centre
    /// takes the current head pose as neutral (OpenTrack's own centre key is
    /// dead under Wayland), hold freezes the eye while it is down. The game
    /// masks the keyboard while a text field has focus; the check here also
    /// covers the frame focus arrives.
    /// </summary>
    internal sealed class HotKeys
    {
        private ProxyAction? m_Toggle;
        private ProxyAction? m_Centre;
        private ProxyAction? m_Hold;

        /// <summary>The hold key is down this frame.</summary>
        public bool Hold { get; private set; }

        public void Update(double now, bool inGame)
        {
            InputManager input = InputManager.instance;
            if (!inGame || input == null || input.hasInputFieldFocus || !Bind())
            {
                Hold = false;
                return;
            }
            if (m_Toggle!.WasPressedThisFrame())
            {
                Toggle();
            }
            if (m_Centre!.WasPressedThisFrame())
            {
                Runtime.Head.RequestCentre(now);
                Mod.Log.Info("key: centre");
            }
            Hold = m_Hold!.IsPressed();
        }

        /// <summary>Finds the actions once the options have registered them, and switches them on (they start off).</summary>
        private bool Bind()
        {
            if (m_Toggle != null && m_Centre != null && m_Hold != null)
            {
                return true;
            }
            HeadTrackOptions? options = Runtime.Options;
            if (options == null || !options.keyBindingRegistered)
            {
                return false;
            }
            m_Toggle = options.GetAction(HeadTrackOptions.ToggleAction);
            m_Centre = options.GetAction(HeadTrackOptions.CentreAction);
            m_Hold = options.GetAction(HeadTrackOptions.HoldAction);
            if (m_Toggle == null || m_Centre == null || m_Hold == null)
            {
                Mod.Log.Warn("keys: an action is missing from the input map");
                return false;
            }
            // Takes effect at the next InputManager update.
            m_Toggle.shouldBeEnabled = true;
            m_Centre.shouldBeEnabled = true;
            m_Hold.shouldBeEnabled = true;
            return true;
        }

        private static void Toggle()
        {
            HeadTrackOptions? options = Runtime.Options;
            if (options != null)
            {
                options.Enabled = !options.Enabled;
                options.ApplyAndSave();
            }
            else
            {
                Runtime.Enabled = !Runtime.Enabled;
            }
            Mod.Log.Info($"key: head tracking {(Runtime.Enabled ? "on" : "off")}");
        }
    }
}
