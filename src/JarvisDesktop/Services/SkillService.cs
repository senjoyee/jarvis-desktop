using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace JarvisDesktop.Services;

public record SkillDefinition(string Name, string Description, string Path);

public class SkillService
{
    private readonly string _skillsRoot;
    private readonly ILogger<SkillService> _logger;

    public SkillService(ILogger<SkillService> logger)
    {
        _logger = logger;
        // Use path relative to the application's base directory
        _skillsRoot = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "skills");
        _skillsRoot = Path.GetFullPath(_skillsRoot);
    }

    public List<SkillDefinition> GetSkills()
    {
        var skills = new List<SkillDefinition>();
        
        try
        {
            if (!Directory.Exists(_skillsRoot))
            {
                // Create directory if it doesn't exist
                Directory.CreateDirectory(_skillsRoot);
                _logger.LogInformation("Created skills directory at {Path}", _skillsRoot);
                return skills;
            }

            var files = Directory.GetFiles(_skillsRoot, "SKILL.md", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var skill = ParseSkill(file);
                if (skill != null)
                {
                    skills.Add(skill);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing skills from {Path}", _skillsRoot);
        }

        return skills;
    }

    private SkillDefinition? ParseSkill(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            
            // Extract YAML frontmatter
            var match = Regex.Match(content, @"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n", RegexOptions.Singleline);
            if (!match.Success)
            {
                _logger.LogWarning("Skipping skill at {Path}: No valid YAML frontmatter found", path);
                return null;
            }

            var yaml = match.Groups[1].Value;
            
            // Simple parsing of name and description
            var nameMatch = Regex.Match(yaml, @"^name:\s*(.+)$", RegexOptions.Multiline);
            var descMatch = Regex.Match(yaml, @"^description:\s*(.+)$", RegexOptions.Multiline);

            if (!nameMatch.Success)
            {
                 _logger.LogWarning("Skipping skill at {Path}: No 'name' field in frontmatter", path);
                 return null;
            }

            var name = nameMatch.Groups[1].Value.Trim();
            // Handle optional quotes
            if ((name.StartsWith("\"") && name.EndsWith("\"")) || (name.StartsWith("'") && name.EndsWith("'")))
            {
                name = name.Substring(1, name.Length - 2);
            }

            var description = descMatch.Success ? descMatch.Groups[1].Value.Trim() : "";
            if ((description.StartsWith("\"") && description.EndsWith("\"")) || (description.StartsWith("'") && description.EndsWith("'")))
            {
                description = description.Substring(1, description.Length - 2);
            }

            return new SkillDefinition(name, description, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing skill at {Path}", path);
            return null;
        }
    }
}
