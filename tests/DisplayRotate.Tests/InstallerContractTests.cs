using System.Text;
using System.Text.RegularExpressions;
using DisplayRotate.Core;
using DisplayRotate.Core.Updates;

namespace DisplayRotate.Tests;

/// <summary>
/// The installer script, the app and the release workflow agree on names that nothing checks at
/// run time: a renamed mutex, a misspelled switch or a MessageBox without a silent default all
/// fail silently, on someone else's machine. These tests are where they fail loudly instead.
/// </summary>
public partial class InstallerContractTests
{
    private static string Nsi() => RepoRoot.Read("packaging", "displayrotate.nsi");

    private static string Define(string name)
    {
        var match = Regex.Match(Nsi(), $@"^!define\s+{name}\s+""([^""]*)""", RegexOptions.Multiline);
        Assert.True(match.Success, $"packaging/displayrotate.nsi defines no {name}");
        return match.Groups[1].Value;
    }

    [Fact]
    public void TheInstallerFindsAndClosesTheAppByTheNamesTheAppUses()
    {
        Assert.Equal(Names.SingleInstanceMutex, Define("MUTEX_NAME"));
        Assert.Equal(Names.QuitEvent, Define("QUIT_EVENT"));
    }

    [Fact]
    public void TheAppReadsTheEntryTheInstallerWrites()
    {
        Assert.Equal(Names.UninstallKey, Define("UNINST_KEY").Replace("${APP}", Define("APP"), StringComparison.Ordinal));
        Assert.Equal(Names.RunKey, Define("RUN_KEY"));
        Assert.Equal(Names.RunValue, Define("APP"));
        Assert.Equal(ProductInfo.Name + ".exe", Define("EXE"));
        Assert.Contains($@"""{Names.BuildVariantValue}""", Nsi(), StringComparison.Ordinal);
        Assert.Contains($@"$TEMP\{Names.UpdateDirectoryPrefix}*", Nsi(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheInstallerRecordsEveryBuildTheUpdaterCanAskFor()
    {
        foreach (var variant in new[] { ReleaseAssets.Full, ReleaseAssets.Min })
        {
            Assert.Contains($@"""BuildVariant""    ""{variant}""", Nsi(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// ${GetOptions} ignores a switch nobody asks for, so a misspelled one is not an error - it
    /// is an update that quietly installs into the wrong scope, or never comes back.
    /// </summary>
    [Fact]
    public void EverySwitchTheUpdaterPassesIsOneTheInstallerReads()
    {
        var read = Regex.Matches(Nsi(), @"\$\{GetOptions\}\s+\$\w+\s+""(/[A-Za-z]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        read.Add("/S"); // NSIS's own

        var passed = InstallerArguments.For(InstallScope.PerUser).Concat(InstallerArguments.For(InstallScope.PerMachine)).Distinct();

        Assert.All(passed, argument => Assert.Contains(argument, read));
    }

    [Fact]
    public void EverySilentInstallHasADefault()
    {
        var boxes = new List<int>();
        var undefaulted = new List<int>();

        foreach (var instruction in ReadNsis(Nsi()))
        {
            if (!NsisMessageBox().IsMatch(instruction.Code))
            {
                continue;
            }

            boxes.Add(instruction.Line);

            if (!NsisSilentDefault().IsMatch(instruction.Code))
            {
                undefaulted.Add(instruction.Line);
            }
        }

        // Zero matches is not a clean bill of health, it is a scanner that stopped understanding
        // its input.
        Assert.True(boxes.Count > 4, "found almost no MessageBox at all, so nothing was asserted");

        // A MessageBox with no /SD hangs a silent install for ever on a dialog nobody can see.
        Assert.Empty(undefaulted);
    }

    [Fact]
    public void TheSilentDefaultScannerReadsNsisRatherThanText()
    {
        const string Script = """
            ; MessageBox MB_OK "a commented-out box is not a call"
            Function Cases
              MessageBox MB_OK "plain, with a default" /SD IDOK
              MessageBox MB_OK "plain, without one"
              messagebox MB_OK "lowercase, without one"
              MessageBox MB_OK "a message that mentions /SD IDOK is still without one"
              MessageBox MB_YESNO "continued over lines" \
                /SD IDNO IDNO +2
              MessageBox MB_OK "an escaped $\" quote, then a default" /SD IDOK
            FunctionEnd
            """;

        var found = ReadNsis(Script).Where(i => NsisMessageBox().IsMatch(i.Code)).ToList();

        Assert.Equal([3, 4, 5, 6, 7, 9], found.Select(i => i.Line));
        Assert.Equal([4, 5, 6], found.Where(i => !NsisSilentDefault().IsMatch(i.Code)).Select(i => i.Line));
    }

    /// <summary>
    /// A release must wait for every check. An <c>if:</c> with a status function (always(),
    /// success() ...) or a job missing from <c>needs:</c> lets a red commit ship.
    /// </summary>
    [Fact]
    public void NothingIsReleasedWithoutEveryCheckHavingPassed()
    {
        var jobs = ReadJobs(RepoRoot.Read(".github", "workflows", "ci.yml"));

        Assert.True(jobs.ContainsKey("release"), "ci.yml has no release job");
        var release = jobs["release"];
        var needs = Needs(release);

        var checks = jobs.Keys.Where(name => name != "release").ToList();
        Assert.True(checks.Count >= 4, "found almost no jobs, so the parser is not reading ci.yml");
        Assert.All(checks, check => Assert.Contains(check, needs));

        Assert.DoesNotMatch(@"(always|success|failure|cancelled)\(\)", release);
    }

    [Fact]
    public void EveryJobDeclaresAHangBudget()
    {
        var jobs = ReadJobs(RepoRoot.Read(".github", "workflows", "ci.yml"));
        Assert.All(jobs, job => Assert.Matches(@"(?m)^    timeout-minutes:\s*\d+", job.Value));
    }

    /// <summary>ci.yml's jobs, name to text, by indentation: two spaces under <c>jobs:</c>.</summary>
    private static Dictionary<string, string> ReadJobs(string yaml)
    {
        var lines = yaml.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var jobs = new Dictionary<string, string>(StringComparer.Ordinal);
        var inJobs = false;
        string? current = null;
        var text = new StringBuilder();

        void Flush()
        {
            if (current is not null)
            {
                jobs[current] = text.ToString();
            }

            text.Clear();
        }

        foreach (var line in lines)
        {
            if (line == "jobs:")
            {
                inJobs = true;
                continue;
            }

            if (!inJobs)
            {
                continue;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
            {
                break; // the next top-level key
            }

            var job = Regex.Match(line, @"^  ([A-Za-z0-9_-]+):\s*$");
            if (job.Success)
            {
                Flush();
                current = job.Groups[1].Value;
                continue;
            }

            text.AppendLine(line);
        }

        Flush();
        return jobs;
    }

    private static HashSet<string> Needs(string job)
    {
        var match = Regex.Match(job, @"(?m)^    needs:\s*\[([^\]]*)\]");
        Assert.True(match.Success, "the release job has no needs: [ ... ] list");
        return match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    }

    /// <summary>One NSIS instruction, with its string literals and comments removed.</summary>
    private readonly record struct NsisInstruction(int Line, string Code);

    /// <summary>
    /// Reads an NSIS script the way the compiler does, near enough to judge one rule: line
    /// continuations are joined (a MessageBox's /SD often lives on the last line), and strings
    /// and comments are removed, so a quoted message cannot satisfy or break a rule about the
    /// instruction around it. Ported from WinLogRotate's ArchitectureTests.
    /// </summary>
    private static IReadOnlyList<NsisInstruction> ReadNsis(string text)
    {
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var instructions = new List<NsisInstruction>();

        for (var i = 0; i < lines.Length; i++)
        {
            var start = i;
            var joined = new StringBuilder();

            while (true)
            {
                var line = lines[i];
                if (line.EndsWith('\\') && i + 1 < lines.Length)
                {
                    joined.Append(line, 0, line.Length - 1);
                    i++;
                    continue;
                }

                joined.Append(line);
                break;
            }

            instructions.Add(new NsisInstruction(start + 1, StripStringsAndComments(joined.ToString())));
        }

        return instructions;
    }

    private static string StripStringsAndComments(string line)
    {
        var code = new StringBuilder(line.Length);

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c is ';' or '#')
            {
                break;
            }

            if (c is not ('"' or '\'' or '`'))
            {
                code.Append(c);
                continue;
            }

            // A string. Consume to its close, honouring NSIS's $\" escape, and emit nothing.
            var quote = c;
            for (i++; i < line.Length; i++)
            {
                if (line[i] == '$' && i + 2 < line.Length && line[i + 1] == '\\' && line[i + 2] == quote)
                {
                    i += 2;
                    continue;
                }

                if (line[i] == quote)
                {
                    break;
                }
            }
        }

        return code.ToString();
    }

    [GeneratedRegex(@"^\s*MessageBox\b", RegexOptions.IgnoreCase)]
    private static partial Regex NsisMessageBox();

    [GeneratedRegex(@"(^|\s)/SD\s", RegexOptions.IgnoreCase)]
    private static partial Regex NsisSilentDefault();
}
