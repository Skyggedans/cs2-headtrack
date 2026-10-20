using System.Globalization;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;
using HeadTrack.Core;

namespace HeadTrack.Options
{
    /// <summary>How the head drives the camera. Saved by member name: renaming one resets it.</summary>
    public enum HeadTrackMode
    {
        Centred,
        Window,
    }

    /// <summary>What the six numbers of a packet mean. Saved by member name.</summary>
    public enum HeadPoseSource
    {
        /// <summary>OpenTrack's "UDP over network" output, centred by OpenTrack.</summary>
        OpenTrack,

        /// <summary>A sender that gives the eye in the display frame, such as tobii-opentrack.</summary>
        Absolute,
    }

    /// <summary>What the head does while a construction tool is active. Saved by member name.</summary>
    public enum ToolTracking
    {
        Full,
        GainUpToOne,
        Off,
    }

    /// <summary>
    /// The mod's page in the game's Options. The game builds it by reflection
    /// from the public properties; every change runs <see cref="Apply"/>,
    /// which hands the values to <see cref="Runtime.ApplyOptions"/>, and saves
    /// the non-default ones to ModsSettings/HeadTrack/HeadTrack.coc. The class
    /// name must be unique among all loaded settings (they are saved by type
    /// name).
    /// </summary>
    [FileLocation("ModsSettings/HeadTrack/HeadTrack")]
    [SettingsUITabOrder(MainTab, AdvancedTab)]
    [SettingsUIGroupOrder(GeneralGroup, MotionGroup, ScreenGroup, KeysGroup, CameraGroup, InputGroup, ResetGroup)]
    [SettingsUIShowGroupName(GeneralGroup, MotionGroup, ScreenGroup, KeysGroup, CameraGroup, InputGroup, ResetGroup)]
    [SettingsUIKeyboardAction(HoldAction, ActionType.Button, modifierOptions: ModifierOptions.Ignore)]
    [SettingsUIMouseAction(HoldAction, ActionType.Button, modifierOptions: ModifierOptions.Ignore)]
    public sealed class HeadTrackOptions : ModSetting
    {
        public const string MainTab = "Main";
        public const string AdvancedTab = "Advanced";

        public const string GeneralGroup = "General";
        public const string MotionGroup = "Motion";
        public const string ScreenGroup = "Screen";
        public const string KeysGroup = "Keys";
        public const string CameraGroup = "Camera";
        public const string InputGroup = "Input";
        public const string ResetGroup = "Reset";

        public const string ToggleAction = "Toggle";
        public const string CentreAction = "Centre";
        public const string HoldAction = "Hold";

        private string? m_PortText;

        public HeadTrackOptions(IMod mod)
            : base(mod)
        {
            // The game never calls SetDefaults on a mod's settings.
            SetDefaults();
        }

        [SettingsUISection(MainTab, GeneralGroup)]
        public bool Enabled { get; set; }

        [SettingsUISection(MainTab, GeneralGroup)]
        public HeadTrackMode Mode { get; set; }

        [SettingsUISection(MainTab, MotionGroup)]
        [SettingsUISlider(min = 0.5f, max = 5f, step = 0.1f, unit = Unit.kFloatSingleFraction, updateOnDragEnd = true)]
        public float Gain { get; set; }

        [SettingsUISection(MainTab, MotionGroup)]
        [SettingsUISlider(min = 0f, max = 2f, step = 0.05f, unit = Unit.kFloatTwoFractions, updateOnDragEnd = true)]
        public float DepthGain { get; set; }

        [SettingsUISection(MainTab, MotionGroup)]
        [SettingsUISlider(min = 0.5f, max = 1.5f, step = 0.05f, unit = Unit.kFloatTwoFractions, updateOnDragEnd = true)]
        public float WindowDepth { get; set; }

        [SettingsUISection(MainTab, MotionGroup)]
        [SettingsUISlider(min = HeadTrack.Core.Smoothing.Min, max = HeadTrack.Core.Smoothing.Max, step = 5, unit = Unit.kPercentage, updateOnDragEnd = true)]
        public int Smoothing { get; set; }

        [SettingsUISection(MainTab, ScreenGroup)]
        [SettingsUISlider(min = 200, max = 2000, step = 1, updateOnDragEnd = true)]
        [SettingsUICustomFormat(separateThousands = false)]
        public int ScreenWidthMm { get; set; }

        [SettingsUISection(MainTab, ScreenGroup)]
        [SettingsUISlider(min = 100, max = 1200, step = 1, updateOnDragEnd = true)]
        [SettingsUICustomFormat(separateThousands = false)]
        public int ScreenHeightMm { get; set; }

