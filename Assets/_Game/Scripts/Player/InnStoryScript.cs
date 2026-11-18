namespace FengChenInn
{
    // Spoken lines follow the user's revised departure / return-to-help story.
    // Existing standing dialogue poses carry scene directions without new clips.
    internal static class InnStoryScript
    {
        internal const string EliteBeforeIntroduction = "斗笠客";
        internal const string EliteAfterIntroduction = "虬髯客";

        internal static readonly (string speaker, string line)[] Sentry =
        {
            ("驻守小兵", "不必找了。今夜这里不做生意。"),
            ("红拂女", "你是店家？"),
            ("驻守小兵", "红拂姑娘，府里的人找了一夜。倒叫我先等着了。"),
            ("红拂女", "劳你回一句，我不回去了。"),
            ("驻守小兵", "这话，留着回府自己说。"),
            ("红拂女", "让开。"),
            ("驻守小兵", "把剑放下。我还能好好带你走。"),
            ("红拂女", "要拿我，过来。")
        };

        internal static readonly (string speaker, string line)[] BeforeFight =
        {
            ("斗笠客", "杨府的人？"),
            ("红拂女", "他们找我。"),
            ("斗笠客", "找得倒不客气。"),
            ("红拂女", "客官，可曾见过一个姓李的书生？"),
            ("斗笠客", "李靖？"),
            ("红拂女", "是他。"),
            ("斗笠客", "你找他做什么？"),
            ("红拂女", "寻他同行。"),
            ("斗笠客", "先倒了一个杨府的人，再来问李靖。你与杨府有仇？"),
            ("红拂女", "我从杨府出来。我叫红拂，他见过我。"),
            ("斗笠客", "你是离府寻人，还是借寻人的话报仇？"),
            ("红拂女", "我若要报仇，何必离开杨府？"),
            ("红拂女", "他从后头走的？"),
            ("斗笠客", "已经先行了。"),
            ("红拂女", "往哪里？"),
            ("斗笠客", "一个人的行踪，我不能只凭你几句话便交出去。"),
            ("斗笠客", "先留下，把事情说清。"),
            ("红拂女", "我已经说清了。"),
            ("斗笠客", "把剑放下。我问明，自会让你走。"),
            ("红拂女", "前路有人追我。我不能等。"),
            ("斗笠客", "李靖也往那边去了。你的来意未明，我不能让你追过去。"),
            ("红拂女", "你替杨府拿人？"),
            ("斗笠客", "我替一个朋友留神。"),
            ("红拂女", "他若在这里，自会认我。你若真是他的朋友，就让我去见他。"),
            ("斗笠客", "若是我看错，自会赔罪。眼下，先留下。"),
            ("红拂女", "我不伤你。路，我要过。")
        };

        internal static readonly (string speaker, string line)[] Victory =
        {
            ("斗笠客", "你的剑，倒不像是为寻仇而练的。"),
            ("红拂女", "我本就不是来寻仇的。"),
            ("斗笠客", "那你为何一定要找李靖？"),
            ("红拂女", "他曾到过杨府。满堂的人只等座上发话，唯有他敢把自己的话说完。"),
            ("斗笠客", "所以你便记住了他？"),
            ("红拂女", "我记住的是，他敢走自己的路。"),
            ("红拂女", "临走时，我问了他的住处。等我寻去，他已上路。我想亲自问问，他愿不愿与我同行。"),
            ("斗笠客", "原来如此。看来是我多心了。"),
            ("红拂女", "阁下如今可信了？"),
            ("斗笠客", "信与不信，至少不该再拦你的路。"),
            ("斗笠客", "路已经让出来了。怎么还不走？"),
            ("红拂女", "这便走。告辞。"),
            ("红拂女", "还未请教阁下名姓。"),
            ("斗笠客", "江湖过客，姓名何足挂齿。")
        };

        // Heard from outside while the real front-door reinforcement queue enters.
        internal static readonly (string speaker, string line)[] Reinforcements =
        {
            ("追兵领头 · 堂内", "人呢？那杨府逃出来的女子，去了何处？"),
            ("斗笠客 · 堂内", "人已经走了。"),
            ("追兵领头 · 堂内", "往哪里走了？"),
            ("斗笠客 · 堂内", "这我便不知道了。"),
            ("追兵 · 堂内", "头儿！后门！"),
            ("斗笠客 · 堂内", "诸位，何必苦苦相逼？这江湖路，可不是杨府一家的。")
        };

        internal static readonly (string speaker, string line)[] ReturnToHelp =
        {
            ("斗笠客", "后门在那头。"),
            ("红拂女", "我知道。"),
            ("斗笠客", "那还回来？"),
            ("红拂女", "这场事因我而起，岂能只让你挡着。"),
            ("斗笠客", "好！留意他们身后的人！"),
            ("追兵领头", "一个也别放走！")
        };

        internal static readonly (string speaker, string line)[] Defeat =
        {
            ("斗笠客", "停手。我不取你性命。"),
            ("红拂女", "也不肯让我走？"),
            ("斗笠客", "先把话说清。")
        };

        internal static readonly (string speaker, string line)[] TutorialDefeat =
        {
            ("驻守小兵", "别动了。等他们来了，一道回府。"),
            ("红拂女", "我还没答应。"),
            ("驻守小兵", "这回，由不得你。")
        };

        internal static readonly (string speaker, string line)[] RescueDefeat =
        {
            ("追兵领头", "她走不动了！守住门！"),
            ("斗笠客", "红拂，退到我身后。"),
            ("红拂女", "……我还能——"),
            ("斗笠客", "先站稳。")
        };

        internal static readonly (string speaker, string line)[] Farewell =
        {
            ("红拂女", "今夜多谢阁下。"),
            ("斗笠客", "你若不回来，我倒还能少费些力气。"),
            ("红拂女", "那我岂不是欠你更大的人情？"),
            ("斗笠客", "倒是个有趣的人。"),
            ("红拂女", "怎么称呼？"),
            ("斗笠客", "江湖上的朋友，叫我虬髯客。"),
            ("红拂女", "红拂。"),
            ("虬髯客", "这回记下了。"),
            ("红拂女", "李靖走了多久？"),
            ("虬髯客", "他比这场雨走得早。从后门出去，沿着驿路往前去了。"),
            ("虬髯客", "前头有处驿亭，他说要在那里歇脚。"),
            ("红拂女", "那我还赶得上。"),
            ("虬髯客", "雨还没停。此地不宜久留。"),
            ("红拂女", "江湖路远，总有雨停的时候。"),
            ("红拂女", "多谢。你的情，我记下了。"),
            ("虬髯客", "朋友间的话，留待下回说。先赶路。"),
            ("红拂女", "掌柜的账，也被我们耽搁了。"),
            ("虬髯客", "这边我留下照应。你去吧。"),
            ("虬髯客", "红拂。后会有期。"),
            ("红拂女", "后会有期。")
        };

        // The protected ally normally remains standing. Preserve the fallback
        // caller without restoring the obsolete Yang-house pursuer identity.
        internal static readonly (string speaker, string line)[] FarewellWhileAllyDown = Farewell;

        internal const string Arrival = "店家。借个避雨的地方。\n账还摊着……人呢？";
        internal const string TutorialVictory = "红拂女 · 连这样的地方也守着……\n门外又响起了脚步声。";
        internal const string TutorialOpening = "驻守小兵 · 今夜这道门，你过不去！";
        internal const string LedgerObservation = "红拂女 · 写到这里，连笔都没收。";
        internal const string RescueDiscovery = "身后响起急促的金铁声。\n斗笠客 · 红拂姑娘快走！他们为追你而来，我掩护你！";
        internal const string LedgerEntries = "初六\n住客二，房钱已收。\n\n"
            + "初七\n买灯油一壶，钱已付。\n\n"
            + "初八\n夜雨。添炭。\n\n"
            + "新到客——";
        internal const string CombatLesson = "看清起手，等对手收势再反击。留些体力，不要一味追着出剑。";
        internal const string VictoryResult = "此夜得脱，前路未尽。";
        internal const string ClosingTitle = "风尘未歇";
        internal const string TutorialDefeatResult = "去路被截。";
        internal const string EliteDefeatResult = "去路未开。";
        internal const string RescueDefeatResult = "突围未成。";
        internal const string AICredit = "AI 辅助制作 · Aholo Lux3D / GPT";
    }
}
