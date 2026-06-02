using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LockerStackVisualFix;

[HarmonyPatch]
internal static class VisualLockerDistributionPatch
{
    private const int VisualCapacitySlots = 48;

    private static MethodBase TargetMethod()
    {
        Type controllerType = AccessTools.TypeByName("VisibleLockerInterior.Controller");
        if (controllerType == null)
        {
            return null;
        }

        return AccessTools.Method(controllerType, "GetSortedItems", new[] { typeof(GameObject) });
    }

    private static void Postfix(GameObject storageRoot, ref List<GameObject> __result)
    {
        if (storageRoot == null || __result == null || __result.Count == 0)
        {
            return;
        }

        StorageContainer storageContainer = ResolveStorageContainer(storageRoot);
        ItemsContainer container = storageContainer?.container;
        if (container == null)
        {
            return;
        }

        int containerSlots = container.sizeX * container.sizeY;
        if (containerSlots != VisualCapacitySlots)
        {
            return;
        }

        Dictionary<TechType, ItemVisualEntry> visualRepresentatives = GetVisualRepresentatives(__result);
        if (visualRepresentatives.Count == 0)
        {
            return;
        }

        Dictionary<TechType, int> countsByTechType = GetActualCountsByTechType(container, visualRepresentatives);
        if (countsByTechType.Count == 0)
        {
            return;
        }

        int totalUnits = 0;
        foreach (KeyValuePair<TechType, int> pair in countsByTechType)
        {
            totalUnits += pair.Value;
        }

        if (totalUnits <= 0)
        {
            return;
        }

        int targetVisualSlots = totalUnits < VisualCapacitySlots ? totalUnits : VisualCapacitySlots;
        List<AllocationEntry> allocations = AllocateSlots(countsByTechType, visualRepresentatives, targetVisualSlots, totalUnits);
        if (allocations.Count == 0)
        {
            return;
        }

        allocations.Sort((left, right) => left.Order.CompareTo(right.Order));

        List<GameObject> rebuilt = new(targetVisualSlots);
        foreach (AllocationEntry allocation in allocations)
        {
            for (int i = 0; i < allocation.Slots; i++)
            {
                rebuilt.Add(allocation.Representative);
            }
        }

        if (rebuilt.Count > 0)
        {
            __result = rebuilt;
        }
    }

    private static StorageContainer ResolveStorageContainer(GameObject storageRoot)
    {
        StorageContainer direct = storageRoot.GetComponent<StorageContainer>();
        if (direct != null)
        {
            return direct;
        }

        StorageContainer parent = storageRoot.GetComponentInParent<StorageContainer>();
        if (parent != null)
        {
            return parent;
        }

        return storageRoot.GetComponentInChildren<StorageContainer>();
    }

    private static Dictionary<TechType, ItemVisualEntry> GetVisualRepresentatives(List<GameObject> sortedItems)
    {
        Dictionary<TechType, ItemVisualEntry> map = new();

        for (int index = 0; index < sortedItems.Count; index++)
        {
            GameObject candidate = sortedItems[index];
            if (candidate == null)
            {
                continue;
            }

            if (!TryGetTechType(candidate, out TechType techType))
            {
                continue;
            }

            if (techType == TechType.None || map.ContainsKey(techType))
            {
                continue;
            }

            map.Add(techType, new ItemVisualEntry(candidate, index));
        }

        return map;
    }

    private static Dictionary<TechType, int> GetActualCountsByTechType(ItemsContainer container, Dictionary<TechType, ItemVisualEntry> visualRepresentatives)
    {
        Dictionary<TechType, int> countsByTechType = new();

        foreach (InventoryItem inventoryItem in container)
        {
            Pickupable pickupable = inventoryItem?.item;
            if (pickupable == null)
            {
                continue;
            }

            TechType techType = pickupable.GetTechType();
            if (techType == TechType.None || !visualRepresentatives.ContainsKey(techType))
            {
                continue;
            }

            int units = StackCountResolver.GetStackUnits(pickupable);
            if (units <= 0)
            {
                units = 1;
            }

            if (countsByTechType.TryGetValue(techType, out int current))
            {
                countsByTechType[techType] = current + units;
            }
            else
            {
                countsByTechType.Add(techType, units);
            }
        }

        StackCountResolver.TryApplyContainerAdjustments(container, countsByTechType);
        return countsByTechType;
    }

