using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VehiclesLib
{
    public class VehiclesLibMod : ModSystem
    {
        private readonly Harmony harmony = new Harmony("fr.mobydick.vehicleslib");

        // Called on server and client
        // Useful for registering block/entity classes on both sides
        public override void Start(ICoreAPI api)
        {
            api.RegisterEntity("EntityVehicle", typeof(EntityVehicle));
            api.RegisterMountable("vehicle", EntityVehicleSeat.GetMountable);
            api.RegisterEntityBehaviorClass("vehicleopenablecontainer", typeof(EntityBehaviorVehicleContainer));

            harmony.PatchAll();
        }
    }

    [HarmonyPatch(typeof(BlockLiquidContainerBase), nameof(BlockLiquidContainerBase.OnHeldInteractStart))]
    public class vehicleslib_liquidcontainerpatch
    {
        public static void Postfix(BlockLiquidContainerBase __instance, ItemSlot itemslot, EntityAgent byEntity, ref BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            if (entitySel?.Entity is EntityVehicle && itemslot.Itemstack.Collectible.Attributes["ExplosiveFuelProps"] != null)
            {
                blockSel = null;
                handHandling = EnumHandHandling.NotHandled;
                return;
            }
        }
    }
}
