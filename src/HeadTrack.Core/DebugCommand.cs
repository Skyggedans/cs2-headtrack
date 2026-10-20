using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HeadTrack.Core
{
    public enum DebugVerb
    {
        /// <summary>Save a screenshot under a plain file name.</summary>
        Shot,

        /// <summary>Log the camera state and reply with it.</summary>
        Dump,

        /// <summary>Switch the effect on (1) or off (0).</summary>
        Enable,

        /// <summary>Change a setting: set &lt;name&gt; &lt;value&gt;.</summary>
        Set,

        /// <summary>Pause (1) or resume (0) the simulation.</summary>
        Pause,

        /// <summary>Take the current head pose as the centre.</summary>
        Centre,

        /// <summary>Quit the game the way the menu does, without saving.</summary>
        Quit,
    }

    /// <summary>
    /// One line of the debug channel's text protocol, a UDP datagram from
    /// localhost such as "shot centre-01", "set gain 3" or "quit".
    /// </summary>
    public readonly struct DebugCommand
    {
        private static readonly Regex s_FileName = new Regex("^[A-Za-z0-9_.-]{1,64}$", RegexOptions.CultureInvariant);

        private DebugCommand(DebugVerb verb, string argument, string name, string value)
        {
            Verb = verb;
            Argument = argument;
            Name = name;
            Value = value;
        }

        public DebugVerb Verb { get; }

        /// <summary>The single argument (shot name, 0/1), or empty.</summary>
        public string Argument { get; }

        /// <summary>For <see cref="DebugVerb.Set"/>: the setting's name.</summary>
        public string Name { get; }

        /// <summary>For <see cref="DebugVerb.Set"/>: the value as text.</summary>
        public string Value { get; }

        public static bool TryParse(string? text, out DebugCommand command, out string? error)
        {
            command = default;
            error = null;
            string[] words = (text ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                error = "empty command";
                return false;
            }
            string verb = words[0].ToLowerInvariant();
            switch (verb)
            {
                case "shot":
                    if (words.Length != 2 || !s_FileName.IsMatch(words[1]) || words[1].Contains(".."))
                    {
                        error = "usage: shot <name> (letters, digits, _ . -)";
                        return false;
                    }
                    command = new DebugCommand(DebugVerb.Shot, words[1], string.Empty, string.Empty);
                    return true;
                case "enable":
                case "pause":
                    if (words.Length != 2 || (words[1] != "0" && words[1] != "1"))
                    {
                        error = $"usage: {verb} 0|1";
                        return false;
                    }
                    command = new DebugCommand(verb == "enable" ? DebugVerb.Enable : DebugVerb.Pause, words[1], string.Empty, string.Empty);
                    return true;
                case "set":
                    if (words.Length != 3)
                    {
                        error = "usage: set <name> <value>";
                        return false;
                    }
                    command = new DebugCommand(DebugVerb.Set, string.Empty, words[1].ToLowerInvariant(), words[2]);
                    return true;
                case "dump":
                case "centre":
                case "quit":
                    if (words.Length != 1)
                    {
                        error = $"usage: {verb}";
                        return false;
                    }
                    DebugVerb plain = verb == "dump" ? DebugVerb.Dump : verb == "centre" ? DebugVerb.Centre : DebugVerb.Quit;
                    command = new DebugCommand(plain, string.Empty, string.Empty, string.Empty);
                    return true;
                default:
                    error = $"unknown command '{words[0]}'";
                    return false;
            }
        }
    }

    /// <summary>
    /// "key = value" lines with '#' comments, keys case-insensitive, later
    /// lines winning; malformed lines are collected as errors, not thrown.
    /// </summary>
    public sealed class KeyValueConfig
    {
        private readonly Dictionary<string, string> m_Values;

        private KeyValueConfig(Dictionary<string, string> values, List<string> errors)
        {
            m_Values = values;
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }

        public static KeyValueConfig Parse(string? text)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            string[] lines = (text ?? string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                int equals = line.IndexOf('=');
                string key = equals > 0 ? line.Substring(0, equals).Trim() : string.Empty;
                if (equals < 0 || key.Length == 0)
                {
                    errors.Add($"line {i + 1}: expected 'key = value', got '{line}'");
                    continue;
                }
                values[key] = line.Substring(equals + 1).Trim();
            }
            return new KeyValueConfig(values, errors);
        }

        public string? Get(string key) => m_Values.TryGetValue(key, out string value) ? value : null;

        public int GetInt(string key, int fallback) =>
            int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

        public double GetDouble(string key, double fallback) =>
            double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;

        public bool GetBool(string key, bool fallback)
        {
            string? value = Get(key);
            if (value == null)
            {
                return fallback;
            }
            switch (value.ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    return fallback;
            }
        }
    }
}
