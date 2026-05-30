using KnowledgeSystem.Ai;

var client = new LlamaCppLogprobFilteringService("http://127.0.0.1:8080", "none");

var tests = new List<(string Query, string Document, bool Expected)>
{
    ("Which PDC is best for upward elevation?", "PDC max upward elevation vs continuous fire time: OPA Hashari & OPA Shotgun best (90°, 21.43s), Voltaire worst composite", true),
    ("Do Johnson Fixed Heavy Railguns overheat differently?", "Johnson Fixed Heavy Railgun vs other fixed railgun: heat params do not differentiate them in practice", true),
    ("List weapons with zero heat per shot", "Complete list of weapons with zero ROF degradation, zero heat/shot, and zero delay-ceasefire (truly instant)", true),
    ("What is the Zeus launcher's shots-per-MW ratio?", "Launcher shots-per-MW idle ratio: Zeus 0.17, Apollo 0.50, Artemis 3.00, Tycho 20.00 from SDX WeaponStats", true),
    ("How do Expanse Nariman PDC specs compare to SDX?", "Nariman PDC full comparison: Expanse lore (40mm, 5000 m/s, 5-20km) vs SDX (3000m max, 1800 RPM, no muzzle velocity stated)", true),
    ("What ship and gear do poor players need to hunt TTU?", "SDX TTU hunting ship requirements, equipment, and combat tactics for poor new players", true),
    ("Where do TTU spawn in The Belt on SDX?", "SDX TTU spawn mechanics, locations, and loot drops in The Belt", true),
    ("Is the Rockhopper an OPA ship on SDX?", "Rockhopper ship: OPA on SDX server vs independently-owned prospector in Expanse canon", true),
    ("What are the specs of the Amun-Ra stealth frigate?", "Amun-Ra-class stealth frigate full specifications, stealth design, and ship inventory (The Expanse)", true),
    ("How does the SDX Adro boss raid work?", "SDX Adro boss raid: endgame PvE instance, spawn rules, faction-specific NPC grids, and loot", true),
    ("What is the Adro Diamond in Expanse lore?", "Adro Diamond The Expanse lore: Ring Builder Library, 5 billion years old, Jupiter-sized data storage", true),
    ("Who is Winston Duarte?", "Winston Duarte: Laconian dictator, protomolecule hive mind plan, Tecoma gate catastrophe", true),
    ("Does SDX Rule #1 prevent Discord insiding?", "SDX Rule #1 prohibits alt accounts and impersonation, preventing Discord insiding regardless of Nation server status", true),
    ("Who is Darkstar in the repository?", "Darkstar references across repository: SDX player/shipwright, Shields Mod author, Stargate squadron", true),
    ("How did the Rocinante vs Osiris battle go at Thoth Station?", "Thoth Station encounter: Rocinante vs Osiris (Amun-Ra-class) battle details and aftermath", true),
    ("What is the FNVY NPC faction on SDX?", "FNVY (Free Navy) NPC faction on SDX server: Saturn location, difficulty, drops, weapons, lore", true),
    ("What is the TIH NPC faction on SDX?", "TIH hostile NPC faction on SDX server: Jupiter area, Belter-tier, patrols, warnings, and bounties", true),
    ("Does SDX use WeaponCore Advanced Fire Distribution?", "SDX disables WeaponCore Advanced Fire Distribution and AdvSync, uses basic distance targeting and custom networking", true),
    ("What is the difference between SDX Adro and Expanse Adro?", "SDX Adro vs The Expanse Adro cross-reference: setting, purpose, function, loot, key divergence", true),
    ("How do I go from zero to hero on SDX?", "SDX zero to hero new player roadmap: daily freebies, mining, building, NPCs, factions, survival tips", true),
    ("How do I get a Rockhopper starter ship on SDX?", "SDX Rockhopper free starter ship acquisition: PvP spawns in Lobby/Sol, rename, !fixrespawn, 72h cooldown", true),
    ("Where can I find SDX PVP learning resources?", "SDX PVP learning resources on YouTube: LittleMajesty, AlphaMatte Rockhopper Redux, BTR Kantax", true),
    ("What power armor types exist in The Expanse?", "All known Expanse power armor types: Goliath, Reaver, Laconian, Stalker, Combat Mechs", true),
    ("How does SDX FNVY compare to canon Expanse Free Navy?", "SDX FNVY vs Expanse canon Free Navy: full comparison across origin, territory, fleet, leadership, purpose, armament", true),
    ("What SDX rules loopholes come from undefined terms?", "SDX rules loopholes: undefined terms and honor-system enforcement gaps", true),
    ("What ship and block mechanic gaps exist in SDX rules?", "SDX rules loopholes: ship and block mechanics gaps in restrictions", true),
    ("What exploitable mechanics exist on SDX?", "SDX rules loopholes: exploitable mechanics — auto-cleanup, KOTH, PvE zone, nation cooldown", true),
    ("What are the SDX server block limits?", "SDX server block limits: 100 per type (gyros, cameras), 6 coils vs 3 railguns tradeoff", true),
    ("What is Mahoraga in Jujutsu Kaisen?", "JJK Eight-Handled Sword Divergent Sila Divine General Mahoraga: shikigami, adaptation, summoning, history", true),
    ("How do Sukuna and Kenjaku recover techniques after domain?", "Sukuna and Kenjaku barrierless divine domain expansion and unique cursed technique recovery methods", true),
    ("Why can't Megumi complete a lethal domain?", "Megumi Fushiguro cannot complete lethal domain: two reasons (barrier proficiency, no sure-hit) and Chimera Shadow Garden vs barrier-enclosed domain comparison", true),
    ("Who uses non-lethal domains in modern JJK?", "Non-lethal domains historical shift, modern users (Higuruma, Hakari), and speed-vs-lethality trade-off", true),
    ("How does Sukuna's Malevolent Shrine reach 200m?", "Sukuna's Malevolent Shrine: Dismantle/Cleave targeting distinction and two structural features enabling 200m range", true),
    ("What happens in a three-way domain clash in JJK?", "JJK three-way domain clash: barriers shatter before forming; escape strategies: self-domain expansion or anti-domain techniques", true),
    ("How does Gojo's Limitless technique work?", "Limitless cursed technique: Infinity, Blue, Red, Hollow Purple, Unlimited Void, teleportation, and counters", true),
    ("How does Limitless compare to Sukuna's Shrine?", "Limitless vs Shrine (Sukuna): complete comparison of mechanics, techniques, domains, and clash results", true),
    ("How did Utahime help Gojo's 200% Hollow Purple?", "Utahime Iori's Solo Forbidden Area enabled Gojo's 200% Hollow Purple against Sukuna", true),
    ("What brain damage did Gojo suffer from domain resets?", "Gojo's lasting brain damage from resetting technique burnout five times during Sukuna fight", true),
    ("Is Sukuna a fraud for using Mahoraga?", "Why Sukuna is not a fraud for using Mahoraga — tamed it, chose Ten Shadows deliberately as strategy", true),
    ("Was Gojo vs Sukuna an unfair 3v1 fight?", "Gojo vs Sukuna fight was unfair 3v1 — Gojo fought Sukuna, Mahoraga, and Agito simultaneously", true),
    ("What is Gojo's full Hollow Purple incantation?", "Gojo's full combined incantation sequence for maximum-output Hollow Purple: Phase, Paramita, Pillar of Light, etc.", true),
    ("How did Gojo unlock reverse cursed technique?", "Gojo Satoru awakened reverse cursed technique and Red on verge of death against Toji Fushiguro", true),
    ("Can Yuta use Idle Transfiguration with Rika?", "Yuta with Idle Transfiguration + Rika infinite CE: needs barrier for mass transfiguration; Construction can't create special cursed tools", true),
    ("Was Sukuna born a human or a curse?", "Ryomen Sukuna was born a human Heian Era sorcerer and died a curse in JJK Chapter 268", true),
    ("How did Kenjaku die in JJK?", "Kenjaku death details: ambush after gauntlet by Yuta, Takaba, Todo; still executed backup plan; Yuta gained CSM", true),
    ("What are the most powerful Stargate technologies ranked?", "Top 6 most powerful Stargate technologies ranked: ZPM, Asgard beams, Drones, Shields, Replicators, Stargate network", true),
    ("How powerful are the Replicators in Stargate?", "Replicators power ranking: Milky Way nanites vs Asurans, capabilities and weaknesses", true),
    ("Are Alterans and Lanteans different species?", "Alterans vs Lanteans are different species: evidence from Ascension Machine in Tao of Rodney", true),
    ("What is the origin of the Goa'uld language?", "Goa'uld language origin is unexplained: distinct alphabet without Ancient derivation", true),
    ("What caused the Asgard cloning degradation?", "Asgard cloning degradation: final cure attempt created fatal disease, not hubris alone", true),
    ("Is there a new Stargate series coming out?", "New Stargate series announced Nov 19, 2025 for Prime Video, showrunner Martin Gero, not a reboot", true),
    ("Who portrayed Adria in Stargate SG-1?", "Adria (Stargate SG-1) portrayed by four actresses at different ages", true),
    ("What is the Nox race in Stargate?", "Nox: Stargate super advanced race living in the woods on Gaia with floating cities", true),
    ("What happened to Rodney McKay trying to ascend?", "Rodney McKay's Ascension abilities in Tao of Rodney that nearly killed him", true),
    ("How does WeaponCore Fire Distribution work?", "WeaponCore Fire Distribution System: FireDistributionManager, three strategies, ThreatMatrix, MinLockTime, accessor loop", true),
    ("Who created WeaponCore?", "BDCarrillo (USAF Engineering Assistant retired) created WeaponCore, maintains it with Nerd and Alioth Merak", true),
    ("What is the architecture of WeaponCore?", "WeaponCore architecture overview: Session singleton, CoreComponent hierarchy, AI targeting, Projectile Engine, Networking, Public API", true),
    ("What does MQR stand for?", "MQR is an umbrella term for MCR intelligence server, plugin, retrieval system, and secret projects", true),
    ("What does MCR mean on SDX?", "MCR = Martian Congressional Republic, ISD = Icaria Science Directorate in SDX universe", true),
    ("Who are the key combat members of the LAC faction?", "LAC (Laconia) faction key combat members: Gergo and Mikolaj from MCR", true),
    ("Which PDC has the best sustained fire rate?", "Amun-Ra-class stealth frigate full specifications, stealth design, and ship inventory (The Expanse)", false),
    ("How do I hunt TTU on SDX?", "All known Expanse power armor types: Goliath, Reaver, Laconian, Stalker, Combat Mechs", false),
    ("What ship did the Rocinante fight at Thoth?", "SDX Adro vs The Expanse Adro cross-reference: setting, purpose, function, loot, key divergence", false),
    ("What is the Adro Diamond?", "SDX Adro boss raid: endgame PvE instance, spawn rules, faction-specific NPC grids, and loot", false),
    ("How do I build a ship on SDX?", "SDX zero to hero new player roadmap: daily freebies, mining, building, NPCs, factions, survival tips", false),
    ("Where is the FNVY faction located?", "Replicators power ranking: Milky Way nanites vs Asurans, capabilities and weaknesses", false),
    ("What is the MQR plugin?", "SDX rules loopholes: exploitable mechanics — auto-cleanup, KOTH, PvE zone, nation cooldown", false),
    ("Who is Winston Duarte?", "JJK Eight-Handled Sword Divergent Sila Divine General Mahoraga: shikigami, adaptation, summoning, history", false),
    ("What is a ZPM in Stargate?", "SDX zero to hero new player roadmap: daily freebies, mining, building, NPCs, factions, survival tips", false),
    ("How does Limitless cursed technique work?", "WeaponCore architecture overview: Session singleton, CoreComponent hierarchy, AI targeting, Projectile Engine, Networking, Public API", false),
    ("What is the Nox race in Stargate?", "Complete list of weapons with zero ROF degradation, zero heat/shot, and zero delay-ceasefire (truly instant)", false),
    ("What caused the Asgard cloning crisis?", "SDX TTU spawn mechanics, locations, and loot drops in The Belt", false),
    ("How did Kenjaku die in JJK?", "Rockhopper ship: OPA on SDX server vs independently-owned prospector in Expanse canon", false),
    ("What is the FireDistributionManager?", "Gojo Satoru awakened reverse cursed technique and Red on verge of death against Toji Fushiguro", false),
    ("Tell me about the Replicators in detail", "Johnson Fixed Heavy Railgun vs other fixed railgun: heat params do not differentiate them in practice", false),
    ("Who is Megumi Fushiguro?", "Darkstar references across repository: SDX player/shipwright, Shields Mod author, Stargate squadron", false),
    ("What happened at Thoth Station?", "Non-lethal domains historical shift, modern users (Higuruma, Hakari), and speed-vs-lethality trade-off", false),
    ("What is the Rocinante?", "Goa'uld language origin is unexplained: distinct alphabet without Ancient derivation", false),
    ("What is Hollow Purple?", "Launcher shots-per-MW idle ratio: Zeus 0.17, Apollo 0.50, Artemis 3.00, Tycho 20.00 from SDX WeaponStats", false),
    ("How do I claim a Rockhopper?", "Nariman PDC full comparison: Expanse lore (40mm, 5000 m/s, 5-20km) vs SDX (3000m max, 1800 RPM, no muzzle velocity stated)", false),
    ("How does Gojo's Infinity work?", "SDX PVP learning resources on YouTube: LittleMajesty, AlphaMatte Rockhopper Redux, BTR Kantax", false),
    ("What are SDX server block limits?", "MQR is an umbrella term for MCR intelligence server, plugin, retrieval system, and secret projects", false),
    ("What is the TIH faction?", "SDX FNVY vs Expanse canon Free Navy: full comparison across origin, territory, fleet, leadership, purpose, armament", false),
    ("Are Alterans and Lanteans the same?", "SDX rules loopholes: undefined terms and honor-system enforcement gaps", false),
    ("Who is BDCarrillo?", "SDX rules loopholes: ship and block mechanics gaps in restrictions", false),
    ("What is a domain expansion in JJK?", "MCR = Martian Congressional Republic, ISD = Icaria Science Directorate in SDX universe", false),
    ("What does SDX Rule #1 say?", "Limitless vs Shrine (Sukuna): complete comparison of mechanics, techniques, domains, and clash results", false),
    ("What is the Adro raid boss loot table?", "Adro Diamond The Expanse lore: Ring Builder Library, 5 billion years old, Jupiter-sized data storage", false),
    ("What is Gojo's Red technique?", "Gojo's full combined incantation sequence for maximum-output Hollow Purple: Phase, Paramita, Pillar of Light, etc.", false),
    ("What is Laconian power armor?", "All known Expanse power armor types: Goliath, Reaver, Laconian, Stalker, Combat Mechs", true),
    ("What is Chimera Shadow Garden?", "Megumi Fushiguro cannot complete lethal domain: two reasons (barrier proficiency, no sure-hit) and Chimera Shadow Garden vs barrier-enclosed domain comparison", false),
    ("What is Sukuna's Malevolent Shrine?", "Sukuna and Kenjaku barrierless divine domain expansion and unique cursed technique recovery methods", false),
    ("What is a PDC in the Expanse?", "Ryomen Sukuna was born a human Heian Era sorcerer and died a curse in JJK Chapter 268", false),
    ("What is Yuta's cursed technique?", "Thoth Station encounter: Rocinante vs Osiris (Amun-Ra-class) battle details and aftermath", false),
    ("Who played Adria in Stargate?", "LAC (Laconia) faction key combat members: Gergo and Mikolaj from MCR", false),
    ("What is the FNVY NPC faction?", "Yuta with Idle Transfiguration + Rika infinite CE: needs barrier for mass transfiguration; Construction can't create special cursed tools", false),
    ("What are coil and railgun limits?", "Utahime Iori's Solo Forbidden Area enabled Gojo's 200% Hollow Purple against Sukuna", false),
    ("Tell me about the SDX Adro boss", "Alterans vs Lanteans are different species: evidence from Ascension Machine in Tao of Rodney", false),
    ("Who is Gergo from LAC?", "New Stargate series announced Nov 19, 2025 for Prime Video, showrunner Martin Gero, not a reboot", false),
    ("What is the Asgard cloning degradation?", "Gojo vs Sukuna fight was unfair 3v1 — Gojo fought Sukuna, Mahoraga, and Agito simultaneously", false),
    ("What are Dismantle and Cleave?", "SDX TTU hunting ship requirements, equipment, and combat tactics for poor new players", false),
    ("What is the Shields Mod?", "Replicators power ranking: Milky Way nanites vs Asurans, capabilities and weaknesses", false),
    ("What happened at Tecoma?", "Limitless vs Shrine (Sukuna): complete comparison of mechanics, techniques, domains, and clash results", false),
    ("What is the Icaria Science Directorate?", "Nox: Stargate super advanced race living in the woods on Gaia with floating cities", false),
};

var hits = 0;
for (var index = 0; index < tests.Count; index++)
{
    var (query, document, expected) = tests[index];
    var result = await client.IsRelevantAsync(query, document, 0.5, CancellationToken.None);

    if (result == expected)
    {
        Console.ForegroundColor = ConsoleColor.Green;

        ++hits;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
    }

    Console.WriteLine($"Test {index + 1} {(expected ? "ACCEPT" : "DROP")}: \"{query}\" | {document}");
    
    Console.ResetColor();
}

Console.WriteLine($"\nTest results: {hits / (double)tests.Count:P2} success");