using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game.Tools.Localization;

namespace UnitTests.Data;

// Verifies LocalizationTool.MergeLangIntoYaml - the pure text transform behind the
// --merge-translations CLI flag. It inserts a Missing.<LANG>.yaml's "<lang>:" rows into the
// matching GameText.yaml entries while leaving every other byte (ordering, newline style,
// trailing newline) unchanged, skipping NameIds that don't exist in the base.
[TestClass]
public class LocalizationMergeTests
{
    const string Yaml =
        "# Version 1\n" +
        "NewGame:\n Id: 1\n ENG: \"New Game\"\n" +
        "LoadGame:\n Id: 2\n ENG: \"Load Game\"\n RUS: \"Zagruzit\"\n";

    [TestMethod]
    public void InsertsLangRowAfterExistingFields()
    {
        const string missing = "# hdr\nNewGame:\n Id: 1\n PTB: \"Novo Jogo\"\n";
        string result = LocalizationTool.MergeLangIntoYaml(Yaml, missing, "PTB", out int merged, out int skipped);

        Assert.AreEqual(1, merged);
        Assert.AreEqual(0, skipped);
        // PTB appended as NewGame's last field, before the next entry
        Assert.AreEqual(
            "# Version 1\n" +
            "NewGame:\n Id: 1\n ENG: \"New Game\"\n PTB: \"Novo Jogo\"\n" +
            "LoadGame:\n Id: 2\n ENG: \"Load Game\"\n RUS: \"Zagruzit\"\n",
            result);
    }

    [TestMethod]
    public void AppendsToLastEntryAndPreservesTrailingNewline()
    {
        const string missing = "LoadGame:\n PTB: \"Carregar\"\n";
        string result = LocalizationTool.MergeLangIntoYaml(Yaml, missing, "PTB", out int merged, out int skipped);

        Assert.AreEqual(1, merged);
        Assert.AreEqual(0, skipped);
        Assert.IsTrue(result.EndsWith(" RUS: \"Zagruzit\"\n PTB: \"Carregar\"\n"),
            $"PTB should land as LoadGame's last field with the trailing newline kept. Got:\n{result}");
    }

    [TestMethod]
    public void SkipsNameIdsNotInBase()
    {
        const string missing = "NotARealToken:\n PTB: \"x\"\nNewGame:\n PTB: \"Novo Jogo\"\n";
        string result = LocalizationTool.MergeLangIntoYaml(Yaml, missing, "PTB", out int merged, out int skipped);

        Assert.AreEqual(1, merged, "only the matching NewGame row merges");
        Assert.AreEqual(1, skipped, "NotARealToken is not in the base and must be skipped");
        Assert.IsFalse(result.Contains("NotARealToken"), "out-of-scope NameId must not be injected");
    }

    [TestMethod]
    public void IsIdempotent_DoesNotDuplicateExistingLang()
    {
        const string missing = "NewGame:\n PTB: \"Novo Jogo\"\n";
        string once = LocalizationTool.MergeLangIntoYaml(Yaml, missing, "PTB", out _, out _);
        string twice = LocalizationTool.MergeLangIntoYaml(once, missing, "PTB", out int merged, out _);

        Assert.AreEqual(0, merged, "second merge should add nothing");
        Assert.AreEqual(once, twice, "re-running the merge must be a no-op");
    }

    [TestMethod]
    public void PreservesCrlfNewlineStyle()
    {
        string crlf = Yaml.Replace("\n", "\r\n");
        const string missing = "NewGame:\n PTB: \"Novo Jogo\"\n";
        string result = LocalizationTool.MergeLangIntoYaml(crlf, missing, "PTB", out int merged, out _);

        Assert.AreEqual(1, merged);
        Assert.IsTrue(result.Contains("\r\n"), "CRLF style must be preserved");
        Assert.IsFalse(result.Contains("\n\n"), "no stray lone-LF should be introduced");
        Assert.IsTrue(result.Contains(" PTB: \"Novo Jogo\"\r\n"), "inserted row must use the file's CRLF");
    }

    [TestMethod]
    public void MatchesTwoSpaceFieldIndentation()
    {
        // A legacy entry whose fields use two-space indentation. Inserting at one space would
        // create a stray dedent and corrupt the YAML structure (the real "Stack empty" bug).
        const string yaml =
            "First:\n ENG: \"First\"\n" +
            "Legacy:\n  Id: 4438\n  ENG: \"Hot Lava\"\n" +
            "Last:\n ENG: \"Last\"\n";
        const string missing = "Legacy:\n PTB: \"Lava Quente\"\n";
        string result = LocalizationTool.MergeLangIntoYaml(yaml, missing, "PTB", out int merged, out _);

        Assert.AreEqual(1, merged);
        // PTB must be inserted with the entry's two-space indent, right after its last field
        Assert.IsTrue(result.Contains("  ENG: \"Hot Lava\"\n  PTB: \"Lava Quente\"\nLast:"),
            $"PTB should mirror the entry's two-space indent. Got:\n{result}");
    }

    [TestMethod]
    public void OnlyMergesTargetLanguage()
    {
        // missing file carries multiple languages; only the requested one is applied
        const string missing = "NewGame:\n GER: \"Neues Spiel\"\n PTB: \"Novo Jogo\"\n";
        string result = LocalizationTool.MergeLangIntoYaml(Yaml, missing, "PTB", out int merged, out _);

        Assert.AreEqual(1, merged);
        Assert.IsTrue(result.Contains(" PTB: \"Novo Jogo\"\n"));
        Assert.IsFalse(result.Contains("GER:"), "non-target language rows must be ignored");
    }
}
