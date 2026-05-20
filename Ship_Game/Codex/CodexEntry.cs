using SDUtils;
using Ship_Game.Data.Serialization;

namespace Ship_Game.Codex
{
    [StarDataType]
    public sealed class CodexEntry
    {
        [StarData] public string UID;
        [StarData] public string TitleId;
        [StarData] public string ShortDescId;
        [StarData] public string TextId;
        [StarData] public string Link;
        [StarData] public string VideoPath;
        [StarData] public Array<CodexEntry> Children;
    }
}