    private static List<AllocationEntry> AllocateSlots(Dictionary<TechType, int> countsByTechType, Dictionary<TechType, ItemVisualEntry> visualRepresentatives, int targetVisualSlots, int totalUnits)
    {
        List<AllocationEntry> entries = new(countsByTechType.Count);

        int assignedBaseSlots = 0;
        foreach (KeyValuePair<TechType, int> pair in countsByTechType)
        {
            int count = pair.Value;
            if (count <= 0 || !visualRepresentatives.TryGetValue(pair.Key, out ItemVisualEntry visualEntry))
            {
                continue;
            }

            double ideal = (double)targetVisualSlots * count / totalUnits;
            int baseSlots = (int)Math.Floor(ideal);
            double remainder = ideal - baseSlots;

            assignedBaseSlots += baseSlots;
            entries.Add(new AllocationEntry(pair.Key, count, baseSlots, remainder, visualEntry.Representative, visualEntry.Order));
        }

        int leftovers = targetVisualSlots - assignedBaseSlots;
        if (leftovers > 0)
        {
            entries.Sort((left, right) =>
            {
                int remainderCmp = right.Remainder.CompareTo(left.Remainder);
                if (remainderCmp != 0)
                {
                    return remainderCmp;
                }

                int countCmp = right.Count.CompareTo(left.Count);
                if (countCmp != 0)
                {
                    return countCmp;
                }

                return left.Order.CompareTo(right.Order);
            });

            for (int i = 0; i < leftovers && i < entries.Count; i++)
            {
                entries[i].Slots++;
            }
        }

        return entries;
    }

    private static bool TryGetTechType(GameObject itemObject, out TechType techType)
    {
        techType = TechType.None;

        Pickupable pickupable = itemObject.GetComponent<Pickupable>();
        if (pickupable != null)
        {
            techType = pickupable.GetTechType();
            return techType != TechType.None;
        }

        Pickupable pickupableInChildren = itemObject.GetComponentInChildren<Pickupable>();
        if (pickupableInChildren != null)
        {
            techType = pickupableInChildren.GetTechType();
            return techType != TechType.None;
        }

        return false;
    }

    private sealed class ItemVisualEntry
    {
        internal ItemVisualEntry(GameObject representative, int order)
        {
            Representative = representative;
            Order = order;
        }

        internal GameObject Representative { get; }

        internal int Order { get; }
    }

    private sealed class AllocationEntry
    {
        internal AllocationEntry(TechType techType, int count, int slots, double remainder, GameObject representative, int order)
        {
            TechType = techType;
            Count = count;
            Slots = slots;
            Remainder = remainder;
            Representative = representative;
            Order = order;
        }

        internal TechType TechType { get; }

        internal int Count { get; }

        internal int Slots { get; set; }

        internal double Remainder { get; }

        internal GameObject Representative { get; }

        internal int Order { get; }
    }

    private static class StackCountResolver
    {
        private static readonly object Sync = new();

        private static bool initialized;
        private static MethodInfo countOfMethod;

        private static PropertyInfo irsMainSaveDataProperty;
        private static FieldInfo irsExtrasField;

        private static bool warned;

        internal static int GetStackUnits(Pickupable pickupable)
        {
            if (pickupable == null)
            {
                return 1;
            }

            EnsureInitialized();

            if (countOfMethod == null)
            {
                return 1;
            }

            try
            {
                object value = countOfMethod.Invoke(null, new object[] { pickupable });
                if (value is int intValue && intValue > 0)
                {
                    return intValue;
                }
            }
            catch (Exception ex)
            {
                if (!warned)
                {
                    warned = true;
                    Plugin.Log?.LogWarning($"Stack count resolver failed. Falling back to vanilla units. {ex.Message}");
                }
            }

            return 1;
        }

