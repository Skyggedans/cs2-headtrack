using System.Collections.Generic;
using System.Globalization;
using Colossal;
using Game.SceneFlow;
using HeadTrack.Core;

namespace HeadTrack.Options
{
    /// <summary>
    /// The texts of the options page in one language. English and Russian
    /// come from the same table, so neither can miss a key; the game falls
    /// back on English for every other language.
    /// </summary>
    internal sealed class HeadTrackLocale : IDictionarySource
    {
        private readonly Dictionary<string, string> m_Entries;

        private HeadTrackLocale(Dictionary<string, string> entries)
        {
            m_Entries = entries;
        }

        public static HeadTrackLocale English(HeadTrackOptions options) => new HeadTrackLocale(Texts(options, russian: false));

        public static HeadTrackLocale Russian(HeadTrackOptions options) => new HeadTrackLocale(Texts(options, russian: true));

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts) => m_Entries;

        public void Unload()
        {
        }

        /// <summary>The pose listener's state as the options page shows it, in the game's language.</summary>
        public static string ListenerStatus(ListenerState state, int wantedPort, int listeningPort)
        {
            bool russian = GameManager.instance?.localizationManager?.activeLocaleId == "ru-RU";
            string T(string english, string russianText) => russian ? russianText : english;
            string F(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
            switch (state)
            {
                case ListenerState.Listening:
                    return F(T("Listening on port {0}", "Слушаю порт {0}"), listeningPort);
                case ListenerState.KeptOld:
                    return F(T("Port {0} is not available; still listening on port {1}", "Порт {0} недоступен; слушаю порт {1}"),
                        wantedPort, listeningPort);
                case ListenerState.Down:
                    return F(T("Cannot listen on port {0}; retrying", "Не удаётся слушать порт {0}; пробую снова"), wantedPort);
                default:
                    return T("Not listening yet", "Пока не слушаю");
            }
        }

        private static Dictionary<string, string> Texts(HeadTrackOptions o, bool russian)
        {
            string T(string english, string russianText) => russian ? russianText : english;

            var d = new Dictionary<string, string>
            {
                { o.GetSettingsLocaleID(), "HeadTrack" },
                { o.GetOptionTabLocaleID(HeadTrackOptions.MainTab), T("Main", "Основные") },
                { o.GetOptionTabLocaleID(HeadTrackOptions.AdvancedTab), T("Advanced", "Дополнительно") },

                { o.GetOptionGroupLocaleID(HeadTrackOptions.GeneralGroup), T("General", "Общие") },
                { o.GetOptionGroupLocaleID(HeadTrackOptions.MotionGroup), T("Motion", "Движение") },
                { o.GetOptionGroupLocaleID(HeadTrackOptions.ScreenGroup), T("Screen", "Экран") },
                { o.GetOptionGroupLocaleID(HeadTrackOptions.KeysGroup), T("Keys", "Клавиши") },
                { o.GetOptionGroupLocaleID(HeadTrackOptions.CameraGroup), T("Camera", "Камера") },
                { o.GetOptionGroupLocaleID(HeadTrackOptions.InputGroup), T("Head tracker input", "Данные трекера") },
                { o.GetOptionGroupLocaleID(HeadTrackOptions.ResetGroup), T("Reset", "Сброс") },

                { o.GetEnumValueLocaleID(HeadTrackMode.Centred), T("Centred", "По центру") },
                { o.GetEnumValueLocaleID(HeadTrackMode.Window), T("Window", "Окно") },
                { o.GetEnumValueLocaleID(HeadPoseSource.OpenTrack), T("OpenTrack (centred)", "OpenTrack (по центру)") },
                { o.GetEnumValueLocaleID(HeadPoseSource.Absolute), T("Absolute (display frame)", "Абсолютно (от экрана)") },
                { o.GetEnumValueLocaleID(ToolTracking.Full), T("Full", "Полностью") },
                { o.GetEnumValueLocaleID(ToolTracking.GainUpToOne), T("Gain at most 1", "Усиление не больше 1") },
                { o.GetEnumValueLocaleID(ToolTracking.Off), T("Off", "Выкл.") },

                { o.GetOptionFormatLocaleID(nameof(HeadTrackOptions.ScreenWidthMm)), T("{VALUE} mm", "{VALUE} мм") },
                { o.GetOptionFormatLocaleID(nameof(HeadTrackOptions.ScreenHeightMm)), T("{VALUE} mm", "{VALUE} мм") },
                { o.GetOptionFormatLocaleID(nameof(HeadTrackOptions.ViewingDistanceMm)), T("{VALUE} mm", "{VALUE} мм") },

                { o.GetOptionWarningLocaleID(nameof(HeadTrackOptions.ResetToDefaults)),
                    T("Reset every HeadTrack option to its default? Keys keep their bindings.",
                        "Сбросить все настройки HeadTrack к значениям по умолчанию? Клавиши не меняются.") },

                { o.GetBindingMapLocaleID(), "HeadTrack" },
                { o.GetBindingKeyLocaleID(HeadTrackOptions.ToggleAction), T("Head tracking on/off", "Отслеживание головы вкл./выкл.") },
                { o.GetBindingKeyLocaleID(HeadTrackOptions.CentreAction), T("Centre", "Центр") },
                { o.GetBindingKeyLocaleID(HeadTrackOptions.HoldAction), T("Hold the view", "Удержать вид") },
            };

            void Option(string name, string label, string labelRu, string description, string descriptionRu)
            {
                d[o.GetOptionLabelLocaleID(name)] = T(label, labelRu);
                d[o.GetOptionDescLocaleID(name)] = T(description, descriptionRu);
            }

            Option(nameof(HeadTrackOptions.Enabled), "Head tracking", "Отслеживание головы",
                "Move the camera with your head, as if the screen were a window into the city.",
                "Камера следует за головой, как будто экран — окно в город.");
            Option(nameof(HeadTrackOptions.Mode), "Mode", "Режим",
                "Centred: with your head where it was when centred you see the game's own view. Window: what a real window would show from where you sit.",
                "По центру: в позе, взятой за центр, картинка совпадает с обычной камерой игры. Окно: вид такой, каким он был бы из настоящего окна с того места, где вы сидите.");
            Option(nameof(HeadTrackOptions.Gain), "Gain", "Усиление",
                "How far the view moves when you move your head sideways or up and down. 1 is physically correct; 3 to 5 lets you look around a building.",
                "Насколько смещается вид, когда голова движется в стороны и вверх-вниз. 1 — физически точно; 3–5 — можно заглянуть за здание.");
            Option(nameof(HeadTrackOptions.DepthGain), "Depth gain", "Усиление по глубине",
                "How far the view moves when you lean towards or away from the screen. 0 ignores leaning.",
                "Насколько смещается вид, когда вы наклоняетесь к экрану или от него. 0 — наклоны не учитываются.");
            Option(nameof(HeadTrackOptions.WindowDepth), "Window depth", "Глубина окна",
                "Where the window plane sits, as a fraction of the distance to the point the camera looks at: what lies in that plane stays still.",
                "Где плоскость окна, как доля расстояния до точки, на которую смотрит камера: всё, что лежит в этой плоскости, остаётся на месте.");
            Option(nameof(HeadTrackOptions.Smoothing), "Smoothing", "Сглаживание",
                "More keeps the view steadier when your head is still; quick moves keep their speed. 0 turns smoothing off.",
                "Больше — вид спокойнее, когда голова неподвижна; быстрые движения не замедляются. 0 — без сглаживания.");
            Option(nameof(HeadTrackOptions.ScreenWidthMm), "Screen width", "Ширина экрана",
                "The width of the picture on your monitor.", "Ширина изображения на мониторе.");
            Option(nameof(HeadTrackOptions.ScreenHeightMm), "Screen height", "Высота экрана",
                "The height of the picture on your monitor.", "Высота изображения на мониторе.");
            Option(nameof(HeadTrackOptions.ViewingDistanceMm), "Viewing distance", "Расстояние до экрана",
                "How far your eyes are from the screen when you sit normally (OpenTrack sends offsets from this pose).",
                "Расстояние от глаз до экрана в обычной позе (OpenTrack передаёт смещения от неё).");
            Option(nameof(HeadTrackOptions.ToggleKey), "Head tracking on/off", "Отслеживание головы вкл./выкл.",
                "Switches head tracking on or off.", "Включает и выключает отслеживание головы.");
            Option(nameof(HeadTrackOptions.CentreKey), "Centre", "Центр",
                "Takes your current head pose as the neutral one.", "Берёт текущее положение головы за нейтральное.");
            Option(nameof(HeadTrackOptions.HoldKey), "Hold the view", "Удержать вид",
                "While held, the view stays where it is and you can move your head freely; on release it glides back.",
                "Пока клавиша нажата, вид стоит на месте и голову можно двигать свободно; после отпускания вид плавно возвращается.");
            Option(nameof(HeadTrackOptions.HoldMouseButton), "Hold the view (mouse)", "Удержать вид (мышь)",
                "A mouse button for the same.", "Кнопка мыши для того же.");
            Option(nameof(HeadTrackOptions.ResetKeys), "Reset keys", "Сбросить клавиши",
                "Puts the keys back to their defaults.", "Возвращает клавиши по умолчанию.");
            Option(nameof(HeadTrackOptions.FollowCamera), "When following a citizen or vehicle", "При слежении за жителем или транспортом",
                "Head tracking also moves the follow camera.", "Голова двигает и камеру слежения.");
            Option(nameof(HeadTrackOptions.PhotoMode), "In photo mode", "В фоторежиме",
                "Head tracking also moves the photo mode camera; screenshots then show the shifted view.",
                "Голова двигает и камеру фоторежима; снимки тогда будут со смещённым видом.");
            Option(nameof(HeadTrackOptions.WhileBuilding), "While a building tool is active", "Пока выбран инструмент строительства",
                "Full: as always. Gain at most 1: a high gain is lowered so the world under the cursor moves less. Off: the view stays still.",
                "Полностью: как обычно. Усиление не больше 1: большое усиление снижается, чтобы мир под курсором двигался меньше. Выкл.: вид не двигается.");
            Option(nameof(HeadTrackOptions.Source), "Pose source", "Источник положения",
                "OpenTrack: its \"UDP over network\" output, centred by OpenTrack. Absolute: a sender that gives the eye's position from the screen centre; the mod takes the first pose as the centre.",
                "OpenTrack: его выход «UDP over network», центрирует сам OpenTrack. Абсолютно: отправитель передаёт положение глаза от центра экрана; первая поза берётся за центр.");
            Option(nameof(HeadTrackOptions.PortText), "UDP port", "UDP-порт",
                "The port the head poses arrive on (OpenTrack's \"UDP over network\" output, 127.0.0.1); 1024 to 65535.",
                "Порт, на который приходят данные о голове (выход OpenTrack «UDP over network», 127.0.0.1); от 1024 до 65535.");
            Option(nameof(HeadTrackOptions.ListenerStatus), "Status", "Состояние",
                "Which port the mod listens on now. A warning: not the one above (busy or not allowed); HeadTrack.log says why.",
                "Какой порт мод сейчас слушает. Предупреждение: не тот, что выше (занят или недоступен); причина — в HeadTrack.log.");
            Option(nameof(HeadTrackOptions.InvertX), "Invert left/right", "Инвертировать влево/вправо",
                "Flips the tracker's sideways axis.", "Меняет направление оси трекера влево-вправо.");
            Option(nameof(HeadTrackOptions.InvertY), "Invert up/down", "Инвертировать вверх/вниз",
                "Flips the tracker's vertical axis.", "Меняет направление вертикальной оси трекера.");
            Option(nameof(HeadTrackOptions.InvertZ), "Invert forward/back", "Инвертировать вперёд/назад",
                "Flips the tracker's axis towards the screen.", "Меняет направление оси трекера к экрану и от него.");
            Option(nameof(HeadTrackOptions.ResetToDefaults), "Reset to defaults", "Сбросить настройки",
                "Puts every HeadTrack option back to its default.", "Возвращает все настройки HeadTrack к значениям по умолчанию.");
            return d;
        }
    }
}
