using System.Text.RegularExpressions;
using System.Xml.Linq;
using DaocUiForge.Core.Model;
using DaocUiForge.Core.Reference;

namespace DaocUiForge.Core.Render;

/// <summary>
/// Sample content for fields that only the engine fills in the game. Ported
/// from <c>adapterOf</c> and <c>dummyFor</c> of the HTML original.
///
/// Many elements fetch their content through &lt;Adapter&gt; and sit empty in
/// the XML. Without sample values neither text width nor layout can be
/// judged. The XML is NEVER modified in the process.
///
/// Order: first the real values from the DAoCEd data set
/// (<see cref="ReferenceData"/>), then rules from the specific to the
/// general, and finally the adapter name itself in readable form.
///
/// The values are English because the game is: a UI editor has to show the
/// string widths that will actually occur.
/// </summary>
public static class SampleData
{
    private static readonly string[] Names =
        { "Toka", "Brannoc", "Sirona", "Alrik", "Maeve", "Gorm", "Nyra", "Cadoc" };

    private static readonly string[] Classes =
        { "Berserker", "Healer", "Shaman", "Runemaster", "Shadowblade", "Hunter", "Thane", "Skald" };

    private static readonly string[] Specs =
        { "Axe", "Shield", "Parry", "Left Axe", "Berserk", "Hammer", "Sword", "Spear" };

    /// <summary>
    /// Adapter name of an element. DAoC knows several spellings — all of them
    /// have to be taken into account or fields stay empty.
    /// </summary>
    public static string AdapterOf(XElement? def)
    {
        foreach (var tag in new[] { "Adapter", "AdapterName", "Adaptername", "TextAdapterName", "LabelAdapterName" })
        {
            string v = Xml.Tx(def, tag);
            if (v.Length > 0) return v;
        }
        return "";
    }

    /// <summary>
    /// Sample text for an adapter. Null when no adapter is set or when it is
    /// explicitly called "none".
    /// </summary>
    /// <param name="maxChars">&lt;MaxCharacters&gt;; 0 means unlimited.</param>
    public static string? For(string? adapter, int maxChars = 0, ReferenceData? data = null)
    {
        if (string.IsNullOrEmpty(adapter) || adapter == "none") return null;
        data ??= ReferenceData.Default;

        string a = adapter.ToLowerInvariant();

        // Trailing digits pick the row: group_health3 -> 3
        int end = a.Length, start = end;
        while (start > 0 && char.IsAsciiDigit(a[start - 1])) start--;
        int idx = start < end && int.TryParse(a.AsSpan(start, end - start), out var n) ? n : 0;
        string baseName = a[..start];

        // 1) Real values from the original editor (exact spelling first)
        foreach (var table in new[] { data.Texts, data.Current })
            if (table.TryGetValue(adapter, out var exact)) return Clip(exact, maxChars);
        foreach (var table in new[] { data.Texts, data.Current })
            foreach (var kv in table)
                if (string.Equals(kv.Key, a, StringComparison.OrdinalIgnoreCase))
                    return Clip(kv.Value, maxChars);

        // 2) Rules, specific before general
        foreach (var (re, value) in Rules)
            if (re.IsMatch(baseName) || re.IsMatch(a))
                return Clip(value(idx), maxChars);

        // 3) Last resort: turn the adapter name into something readable, so
        //    that unfamiliar UI packages stay judgeable too.
        string words = baseName.Replace('_', ' ').Trim();
        return Clip(words.Length > 0 ? char.ToUpperInvariant(words[0]) + words[1..] : "—", maxChars);
    }

    private static string Clip(string s, int maxChars) =>
        maxChars > 0 && s.Length > maxChars ? s[..maxChars] : s;

    private static string Pick(string[] arr, int idx) => arr[idx % arr.Length];

    private static string Pick(int[] arr, int idx) => arr[idx % arr.Length].ToString();

    private static readonly (Regex Re, Func<int, string> Value)[] Rules = Build();