        internal static void TryApplyContainerAdjustments(ItemsContainer container, Dictionary<TechType, int> countsByTechType)
        {
            if (container == null || countsByTechType == null || countsByTechType.Count == 0)
            {
                return;
            }

            EnsureInitialized();

            IDictionary extras = GetInventoryResourceStacksExtras(container);
            if (extras == null)
            {
                return;
            }

            try
            {
                List<TechType> techTypes = new(countsByTechType.Keys);
                for (int i = 0; i < techTypes.Count; i++)
                {
                    TechType techType = techTypes[i];
                    if (!extras.Contains(techType))
                    {
                        continue;
                    }

                    int extraUnits = Convert.ToInt32(extras[techType]);
                    if (extraUnits > 0)
                    {
                        countsByTechType[techType] += extraUnits;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!warned)
                {
                    warned = true;
                    Plugin.Log?.LogWarning($"Inventory Resource Stacks adjustment failed. {ex.Message}");
                }
            }
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            lock (Sync)
            {
                if (initialized)
                {
                    return;
                }

                countOfMethod = ResolveCountOfMethod();
                ResolveInventoryResourceStacksMembers();
                initialized = true;

                if (countOfMethod == null && irsMainSaveDataProperty == null)
                {
                    Plugin.Log?.LogInfo("No stack quantity provider detected. Using vanilla unit-per-item visualization.");
                }
            }
        }

        private static MethodInfo ResolveCountOfMethod()
        {
            MethodInfo method = ResolveKnownMethod("MR_InventoryStacking.MRStack", "CountOf");
            if (method != null)
            {
                Plugin.Log?.LogInfo("Detected MR_InventoryStacking stack quantity provider.");
                return method;
            }

            return ResolveGenericCountMethod();
        }

        private static void ResolveInventoryResourceStacksMembers()
        {
            Type pluginType = AccessTools.TypeByName("InventoryStacks.VirtualStackPlugin");
            if (pluginType == null)
            {
                return;
            }

            irsMainSaveDataProperty = pluginType.GetProperty("MainSaveData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Type saveDataType = AccessTools.TypeByName("InventoryStacks.ModSaveData");
            if (saveDataType != null)
            {
                irsExtrasField = saveDataType.GetField("extras", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (irsMainSaveDataProperty != null && irsExtrasField != null)
            {
                Plugin.Log?.LogInfo("Detected Inventory Resource Stacks quantity provider.");
            }
        }

        private static IDictionary GetInventoryResourceStacksExtras(ItemsContainer container)
        {
            if (irsMainSaveDataProperty == null || irsExtrasField == null)
            {
                return null;
            }

            Inventory main = Inventory.main;
            if (container != main?.container)
            {
                return null;
            }

            object saveData = irsMainSaveDataProperty.GetValue(null, null);
            if (saveData == null)
            {
                return null;
            }

            return irsExtrasField.GetValue(saveData) as IDictionary;
        }

        private static MethodInfo ResolveKnownMethod(string typeName, string methodName)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                return null;
            }

            return type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Pickupable) }, null);
        }

        private static MethodInfo ResolveGenericCountMethod()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                Assembly assembly = assemblies[assemblyIndex];
                Type[] types;

                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
                {
                    Type type = types[typeIndex];
                    if (type == null)
                    {
                        continue;
                    }

                    MethodInfo method = FindCountMethodCandidate(type);
                    if (method != null)
                    {
                        Plugin.Log?.LogInfo($"Detected stack quantity provider: {type.FullName}.{method.Name}");
                        return method;
                    }
                }
            }

            return null;
        }

        private static MethodInfo FindCountMethodCandidate(Type type)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                if (method.ReturnType != typeof(int))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(Pickupable))
                {
                    continue;
                }

                string name = method.Name;
                if (name.IndexOf("count", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("stack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("amount", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("unit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return method;
                }
            }

            return null;
        }
    }
}
