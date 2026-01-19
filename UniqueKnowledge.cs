using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using ModData;
using Il2CppSystem.Text.RegularExpressions;
using Il2CppSystem.Text;
using Il2CppSystem.IO;

namespace UniqueKnowledge
{
    internal class UniqueKnowledge : MelonMod
    {
		internal static UniqueKnowledge Instance { get; private set; }
		internal UniqueKnowledgeSave Data { get; private set; } = new UniqueKnowledgeSave(nameof(UniqueKnowledge));
		// internal MelonLogger.Instance Logger { get; }

        public override void OnInitializeMelon()
		{
			Instance = this;

			uConsole.RegisterCommand("uknowledge_clear", new Action(() => {
				Data.ClearData();
			}));
		}

    }

    class UniqueKnowledgeSave : ModDataManager
    {
        public UniqueKnowledgeSave(string modName, bool debug = false) : base(modName, debug)
		{
			researched = new Dictionary<string, float>();
		}
		Dictionary<string, float> researched;

		internal void SaveData ()
		{
			StringBuilder sb = new StringBuilder();
			foreach (var kvp in researched)
			{
				sb.AppendFormat("#{0}:{1}!", kvp.Key, kvp.Value);
				// MelonLogger.Msg($"SaveData: {sb.ToString()}");
				Save(sb.ToString());
			}
		}

		internal void LoadData ()
		{
			researched.Clear();
			var record = Load();
			if (string.IsNullOrWhiteSpace(record)) return;
			MelonLogger.Msg(record);

			var validationCursor = 0;
			while ((validationCursor = record.IndexOf('#', validationCursor)) != -1)
			{
				var nextA = record.IndexOf('#', validationCursor + 1);
				var nextB = record.IndexOf(':', validationCursor);
				var nextC = record.IndexOf('!', validationCursor);
				
				if (nextB == -1 || nextC == -1 || nextC < nextB || (nextA != -1 && (nextA < nextB || nextA < nextC)))
				{
					MelonLogger.Error($"Invalid save data, will not load researched hours: {record} (debug info: { nextA }/{ nextB }/{ nextC })");
					return;
				}

				validationCursor += 1;
			}

			var matches = Regex.Matches(record, "#(.+?):(.+?)!");
			if (matches == null || matches.Count == 0) return;
			for (int i = 0; i < matches.Count; i++)
			{
				var m = matches.GetMatch(i);
				var name = m.Groups[1];
				var hourStr = m.Groups[2];
				// MelonLogger.Msg($"LoadData: {m.Value} -> {name.Value} : {hourStr.Value}");
				if (string.IsNullOrWhiteSpace(name.Value) || !float.TryParse(hourStr.Value, out float hours)) continue;
				researched.Add(name.Value, hours);
			}
 		}

		internal void ClearData ()
		{
			researched.Clear();
			Save("");
		}

		internal float GetResearchedHours (string name)
		{

			if (researched.TryGetValue(name, out float hours)) return hours;
			else return 0;
		}

		internal float GetOrUpdateResearchedHours (string name, float defaultHours)
		{
			if (researched.TryGetValue(name, out float hours)) return hours;
			else
			{
				researched[name] = defaultHours;
				return defaultHours;
			}
		}

		internal void UpdateResearchedHours (string name, float hours)
		{
			researched[name] = hours;
		}
    }

    [HarmonyPatch(typeof(ResearchItem), nameof(ResearchItem.Read))]
	internal static class Read
	{
		internal static void Postfix (ResearchItem __instance)
		{
            GearItem gearItem = __instance.GetComponent<GearItem>();
			Il2CppSystem.Collections.Generic.List<GearItem> tmp = new Il2CppSystem.Collections.Generic.List<GearItem>(3);
            GameManager.m_Inventory.GetItems(gearItem.name, tmp);
			foreach (var gi in tmp)
			{
				if (gi == gearItem) continue;
				var r = gi.GetComponent<ResearchItem>();
				if (!r) continue;
				r.m_ElapsedHours = __instance.m_ElapsedHours;
				// MelonLogger.Msg($"Synced: {r.name}: {r.m_ElapsedHours}/{r.m_TimeRequirementHours}");
			}
			UniqueKnowledge.Instance.Data.UpdateResearchedHours(gearItem.name, __instance.m_ElapsedHours);
			// MelonLogger.Msg($"Read: {__instance.name} ({gearItem.name}): {__instance.m_ElapsedHours}/{__instance.m_TimeRequirementHours}");
		}
	}