    private static (Regex, Func<int, string>)[] Build()
    {
        (string, Func<int, string>)[] raw =
        {
            (@"^group_name$|^name$",             i => Pick(Names, i)),
            (@"^group_class$|class",             i => Pick(Classes, i)),
            (@"^group_health$|health|_hits$",    i => Pick(new[] { 100, 87, 64, 93, 45, 100, 72, 58 }, i)),
            (@"^group_power$|power|mana",        i => Pick(new[] { 100, 72, 95, 40, 88, 61, 100, 33 }, i)),
            (@"^group_endurance$|endurance|_end$", i => Pick(new[] { 100, 95, 80, 67, 100, 54, 88, 76 }, i)),
            (@"^group_level$|^level$|_level$",   i => Pick(new[] { 50, 50, 49, 50, 48, 50, 47, 50 }, i)),
            (@"concentration",                   _ => "12"),
            (@"^spec_name$",                     i => Pick(Specs, i)),
            (@"^spec_level$",                    i => Pick(new[] { 50, 42, 39, 35, 28, 21, 15, 10 }, i)),

            (@"realm_rank|^rank$",               _ => "RR 7L4"),
            (@"realm_level",                     _ => "74"),
            (@"realm_name",                      i => Pick(new[] { "Albion", "Midgard", "Hibernia" }, i)),
            (@"realm_points|^rp$",               _ => "4,512,880"),
            (@"realm_cost",                      _ => "3,200"),
            (@"bounty_points|^bp$",              _ => "18,420"),
            (@"merit_points",                    _ => "250,000"),
            (@"champ|^cl_",                      _ => "CL 10"),
            (@"master_rank",                     _ => "ML 10"),
            (@"master_title|master_window_title", _ => "Master Levels"),

            (@"guild_name",                      _ => "The Wardens"),
            (@"alliance_name",                   _ => "Northwind Alliance"),
            (@"guild_level",                     _ => "15"),
            (@"guild_dues",                      _ => "5%"),
            (@"guild_money|guild_bp|guild_bounty", _ => "42,100"),
            (@"member_count",                    _ => "128"),
            (@"current_page",                    _ => "1"),
            (@"total_pages",                     _ => "7"),
            (@"house_lot",                       _ => "Lot 421"),
            (@"banner_status",                   _ => "Active"),

            (@"platinum",                        _ => "3"),
            (@"^.*gold$",                        _ => "184"),
            (@"silver",                          _ => "52"),
            (@"copper",                          _ => "77"),
            (@"mithril",                         _ => "0"),
            (@"quantity",                        _ => "1"),
            (@"merchant_title",                  _ => "Merchant"),
            (@"page_display|_page$",             _ => "Page 1 / 4"),

            (@"stats_strength",                  _ => "312"),
            (@"stats_constitution",              _ => "287"),
            (@"stats_dexterity",                 _ => "204"),
            (@"stats_quickness",                 _ => "196"),
            (@"stats_intelligence",              _ => "88"),
            (@"stats_empathy",                   _ => "75"),
            (@"stats_piety",                     _ => "80"),
            (@"stats_charisma",                  _ => "72"),
            (@"stats_hitpoints",                 _ => "3,184"),
            (@"stats_weapon_skill",              _ => "1,402"),
            (@"stats_armor_factor",              _ => "1,128"),
            (@"stats_level",                     _ => "50"),
            (@"^stats_",                         _ => "128"),

            (@"keep_status_title|keep_upgrade_title", _ => "Caer Benowyc"),
            (@"keep_status_type",                _ => "Keep"),
            (@"keep_status_claimed",             _ => "The Wardens"),
            (@"keep_status_target|keep_upgrade_target", _ => "Benowyc"),
            (@"keep_upgrade_level|keep_status_level", _ => "Level 8"),
            (@"siege_title",                     _ => "Battering Ram"),
            (@"siege_timer",                     _ => "0:24"),

            (@"game_time",                       _ => "21:47"),
            (@"alpha_value",                     _ => "80"),
            (@"lfg_min_level",                   _ => "45"),
            (@"lfg_max_level",                   _ => "50"),
            (@"lfg_title",                       _ => "Looking for Group"),
            (@"mount_name",                      _ => "Horse"),
            (@"mount_encumbrance",               _ => "40 / 120"),
            (@"saddlebag",                       _ => "Empty"),
            (@"mini_pet_title",                  _ => "Companion"),
            (@"mini_pet_life",                   _ => "100"),
            (@"quiver_count",                    _ => "40"),
            (@"mini_craft_title",                _ => "Crafting"),
            (@"mini_info_string",                _ => "Ready"),
            (@"interact_title|info_title",       _ => "Information"),
            (@"hookpoint_store_title",           _ => "Hookpoint"),
            (@"quest_title",                     _ => "The Lost Messenger"),
            (@"opt_reward_count",                _ => "2"),
            (@"summary_title",                   _ => "Summary"),
            (@"summary_target",                  _ => "Target"),
            (@"summary_label|summary_amount|summary_skill_amount", _ => "100"),
            (@"^summary_",                       _ => "50"),
            (@"train_.*points",                  _ => "14"),
            (@"low_pop_rp_bonus",                _ => "+15%"),
            (@"realmwar_info",                   _ => "4 / 7 relic keeps"),
            (@"realm_(df|keep|power|exp)_text",  _ => "Controlled"),
            (@"map_info",                        _ => "Emain Macha"),
            (@"chat_entry",                      _ => "Welcome to Camelot!"),
            (@"_title$",                         _ => "Title"),
            (@"window_alpha|font_alpha",         _ => "80"),
            (@"time_of_day",                     _ => "Evening"),
            (@"realm_exp_fine",                  _ => "82%"),
            (@"realm_strength_text",             _ => "Strong"),
            (@"release_timer_text|timer_text|timer_time", _ => "0:08"),
            (@"training_.*avail",                _ => "14"),
            (@"training_.*needed",               _ => "6"),
            (@"training_.*remaining",            _ => "8"),
            (@"value_chooser_qty",               _ => "1"),
            (@"_name$",                          i => Pick(Names, i)),
            (@"count|amount|_num$",              _ => "12"),

            // --- general fallback rules for unfamiliar UI packages ---
            (@"percent|pct|_pc$|ratio",          _ => "75"),
            (@"xp|experience",                   _ => "62"),
            (@"mana|energy|stamina|fatigue",     _ => "85"),
            (@"gold|money|coin|cash|currency",   _ => "184"),
            (@"time|clock|duration|timer",       _ => "0:42"),
            (@"date|day",                        _ => "Jun 12"),
            (@"zone|region|area|location|map",   _ => "Emain Macha"),
            (@"target|enemy|mob",                _ => "Target"),
            (@"text|string|message|desc",        _ => "Sample text"),
            (@"^is_|^has_|enabled|active|state", _ => "Yes"),
            (@"current|value|val$|total|max|min", _ => "100"),
            (@"id$|index|slot|page|rank|tier",   _ => "1"),
        };

        return raw.Select(r => (new Regex(r.Item1, RegexOptions.Compiled | RegexOptions.CultureInvariant), r.Item2))
                  .ToArray();
    }
}
