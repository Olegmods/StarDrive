using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using SDGraphics;
using SDUtils;
using Ship_Game.Audio;
using Ship_Game.Codex;
using Ship_Game.Data.Yaml;
using Ship_Game.GameScreens;
using Ship_Game.Utils;
using Vector2 = SDGraphics.Vector2;

// ReSharper disable once CheckNamespace
namespace Ship_Game
{
    public sealed class InGameWiki : PopupWindow
    {
        readonly Array<CodexEntry> Codex;
        ScrollList<WikiHelpCategoryListItem> HelpCategories;
        RectF TextRect;
        Vector2 TitlePosition;
        UITextBox HelpEntries;

        ScreenMediaPlayer Player;
        RectF SmallViewer;
        RectF BigViewer;
        CodexEntry ActiveEntry;
        string ActiveTitle = "";
        string ActiveText = "";

        public InGameWiki(GameScreen parent) : base(parent, 750, 600)
        {
            IsPopup           = true;
            TransitionOnTime  = 0.25f;
            TransitionOffTime = 0.25f;

            // Codex.yaml is a flat array of root categories; each may have nested Children.
            var file = ResourceManager.GetModOrVanillaFile("Codex.yaml");
            Codex = file != null && file.Exists
                ? YamlParser.DeserializeArray<CodexEntry>(file)
                : new Array<CodexEntry>();

            TitleText  = Localizer.Token(GameText.StardriveHelp2);
            MiddleText = Localizer.Token(GameText.ThisHelpMenuContainsInformation);
        }

        public override void LoadContent()
        {
            base.LoadContent();

            TitleText += $" {GlobalStats.ExtendedVersion}";
            if (GlobalStats.HasMod)
            {
                MiddleText = $"Mod Loaded: {GlobalStats.ModName} Ver: {GlobalStats.ActiveMod.Mod.Version}";
            }

            ActiveEntry = null;
            ActiveTitle = Localizer.Token(GameText.StardriveHelp);
            ActiveText  = Localizer.Token(GameText.SelectATopicOnThe);

            RectF CategoriesRect = new(Rect.X + 25, MidSepBot.Y + 10, 330, 430);
            HelpCategories = Add(new ScrollList<WikiHelpCategoryListItem>(CategoriesRect));

            RectF textSlRect = new(CategoriesRect.X + CategoriesRect.W + 5, CategoriesRect.Y + 10, 375, 420);
            HelpEntries = Add(new UITextBox(textSlRect, useBorder:false));
            TextRect = new(HelpCategories.X + HelpCategories.Width + 5, HelpCategories.Y + 10, 375, 420);

            ResetActiveTopic();

            SmallViewer = new(TextRect.X + 20, TextRect.Y + 40, 336, 189);
            BigViewer = new(ScreenWidth / 2 - 640, ScreenHeight / 2 - 360, 1280, 720);
            Player = new(ContentManager)
            {
                EnableInteraction = true,
                MuteGameAudioWhilePlaying = true,
                OnPlayStatusChange = OnPlayerStatusChanged
            };

            // Phase 1: render the top-level categories as ScrollList headers with their
            // direct children as sub-items. Arbitrary nesting is Phase 2.
            foreach (CodexEntry cat in Codex)
            {
                WikiHelpCategoryListItem header = HelpCategories.AddItem(new WikiHelpCategoryListItem(cat));
                if (cat.Children != null)
                {
                    foreach (CodexEntry child in cat.Children)
                        header.AddSubItem(new WikiHelpCategoryListItem(child));
                }
            }
            HelpCategories.OnClick = OnHelpCategoryClicked;
        }

        void ResetActiveTopic()
        {
            HelpEntries.SetLines(ActiveText, Fonts.Arial12Bold, Color.White);
            float titleW = Fonts.Arial20Bold.TextWidth(ActiveTitle);
            TitlePosition = new(TextRect.CenterX - titleW / 2f - 15f, TextRect.Y + 10);
        }

        void OnHelpCategoryClicked(WikiHelpCategoryListItem item)
        {
            // A bare category header has children but no body of its own — clear video
            // and leave the previous selection intact (matches the legacy behavior).
            if (item.IsHeader)
            {
                Player.Stop();
                Player.Visible = false;
                return;
            }

            HelpEntries.Clear();
            ActiveEntry = item.Entry;
            ActiveTitle = ActiveEntry != null && !string.IsNullOrEmpty(ActiveEntry.TitleId)
                ? Localizer.Token(ActiveEntry.TitleId)
                : "";
            ActiveText  = ActiveEntry != null && !string.IsNullOrEmpty(ActiveEntry.TextId)
                ? Localizer.Token(ActiveEntry.TextId)
                : "";

            if (!string.IsNullOrEmpty(ActiveText))
                ResetActiveTopic();

            if (ActiveEntry != null && !string.IsNullOrEmpty(ActiveEntry.Link))
                Log.OpenURL(ActiveEntry.Link);

            if (ActiveEntry == null || string.IsNullOrEmpty(ActiveEntry.VideoPath))
            {
                Player.Stop();
                Player.Visible = false;
            }
            else
            {
                HelpEntries.Clear();
                Player.PlayVideo(ActiveEntry.VideoPath, looping: false, startPaused: true);
                Player.Visible = true;
            }
        }

        void OnPlayerStatusChanged()
        {
            Player.Rect = Player.IsPlaying ? BigViewer : SmallViewer;
        }

        public override bool HandleInput(InputState input)
        {
            if (Player.HandleInput(input))
                return true;

            if (!GlobalStats.TakingInput && input.InGameWiki)
            {
                GameAudio.EchoAffirmative();
                ExitScreen();
                return true;
            }

            return base.HandleInput(input);
        }

        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
            base.Draw(batch, elapsed);

            batch.SafeBegin();

            Player.Draw(batch);
            if (Player.IsPaused)
            {
                batch.DrawRectangleGlow(Player.Rect);
                batch.DrawString(Fonts.Arial20Bold, ActiveTitle, TitlePosition, Color.Orange);
            }

            batch.SafeEnd();
        }

        public override void ExitScreen()
        {
            Player.Stop();
            base.ExitScreen();
        }

        protected override void Dispose(bool disposing)
        {
            if (IsDisposed)
                return;
            Player.Dispose();
            base.Dispose(disposing); // sets IsDisposed = true
        }
    }
}
