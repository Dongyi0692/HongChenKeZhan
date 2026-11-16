namespace FengChenInn
{
    internal static class InnStoryScript
    {
        internal static readonly (string speaker, string line)[] Sentry =
        {
            ("驻守小兵", "账还没看够？从你进门起，我就看着你。"),
            ("红拂女", "掌柜呢？"),
            ("驻守小兵", "后头歇着。我们只等一位紫衣姑娘。"),
            ("红拂女", "那就别碰旁人。你的剑，冲我来。")
        };

        internal static readonly (string speaker, string line)[] BeforeFight =
        {
            ("追兵", "门口就听见了。红拂姑娘，好利落的剑。"),
            ("红拂女", "前面这个，没拦住我。你也要试？"),
            ("追兵", "差事在身，总得试一试。只与你交手，不动旁人。"),
            ("红拂女", "赢了你，便让我走？"),
            ("追兵", "你赢，我让路；你输，随我回去。我不捆你。"),
            ("红拂女", "你说话算数？"),
            ("追兵", "算数。"),
            ("红拂女", "好。")
        };

        internal static readonly (string speaker, string line)[] Victory =
        {
            ("追兵", "我输了。后门通着院子，从那里走。"),
            ("红拂女", "你还守着？"),
            ("追兵", "我替你挡一回。后面还有五个人，不会像我这样讲理。"),
            ("红拂女", "你这样，怎么交差？"),
            ("追兵", "输了就是输了。快走，别回头。")
        };

        internal static readonly (string speaker, string line)[] Reinforcements =
        {
            ("后续小兵 · 门外", "五个人都到齐了。你竟敢放她走？"),
            ("追兵", "她赢了我。我答应放人。"),
            ("后续小兵 · 门外", "你的承诺，抵得过府里的令？让开！"),
            ("追兵", "人是我放的。要交差，冲我来。")
        };

        internal static readonly (string speaker, string line)[] Defeat =
        {
            ("追兵", "剑放下。等雨小些，我带你回去。"),
            ("红拂女", "这场是我输了。"),
            ("追兵", "我说过，不捆你。")
        };

        internal static readonly (string speaker, string line)[] TutorialDefeat =
        {
            ("驻守小兵", "看住她。等后面的人来。")
        };

        internal static readonly (string speaker, string line)[] RescueDefeat =
        {
            ("追兵", "是我拖住了你。"),
            ("红拂女", "回头是我自己选的。")
        };

        internal static readonly (string speaker, string line)[] Farewell =
        {
            ("追兵", "你已经走了，何必回来。"),
            ("红拂女", "你守了一句话，我还一份情。"),
            ("追兵", "这下，我真交不了差了。"),
            ("红拂女", "那就别回去了。江湖路远，交个朋友。"),
            ("追兵", "好。天亮前，我们还能赶一段路。"),
            ("", "风尘未歇。")
        };

        internal static readonly (string speaker, string line)[] FarewellWhileAllyDown =
        {
            ("红拂女", "人走了。还能起来么？"),
            ("追兵", "歇口气。你已经走了，何必回来。"),
            ("红拂女", "你守了一句话，我还一份情。"),
            ("追兵", "这下，我真交不了差了。"),
            ("红拂女", "那就别回去了。这个朋友，我认了。起来，一起走。"),
            ("", "风尘未歇。")
        };

        internal const string Arrival = "灯还亮着，人却不见了。账台上有本摊开的册子。";
        internal const string RescueDiscovery = "追兵被五名来兵围住，仍在独自支撑。\n红拂女 · 你守一句话，我还一份情。";
        internal const string LedgerEntries = "初五\n房钱六百，饭食二百四十，酒九十。\n入九百三十；支米柴四百一十；余五百二十。\n\n"
            + "初六\n房钱八百，饭食三百六十，酒一百二十。\n入一千二百八十；支四百三十；余八百五十。\n\n"
            + "初七\n房钱四百，饭食一百八十，酒六十。\n入六百四十；支菜盐二百一十；余四百三十。\n\n"
            + "初八 · 夜雨\n来客五人，借后院歇脚。\n房钱尚未——";
        internal const string CombatLesson = "看清起手，等对手收势再反击。留些体力，不要一味追着出剑。";
        internal const string AICredit = "AI 辅助制作 · Aholo Lux3D / GPT";
    }
}
