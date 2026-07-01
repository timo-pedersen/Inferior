using System.Text.RegularExpressions;

namespace Inferior.UI;

/// <summary>
/// Ready-made text filter functions for use with TextBox.TextFilter.
/// </summary>
public static class TextFilters
{
    // Ordered longest-first so "motherfucker" beats "fucker" in alternation.
    private static readonly (string Word, string Replacement)[] _entries =
    [
        // Extended family portraits
        ("motherfucker",   "mommy-lover"),
        ("motherfucking",  "mommy-hugging"),
        ("clusterfuck",    "clusterduck"),
        ("fuckwit",        "duckwit"),
        ("fuckhead",       "duckhead"),
        ("fuckface",       "duckface"),
        ("fucking",        "ducking"),
        ("fucked",         "ducked"),
        ("fucker",         "ducker"),
        ("fuck",           "duck"),

        // Sunshine department
        ("bullshitter",    "bull-daisier"),
        ("bullshitting",   "bull-daisying"),
        ("bullshit",       "bull-daisy"),
        ("horseshit",      "horsedaisy"),
        ("shithead",       "sunbeam"),
        ("shitstorm",      "glitter-storm"),
        ("shitface",       "sunnyface"),
        ("shithole",       "sunshine-hole"),
        ("shitshow",       "glittershow"),
        ("dipshit",        "dipsauce"),
        ("shitbag",        "teabag"),
        ("shitty",         "sparkly"),
        ("shit",           "sunshine"),

        // Squirrel empire
        ("assclown",       "clownfish"),
        ("asshole",        "pineapple"),
        ("asswipe",        "napkin"),
        ("asshat",         "acornhat"),
        ("jackass",        "jackrabbit"),
        ("smartass",       "cleverclogs"),
        ("dumbass",        "fluffybrain"),
        ("fatass",         "rotund-wombat"),
        ("badass",         "goodsquirrel"),
        ("ass",            "squirrel"),
        ("arsehole",       "armchairhole"),
        ("arse",           "armchair"),

        // Princess protocol
        ("bitching",       "princesssing"),
        ("bitchy",         "princessy"),
        ("bitch",          "princess"),
        ("bastard",        "rascal"),
        ("bollocks",       "bolts"),

        // Barnyard restructuring
        ("cocksucker",     "lollipop-enthusiast"),
        ("cock",           "rooster"),
        ("cunt",           "cupcake"),

        // Condiment corner
        ("dickhead",       "picklehat"),
        ("dick",           "pickle"),
        ("douchebag",      "perfume-bag"),
        ("douche",         "perfume"),
        ("dildo",          "dilly"),

        // Dang-zone
        ("goddammit",      "golldangit"),
        ("goddamn",        "golldang"),
        ("dammit",         "dang it"),
        ("damned",         "danged"),
        ("damn",           "dang"),

        // Fizz-ics
        ("pisshead",       "fizzpop"),
        ("pissing",        "fizzing"),
        ("pissed",         "fizzled"),
        ("pissoff",        "fizz off"),
        ("piss",           "fizz"),
        ("prick",          "pickle"),
        ("pussy",          "kitten"),

        // Aquatic logistics
        ("whorehouse",     "wholesale depot"),
        ("whore",          "wholesaler"),
        ("wanker",         "wanderer"),
        ("wank",           "walk"),

        // Avian redistribution
        ("twat",           "tweet"),
        ("tits",           "bits"),
        ("tosser",         "toaster"),
        ("turd",           "turtle"),
        ("turds",          "turtles"),

        // Structural upgrades
        ("scumbag",        "snugglebag"),
        ("skank",          "skunk"),
        ("slut",           "slug"),
        ("ballsack",       "bellsack"),
        ("balls",          "bells"),
        ("boobs",          "booths"),
        ("butthead",       "butterhead"),

        // Geographic and theological corrections
        ("crap",           "crafts"),
        ("crappy",         "crafty"),
        ("hell",           "heck"),

        // British charm school
        ("bugger",         "buffer"),
        ("sodding",        "sorting"),
        ("bloody",         "bloomy"),
        ("blimey",         "bliney"),

        // Human-resources approved terms
        ("retard",         "rockstar"),
        ("nigger",         "neighbor"),
        ("nigga",          "neighbor"),
        ("jackoff",        "jog off"),
        ("jerkoff",        "jerkwater"),
        ("fuckoff",        "duck off"),
    ];

    private static readonly Dictionary<string, string> _map =
        _entries.ToDictionary(e => e.Word, e => e.Replacement, StringComparer.OrdinalIgnoreCase);

    private static readonly Regex _pattern = new(
        @"\b(" + string.Join("|", _entries.Select(e => Regex.Escape(e.Word))) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Replaces the ~100 most common swear words with lightly baffling alternatives.
    /// Case is mirrored: ALL CAPS stays ALL CAPS, Title Case gets Title Case.
    /// </summary>
    public static string NastyWordFilter(string text) =>
        _pattern.Replace(text, m => MirrorCase(m.Value, _map[m.Value]));

    private static string MirrorCase(string original, string replacement)
    {
        if (original.All(char.IsUpper)) return replacement.ToUpperInvariant();
        if (original.Length > 0 && char.IsUpper(original[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return replacement;
    }
}