        [SettingsUISection(MainTab, ScreenGroup)]
        [SettingsUISlider(min = 300, max = 1200, step = 10, updateOnDragEnd = true)]
        [SettingsUICustomFormat(separateThousands = false)]
        public int ViewingDistanceMm { get; set; }

        [SettingsUISection(MainTab, KeysGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.H, ToggleAction, ctrl: true)]
        public ProxyBinding ToggleKey { get; set; }

        [SettingsUISection(MainTab, KeysGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.H, CentreAction, shift: true)]
        public ProxyBinding CentreKey { get; set; }

        [SettingsUISection(MainTab, KeysGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.N, HoldAction)]
        public ProxyBinding HoldKey { get; set; }

        [SettingsUISection(MainTab, KeysGroup)]
        [SettingsUIMouseBinding(BindingMouse.None, HoldAction)]
        public ProxyBinding HoldMouseButton { get; set; }

        [SettingsUISection(MainTab, KeysGroup)]
        [SettingsUIButton]
        public bool ResetKeys
        {
            set => ResetKeyBindings();
        }

        [SettingsUISection(AdvancedTab, CameraGroup)]
        public bool FollowCamera { get; set; }

        [SettingsUISection(AdvancedTab, CameraGroup)]
        public bool PhotoMode { get; set; }

        [SettingsUISection(AdvancedTab, CameraGroup)]
        public ToolTracking WhileBuilding { get; set; }

        [SettingsUISection(AdvancedTab, InputGroup)]
        public HeadPoseSource Source { get; set; }

        /// <summary>The saved port; the page shows <see cref="PortText"/>.</summary>
        [SettingsUIHidden]
        public int Port { get; set; }

        /// <summary>
        /// The port as typed. The UI applies on every keystroke, so the raw
        /// text is kept (the field must not snap back while typing) and only
        /// a valid port reaches <see cref="Port"/>. Not saved.
        /// </summary>
        [Exclude]
        [SettingsUISection(AdvancedTab, InputGroup)]
        [SettingsUITextInput]
        [SettingsUIWarning(typeof(HeadTrackOptions), nameof(PortTextInvalid))]
        public string PortText
        {
            get => m_PortText ?? Port.ToString(CultureInfo.InvariantCulture);
            set
            {
                m_PortText = value;
                if (SettingsLimits.TryParsePort(value, out int port, out _))
                {
                    Port = port;
                }
            }
        }

        /// <summary>What the mod does with the pose port now, in the game's language; read-only.</summary>
        [Exclude]
        [SettingsUISection(AdvancedTab, InputGroup)]
        [SettingsUIWarning(typeof(HeadTrackOptions), nameof(ListenerWarning))]
        public string ListenerStatus => HeadTrackLocale.ListenerStatus(Runtime.Listener.State, Runtime.PosePort, Runtime.Listener.ListeningPort);

        [SettingsUISection(AdvancedTab, InputGroup)]
        public bool InvertX { get; set; }

        [SettingsUISection(AdvancedTab, InputGroup)]
        public bool InvertY { get; set; }

        [SettingsUISection(AdvancedTab, InputGroup)]
        public bool InvertZ { get; set; }

        /// <summary>The port field's warning: the typed text is no port, so the mod keeps the last valid one.</summary>
        private bool PortTextInvalid() => m_PortText != null && !SettingsLimits.TryParsePort(m_PortText, out _, out _);

        /// <summary>The status line's warning: the mod could not listen on the port the page asks for.</summary>
        private bool ListenerWarning() => Runtime.Listener.Failing;

        /// <summary>A button: every option back to its default (the keys have their own).</summary>
        [SettingsUISection(AdvancedTab, ResetGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool ResetToDefaults
        {
            set
            {
                SetDefaults();
                ApplyAndSave();
            }
        }

        public override void SetDefaults()
        {
            Enabled = true;
            Mode = HeadTrackMode.Centred;
            Gain = 1f;
            DepthGain = 1f;
            WindowDepth = 1f;
            Smoothing = HeadTrack.Core.Smoothing.Default;
            ScreenWidthMm = 597;
            ScreenHeightMm = 336;
            ViewingDistanceMm = 600;
            FollowCamera = true;
            PhotoMode = false;
            WhileBuilding = ToolTracking.GainUpToOne;
            Source = HeadPoseSource.OpenTrack;
            Port = 4242;
            m_PortText = null;
            InvertX = false;
            InvertY = false;
            InvertZ = false;
        }

        /// <summary>
        /// Runs after every change in Options, after a reset, and once at load
        /// (LoadSettings itself never applies): hands the values to the runtime.
        /// </summary>
        public override void Apply()
        {
            base.Apply();
            Runtime.ApplyOptions(this);
        }
    }
}
