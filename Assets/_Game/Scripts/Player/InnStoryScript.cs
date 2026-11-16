namespace FengChenInn
{
    internal static class InnStoryScript
    {
        internal static readonly (string speaker, string line)[] Sentry =
        {
            ("驻守小兵", "站住。摘下斗笠，让我看清楚。"),
            ("红拂女", "借个檐，等雨小些就走。"),
            ("驻守小兵", "紫衣，独行。等的就是你。把剑放下。"),
            ("红拂女", "想拿我的剑，自己来取。")
        };

        internal static readonly (string speaker, string line)[] BeforeFight =
        {
            ("追兵", "红拂姑娘。你让我们追得很辛苦。"),
            ("红拂女", "前面这个，没拦住我。你也要试？"),
            ("追兵", "府里只叫我带你回去，没叫我伤你。"),
            ("红拂女", "那就让开。"),
            ("追兵", "我在府里当差七年。你替旧人求过情，我记得。"),
            ("红拂女", "记得，就别替他们拿人。"),
            ("追兵", "我放你走，我回去交不了差。"),
            ("红拂女", "你交不了差，与我何干。"),
            ("追兵", "与你无干。今夜你赢了，我让路；你输了，跟我回去。我不捆你。"),
            ("红拂女", "你说话算数？"),
            ("追兵", "算数。"),
            ("红拂女", "好。")
        };

        internal static readonly (string speaker, string line)[] Victory =
        {
            ("追兵", "我输了。门在你身后。"),
            ("红拂女", "你还守着？"),
            ("追兵", "替你守一回。后面的人快到了。"),
            ("红拂女", "你这样，怎么交差？"),
            ("追兵", "输了就是输了。走吧。")
        };

        internal static readonly (string speaker, string line)[] Reinforcements =
        {
            ("后续小兵 · 门外", "人在这里！把门守住！"),
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
            ("红拂女", "那就别回去了。"),
            ("追兵", "先走。天亮前，还能赶一段路。"),
            ("", "风尘未歇。")
        };

        internal static readonly (string speaker, string line)[] FarewellWhileAllyDown =
        {
            ("红拂女", "人走了。还能起来么？"),
            ("追兵", "歇口气。你已经走了，何必回来。"),
            ("红拂女", "你守了一句话，我还一份情。"),
            ("追兵", "这下，我真交不了差了。"),
            ("红拂女", "那就别回去了。天亮前，我们还能赶一段路。"),
            ("", "风尘未歇。")
        };

        internal const string Arrival = "门后有脚印。不是掌柜的。";
        internal const string CombatLesson = "看清起手，等对手收势再反击。留些体力，不要一味追着出剑。";
        internal const string AICredit = "AI 辅助制作 · Aholo Lux3D / GPT";
    }
}
