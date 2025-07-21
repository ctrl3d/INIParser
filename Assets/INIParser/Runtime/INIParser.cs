using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace work.ctrl3d.Config
{
    public class INIParser
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections;
        private readonly string _filePath;
        private const string GlobalSection = "";

        public class INIParserException : Exception
        {
            public INIParserException(string message) : base(message)
            {
            }

            public INIParserException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }

        public INIParser(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty.");
            }

            _filePath = filePath;
            _sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Load();
            }
            catch (FileNotFoundException)
            {
                // File doesn't exist, proceed with empty sections. Will be created on Save().
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or SecurityException or RegexMatchTimeoutException)
            {
                throw new INIParserException(
                    $"Failed to load INI file '{_filePath}'. See inner exception for details.",
                    ex
                );
            }
        }

        private void Load()
        {
            if (!File.Exists(_filePath))
            {
                throw new FileNotFoundException($"INI file not found at path: '{_filePath}'", _filePath);
            }

            _sections.Clear();

            var currentSection = GlobalSection;
            _sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var sectionRegex = new Regex(@"^\s*\[\s*(?<section>[^\]]+?)\s*\]\s*$", RegexOptions.Compiled);
            var keyValueRegex = new Regex(@"^\s*(?<key>[^=]+?)\s*=\s*(?<value>.*)\s*$", RegexOptions.Compiled);
            var commentRegex = new Regex(@"^\s*[;#]", RegexOptions.Compiled);

            try
            {
                foreach (var line in File.ReadLines(_filePath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (commentRegex.IsMatch(line))
                        continue;

                    var sectionMatch = sectionRegex.Match(line);
                    if (sectionMatch.Success)
                    {
                        currentSection = sectionMatch.Groups["section"].Value.Trim();
                        if (!_sections.ContainsKey(currentSection))
                        {
                            _sections[currentSection] =
                                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        }

                        continue;
                    }

                    var keyValueMatch = keyValueRegex.Match(line);
                    if (!keyValueMatch.Success) continue;

                    var key = keyValueMatch.Groups["key"].Value.Trim();
                    var value = keyValueMatch.Groups["value"].Value.Trim();
                    _sections[currentSection][key] = value;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or SecurityException or RegexMatchTimeoutException)
            {
                throw; // Re-throw to be caught and wrapped by the constructor
            }
        }

        public void SetValue(string section, string key, string value)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (key == null) throw new ArgumentNullException(nameof(key));

            var targetSection = string.IsNullOrEmpty(section) ? GlobalSection : section;

            if (!_sections.TryGetValue(targetSection, out var sectionDict))
            {
                sectionDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections[targetSection] = sectionDict;
            }

            sectionDict[key] = value ?? string.Empty;
        }

        public string GetValue(string section, string key, string defaultValue = "")
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (key == null) throw new ArgumentNullException(nameof(key));

            var targetSection = string.IsNullOrEmpty(section) ? GlobalSection : section;

            if (_sections.TryGetValue(targetSection, out var sectionDict) &&
                sectionDict.TryGetValue(key, out var value))
            {
                return value;
            }

            return defaultValue;
        }

        public Dictionary<string, string> GetSection(string section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));

            var targetSection = string.IsNullOrEmpty(section) ? GlobalSection : section;

            return _sections.TryGetValue(targetSection, out var resultSection)
                ? new Dictionary<string, string>(resultSection, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool HasSection(string section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            var targetSection = string.IsNullOrEmpty(section) ? GlobalSection : section;
            return _sections.ContainsKey(targetSection);
        }

        public bool HasKey(string section, string key)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (key == null) throw new ArgumentNullException(nameof(key));

            var targetSection = string.IsNullOrEmpty(section) ? GlobalSection : section;

            return _sections.TryGetValue(targetSection, out var sectionDict) && sectionDict.ContainsKey(key);
        }

        public bool DeleteKey(string section, string key)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (key == null) throw new ArgumentNullException(nameof(key));

            var targetSection = string.IsNullOrEmpty(section) ? GlobalSection : section;

            return _sections.TryGetValue(targetSection, out var sectionDict) && sectionDict.Remove(key);
        }

        public bool DeleteSection(string section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));

            var targetSection = string.IsNullOrEmpty(section) ? GlobalSection : section;

            if (targetSection != GlobalSection) return _sections.Remove(targetSection);
            if (!_sections.TryGetValue(GlobalSection, out var globalDict)) return false;
            globalDict.Clear();
            return true;
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var writer = new StreamWriter(_filePath, false, new UTF8Encoding(false));

                if (_sections.TryGetValue(GlobalSection, out var globalDict) && globalDict.Count > 0)
                {
                    foreach (var keyValue in globalDict.Where(keyValue => !string.IsNullOrEmpty(keyValue.Key)))
                    {
                        writer.WriteLine($"{keyValue.Key}={keyValue.Value}");
                    }

                    if (_sections.Count > 1)
                    {
                        writer.WriteLine();
                    }
                }


                foreach (var sectionPair in _sections.Where(sectionPair => sectionPair.Key != GlobalSection))
                {
                    writer.WriteLine($"[{sectionPair.Key}]");

                    foreach (var keyValue in sectionPair.Value.Where(keyValue => !string.IsNullOrEmpty(keyValue.Key)))
                    {
                        writer.WriteLine($"{keyValue.Key}={keyValue.Value}");
                    }

                    writer.WriteLine();
                }
            }

            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or SecurityException
                                           or DirectoryNotFoundException)
            {
                throw new INIParserException(
                    $"Failed to save INI file '{_filePath}'. See inner exception for details.",
                    ex
                );
            }

            catch (Exception ex)
            {
                throw new INIParserException($"An unexpected error occurred while saving INI file '{_filePath}'.", ex);
            }
        }
    }
}