    [HarmonyPatch(typeof(ResearchItem), nameof(ResearchItem.Deserialize))]
	internal static class Deserialize
	{
		internal static void Postfix (ResearchItem __instance)
		{
            GearItem gearItem = __instance.GetComponent<GearItem>();
			__instance.m_ElapsedHours = UniqueKnowledge.Instance.Data.GetOrUpdateResearchedHours(gearItem.name, __instance.m_ElapsedHours);
		}
	}

	[HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.EnterInspectGearMode), new Type[] { typeof(GearItem), typeof(Container), typeof(IceFishingHole), typeof(Harvestable), typeof(CookingPotItem) } )]
	internal static class EnterInspectGearMode
	{
		internal static bool Prefix (PlayerManager __instance, GearItem gear)
		{
            if (gear.m_ResearchItem == null) return true;
			// MelonLogger.Msg($"EnterInspectGearMode: {gear.name}");
			gear.m_ResearchItem.m_ElapsedHours = UniqueKnowledge.Instance.Data.GetOrUpdateResearchedHours(gear.name, gear.m_ResearchItem.m_ElapsedHours);
			return true;
		}
	}

    [HarmonyPatch(typeof(Panel_Inventory_Examine), nameof(Panel_Inventory_Examine.OnRead))]
	internal static class OnRead
	{
		internal static void Prefix (Panel_Inventory_Examine __instance)
		{
            // MelonLogger.Msg($"OnRead Mark as read: {__instance.name} ({__instance.m_GearItem.name})");
			if (__instance.m_GearItem.m_ResearchItem.NoBenefitAtCurrentSkillLevel())
			{
				// Update all books of the same type in the inventory
				Il2CppSystem.Collections.Generic.List<GearItem> tmp = new Il2CppSystem.Collections.Generic.List<GearItem>(3);
				GameManager.m_Inventory.GetItems(__instance.m_GearItem.name, tmp);
				foreach (var gi in tmp)
				{
					if (gi == __instance.m_GearItem) continue;
					var r = gi.GetComponent<ResearchItem>();
					if (!r) continue;
					r.m_ElapsedHours = __instance.m_GearItem.m_ResearchItem.m_TimeRequirementHours;
					// MelonLogger.Msg($"Synced: {r.name}: {r.m_ElapsedHours}/{r.m_TimeRequirementHours}");
				}
				// Save
				UniqueKnowledge.Instance.Data.UpdateResearchedHours(__instance.m_GearItem.name, __instance.m_GearItem.m_ResearchItem.m_TimeRequirementHours);
				return;
			}
		}
	}

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddGear))]
	internal static class AddGear
	{
		internal static void Postfix (Inventory __instance, GearItem gi)
		{
            if (gi.m_ResearchItem == null) return;
			// MelonLogger.Msg($"+AddGear: {gi.name} ({gi.m_ResearchItem.m_ElapsedHours})");
			gi.m_ResearchItem.m_ElapsedHours = UniqueKnowledge.Instance.Data.GetOrUpdateResearchedHours(gi.name, gi.m_ResearchItem.m_ElapsedHours);
			// MelonLogger.Msg($"-AddGear: {gi.name} ({gi.m_ResearchItem.m_ElapsedHours})");
		}
	}


    [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadSaveGameSlot), new Type[] { typeof(SaveSlotInfo) })]
	internal static class LoadSaveGameSlot
	{
		internal static void Postfix ()
		{
			UniqueKnowledge.Instance.Data.LoadData();
		}
	}

    [HarmonyPatch(typeof(SaveGameSlots), nameof(SaveGameSlots.WriteSlotToDisk), new Type[] { typeof(SlotData), typeof(SaveGameSlots.Timestamp) })]
		internal static class SaveGlobalData
	{
		internal static void Prefix ()
		{
			UniqueKnowledge.Instance.Data.SaveData();
		}
	}

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.HandlePlayerDeath))]
	internal static class HandlePlayerDeath // Workaround for ModData "autosave" issue
	{
		internal static void Postfix (GameManager __instance)
		{
			UniqueKnowledge.Instance.Data.ClearData();
		}
	}
}
