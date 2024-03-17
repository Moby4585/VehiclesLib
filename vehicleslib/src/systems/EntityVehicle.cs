using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Util;
using Vintagestory.Common;
using static Vintagestory.GameContent.BlockLiquidContainerBase;

namespace VehiclesLib
{
    public class EntityVehicle : Entity, IRenderer, IMountableSupplier
    {
        /* Watched Attributes list :
         * fuelStack : ItemStack, the itemstack containing fuel (only used for steam (combustible type) and gas (fuel type) engines)
         * fuelTimer : float, the amount of time (in ms) this particular item has been burning for (starts at max then decrements)
         * 
         * Steam engine exclusives :
         * boilerTemp : the current boiler temperature
         * waterAmount : float, the amount in litres of water in the boiler
         * targetTemp : float, the current target temperature from the last burning item
         * 
         * Control attributes :
         * steerAngle : the current steering angle, for use by animations (not synced by the server by default)
        */

        public EntityVehicleSeat[] Seats;

        public string defaultSuggestedAnimation = "sitflooridle";
        public string className = "vehicle";

        public VehicleProps vehicleProps;

        // current forward speed
        public double ForwardSpeed = 0.0;

        // current turning speed (rad/tick)
        public double AngularVelocity = 0.0;

        // Current engine speed. Used for fuel consumption and animation
        public double EngineSpeed = 0.0;
        public bool isControlled = false;

        // Current altitude gain/loss speed for flight
        public double VerticalSpeed = 0.0;

        public float fuelSpeed = 1f;

        VehicleSoundSystem modsysSounds;

        Shape vehicleShape;

        public override bool ApplyGravity
        {
            get { return vehicleProps.useGravity; }
        }

        public override bool IsInteractable
        {
            get { return true; }
        }


        public override float MaterialDensity
        {
            get { return vehicleProps.density; }
        }

        public override double SwimmingOffsetY
        {
            get { return vehicleProps.SwimmingYOffset; }
        }

        /// <summary>
        /// The speed this boat can reach at full power
        /// </summary>
        public virtual float SpeedMultiplier => 1f;

        public double RenderOrder => 0;
        public int RenderRange => 999;

        public IMountable[] MountPoints => Seats;

        public Vec3f[] MountOffsets = new Vec3f[] { new Vec3f(-0.6f, 0.2f, 0), new Vec3f(0.7f, 0.2f, 0) };

        ICoreClientAPI capi;

        public EntityVehicle()
        {
            Seats = new EntityVehicleSeat[32];
            for (int i = 0; i < Seats.Length; i++) Seats[i] = new EntityVehicleSeat(this, i, new Vec3f(0, 0, 0));
            Seats[0].controllable = true;
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            vehicleProps = properties.Attributes["VehicleProps"].AsObject<VehicleProps>() ?? new VehicleProps();

            List<EntityVehicleSeat> seatsList = Seats.ToList();
            Seats = new EntityVehicleSeat[vehicleProps.seats.Count];
            for (int i = 0; i < Seats.Length; i++)
            {
                Seats[i] = seatsList[i];
                Seats[i].mountOffset = vehicleProps.seats[i].offset;
                Seats[i].controllable = vehicleProps.seats[i].isControllable;
                Seats[i].suggestedAnimation = vehicleProps.seats[i].suggestedAnimation == "" ? defaultSuggestedAnimation : vehicleProps.seats[i].suggestedAnimation;
                Seats[i].MountAngleMode = (EnumMountAngleMode)vehicleProps.seats[i].angleMode;
                Seats[i].lockYaw = vehicleProps.seats[i].lockBodyYaw;
                
                // A implémenter : gestion des sièges liés à des parts, gestion de la visée à la souris
            }

            ItemStack fuelStack = WatchedAttributes.GetItemstack("fuelStack");
            bool hasResolved = fuelStack?.ResolveBlockOrItem(Api.World) ?? false;
            WatchedAttributes.SetItemstack("fuelStack", hasResolved ? fuelStack : null);

            capi = api as ICoreClientAPI;
            if (capi != null)
            {
                capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "vehiclesim");
                //modsysSounds = api.ModLoader.GetModSystem<ModSystemVehicleSound>();

                // charger les sons sur le VehicleSoundSystem
                modsysSounds = new VehicleSoundSystem();
                modsysSounds.loadSounds(capi, vehicleProps.engineSound, vehicleProps.idleSound);
            }

            if (api.Side == EnumAppSide.Server)
            {
                this.WatchedAttributes.MarkAllDirty();
            }

            // The mounted entity will try to mount as well, but at that time, the boat might not have been loaded, so we'll try mounting on both ends. 
            foreach (var seat in Seats)
            {
                if (seat.PassengerEntityIdForInit != 0 && seat.Passenger == null)
                {
                    var entity = api.World.GetEntityById(seat.PassengerEntityIdForInit) as EntityAgent;
                    if (entity != null)
                    {
                        entity.TryMount(seat);
                    }
                }
            }
        }


        public float xangle = 0, yangle = 0, zangle = 0;

        public virtual void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            // Client side we update every frame for smoother turning
            if (capi.IsGamePaused) return;

            updateVehicleAngleAndMotion(dt);

            long ellapseMs = capi.InWorldEllapsedMilliseconds;

            if (Swimming)
            {
                float intensity = 0.15f + GlobalConstants.CurrentWindSpeedClient.X * 0.9f;
                float diff = GameMath.DEG2RAD / 2f * intensity;
                xangle = GameMath.Sin((float)(ellapseMs / 1000.0 * 2)) * 8 * diff;
                yangle = GameMath.Cos((float)(ellapseMs / 2000.0 * 2)) * 3 * diff;
                zangle = -GameMath.Sin((float)(ellapseMs / 3000.0 * 2)) * 8 * diff;
            }


            var esr = Properties.Client.Renderer as EntityShapeRenderer;
            if (esr == null) return;

            esr.sidewaysSwivelAngle = vehicleProps.swivelAngle;
            esr.WindWaveIntensity = 0f;

            esr.xangle = xangle;
            esr.yangle = yangle;
            esr.zangle = zangle;

            if (vehicleShape != null && esr.OverrideEntityShape == null)
            {
                esr.OverrideEntityShape = vehicleShape.Clone();
            }
            if (esr.OverrideEntityShape != null)
            {
                foreach (AnimPart part in vehicleProps.wheels)
                {
                    if (!part.enabled) continue;
                    ShapeElement shape = esr.OverrideEntityShape.GetElementByName(part.partName);
                    if (shape == null) continue;
                    shape.RotationZ += part.strength * dt * (float)SidedPos.Motion.Dot(SidedPos.GetViewVector().ToVec3d()) * 360f;
                }
                foreach (AnimPart part in vehicleProps.steer)
                {
                    if (!part.enabled) continue;
                    ShapeElement shape = esr.OverrideEntityShape.GetElementByName(part.partName);
                    if (shape == null) continue;
                    shape.RotationY += (part.strength * AngularVelocity - esr.OverrideEntityShape.GetElementByName(part.partName).RotationY) * dt * 5f;
                    // Formule pour adoucir le mouvement
                }
                foreach (AnimPart part in vehicleProps.propellers)
                {
                    if (!part.enabled) continue;
                    ShapeElement shape = esr.OverrideEntityShape.GetElementByName(part.partName);
                    if (shape == null) continue;
                    shape.RotationZ += part.strength * dt * (float)EngineSpeed * 360f;
                }

                // A rajouter : la visée de pièce en fonction des sièges


                esr.MarkShapeModified();
            }

            bool selfSitting = false;

            foreach (var seat in Seats)
            {
                selfSitting |= seat.Passenger == capi.World.Player.Entity;
                var pesr = seat.Passenger?.Properties?.Client.Renderer as EntityShapeRenderer;
                if (pesr != null)
                {
                    pesr.xangle = xangle;
                    pesr.yangle = yangle;
                    pesr.zangle = zangle;

                    pesr.bodyYawLerped = SidedPos.Yaw;
                }
            }

            if (selfSitting)
            {
                modsysSounds.NowInMotion((float)Pos.Motion.Length(), Pos);
            }
            else
            {
                modsysSounds.NotMounted();
            }
        }


        public override void OnGameTick(float dt)
        {
            if (World.Side == EnumAppSide.Server)
            {
                updateVehicleAngleAndMotion(dt);
                updateVehicleFuel(dt);
            }

            base.OnGameTick(dt);
        }

        public override void OnAsyncParticleTick(float dt, IAsyncParticleManager manager)
        {
            base.OnAsyncParticleTick(dt, manager);

        }

        bool justCollided = false;
        CollisionTester collisionTester = new CollisionTester();

        protected virtual void updateVehicleAngleAndMotion(float dt)
        {

            // Ignore lag spikes
            dt = Math.Min(0.5f, dt);

            float step = GlobalConstants.PhysicsFrameTime;
            EntityControls pilotControls = null;
            var motion = SeatsToMotion(step, ref pilotControls);

            double waterSpeedMultiplier = Swimming ? vehicleProps.waterMultiplier : vehicleProps.groundMultiplier;

            // Add some easing to it
            ForwardSpeed += ((motion.X * (hasFuel() ? 1f : 0f)) * (vehicleProps.topSpeed * waterSpeedMultiplier * getFuelSpeed()) - ForwardSpeed) * dt * vehicleProps.acceleration /* pas sûr du acceleration */;

            AngularVelocity += (motion.Y * vehicleProps.turnSpeed - AngularVelocity) * dt * 5;

            VerticalSpeed += (((motion.Z != 0)  && (!vehicleProps.poweredFlightOnly || hasFuel())) ? (motion.Z * vehicleProps.climbSpeed - VerticalSpeed) : (-vehicleProps.fallSpeed * dt - VerticalSpeed)) * dt * vehicleProps.acceleration;

            float desiredEngineSpeed = (motion.Length() > 0 || (vehicleProps.poweredFlightOnly && (!this.OnGround && !this.Swimming))) ? 1 : 0;

            EngineSpeed += (desiredEngineSpeed - EngineSpeed) * dt * 5f * vehicleProps.acceleration;
            if (EngineSpeed < 0.01f) EngineSpeed = 0f;
            if (EngineSpeed > 0.99f) EngineSpeed = 1f;
            
            var pos = SidedPos;

            ForwardSpeed = vehicleProps.canReverse ? ForwardSpeed : Math.Max(0, ForwardSpeed);

            if (vehicleProps.canFly)
            {
                if (VerticalSpeed != 0)
                {
                    pos.Motion.Y = VerticalSpeed;
                }
            }

            if (ForwardSpeed != 0.0)
            {
                if ((pilotControls?.Jump ?? false) && vehicleProps.canBrake)
                {
                    ForwardSpeed += (0 - ForwardSpeed) * dt * vehicleProps.acceleration * 5;
                    //if (ForwardSpeed < vehicleProps.topSpeed / 5) ForwardSpeed = 0f;
                }

                var targetmotion = pos.GetViewVector().Mul((float)-ForwardSpeed).ToVec3d();
                pos.Motion.X = targetmotion.X;
                pos.Motion.Z = targetmotion.Z;

                if (vehicleProps.canStepUp) // Code pour le step up
                {
                    if (this.CollidedHorizontally)
                    {
                        bool canClimb = !collisionTester.IsColliding(Api.World.BlockAccessor, this.CollisionBox.OffsetCopy(0, vehicleProps.stepHeight, 0), pos.XYZ + pos.Motion * 0.00001d, true);

                        if (canClimb)
                        {
                            pos.Motion.Y = Math.Abs(ForwardSpeed);
                            justCollided = true;
                        }
                        else if (justCollided)
                        {
                            pos.Motion.Y = 0;
                            justCollided = false;
                        }
                    }
                    else if (justCollided)
                    {
                        pos.Motion.Y = 0;
                        justCollided = false;
                    }
                }
            }

            if (AngularVelocity != 0.0)
            {
                pos.Yaw += (float)AngularVelocity * dt * (vehicleProps.movingTurnOnly ? (float)ForwardSpeed : 1f);
            }
        }

        protected virtual void updateVehicleFuel (float dt)
        {
            float fuelTimer = this.WatchedAttributes.GetFloat("fuelTimer", 0f);

            // TODO : vérifier qu'il faut bien retirer du carburant -> idle monté vs pas monté

            switch (vehicleProps.engineProps.fuelType)
            {
                case EngineProps.FuelType.none: 
                    
                    break;
                case EngineProps.FuelType.steam:
                    ItemStack burnerStack = WatchedAttributes.GetItemstack("fuelStack");
                    if (burnerStack?.Collectible == null && burnerStack != null) burnerStack.ResolveBlockOrItem(Api.World);
                    fuelTimer -= dt;

                    float waterAmount = WatchedAttributes.GetFloat("waterAmount");

                    if (fuelTimer <= 0 && burnerStack != null && burnerStack?.Collectible?.CombustibleProps != null)
                    {
                        burnerStack.StackSize -= 1;
                        if (burnerStack != null) fuelTimer += (burnerStack.Collectible?.CombustibleProps?.BurnDuration ?? 0f);
                        WatchedAttributes.SetFloat("targetTemp", burnerStack.Collectible?.CombustibleProps?.BurnTemperature ?? 0f);
                        burnerStack = burnerStack.StackSize > 0 ? burnerStack : null;
                        WatchedAttributes.SetItemstack("fuelStack", burnerStack);
                        WatchedAttributes.MarkPathDirty("fuelStack");
                    }

                    fuelTimer = Math.Max(0, fuelTimer);

                    if (fuelTimer > 0)
                    {
                        float boilerTemp = WatchedAttributes.GetFloat("boilerTemp");

                        waterAmount -= dt * (EngineSpeed > 0f ? (vehicleProps.engineProps.consumptionRatio * (boilerTemp / 900f)) : (isControlled || vehicleProps.engineProps.alwaysIdle ? (vehicleProps.engineProps.idleConsumption * (boilerTemp / 900f)) : 0f));

                        waterAmount = Math.Max(waterAmount, 0);
                        WatchedAttributes.SetFloat("waterAmount", waterAmount);
                    }

                    float desiredTemp = WatchedAttributes.GetFloat("targetTemp");
                    if (fuelTimer > 0 && desiredTemp > 0)
                    {
                        float currentBoilerTemp = WatchedAttributes.GetFloat("boilerTemp");
                        currentBoilerTemp = Math.Min(currentBoilerTemp + dt * 20f, desiredTemp);
                        WatchedAttributes.SetFloat("boilerTemp", currentBoilerTemp);
                    }

                    fuelTimer = Math.Max(fuelTimer, 0);
                    WatchedAttributes.SetFloat("fuelTimer", fuelTimer);
                    break;
                case EngineProps.FuelType.gas:
                    ItemStack gasStack = WatchedAttributes.GetItemstack("fuelStack");
                    if (gasStack?.Collectible == null && gasStack != null) gasStack.ResolveBlockOrItem(Api.World);
                    fuelTimer -= dt * (EngineSpeed > 0f ? vehicleProps.engineProps.consumptionRatio : (isControlled || vehicleProps.engineProps.alwaysIdle ? vehicleProps.engineProps.idleConsumption : 0f));
                    // Math.Abs(ForwardSpeed) > (vehicleProps.topSpeed * 0.1f * dt)

                    if (fuelTimer <= 0 && gasStack != null)
                    {
                        gasStack.StackSize -= 1;
                        gasStack = gasStack.StackSize > 0 ? gasStack : null;
                        if (gasStack != null) fuelTimer += (gasStack.Collectible?.Attributes["ExplosiveFuelProps"]?.AsObject<ExplosiveFuelProps>().energy ?? 0f);
                        WatchedAttributes.SetItemstack("fuelStack", gasStack);
                        WatchedAttributes.MarkPathDirty("fuelStack");
                    }
                    if (gasStack == null) fuelTimer = 0f; // NON TESTE

                    fuelTimer = Math.Max(0, fuelTimer);
                    WatchedAttributes.SetFloat("fuelTimer", fuelTimer);
                    break;
                case EngineProps.FuelType.temporal:

                    fuelTimer -= dt * 1000 * (EngineSpeed > 0f ? vehicleProps.engineProps.consumptionRatio : (isControlled || vehicleProps.engineProps.alwaysIdle ? vehicleProps.engineProps.idleConsumption : 0f));
                    WatchedAttributes.SetFloat("fuelTimer", Math.Max(0f, fuelTimer));
                    WatchedAttributes.MarkPathDirty("fuelTimer");
                    break;
            }
        }

        protected virtual bool hasFuel()
        {
            switch (vehicleProps.engineProps.fuelType)
            {
                case EngineProps.FuelType.none:
                    return true;

                case EngineProps.FuelType.steam:
                    float boilerTemp = this.WatchedAttributes.GetFloat("boilerTemp", 0f);
                    float waterAmount = this.WatchedAttributes.GetFloat("waterAmount", 0f);

                    return boilerTemp >= 100f && waterAmount > 0f;

                case EngineProps.FuelType.gas:
                    ItemStack gasStack = this.WatchedAttributes.GetItemstack("fuelStack");
                    if (gasStack?.Collectible == null && gasStack != null) gasStack.ResolveBlockOrItem(Api.World);
                    float gasAmount = this.WatchedAttributes.GetFloat("fuelTimer", 0f);

                    return (gasStack?.StackSize ?? 0f) > 0f || gasAmount > 0f;

                case EngineProps.FuelType.temporal:
                    ItemStack temporalStack = this.WatchedAttributes.GetItemstack("fuelStack");
                    if (temporalStack?.Collectible == null && temporalStack != null) temporalStack.ResolveBlockOrItem(Api.World);
                    float temporalTimer = this.WatchedAttributes.GetFloat("fuelTimer", 0f);
                    return (temporalStack?.StackSize ?? 0f) > 0f || temporalTimer > 0f;
            }

            return false;
        }

        protected virtual float getFuelSpeed()
        {
            switch (vehicleProps.engineProps.fuelType)
            {
                case EngineProps.FuelType.none:
                    return 1f;

                case EngineProps.FuelType.steam:
                    float boilerTemp = this.WatchedAttributes.GetFloat("boilerTemp", 0);

                    return boilerTemp/900f ;

                case EngineProps.FuelType.gas:
                    ItemStack gasStack = this.WatchedAttributes.GetItemstack("fuelStack");
                    if (gasStack?.Collectible == null && gasStack != null) gasStack.ResolveBlockOrItem(Api.World);

                    ExplosiveFuelProps fuelProps = gasStack?.Collectible?.Attributes?["ExplosiveFuelProps"]?.AsObject<ExplosiveFuelProps>() ?? null;

                    return fuelProps?.powerRating ?? 0f;

                case EngineProps.FuelType.temporal:
                    return 1f;
            }

            return 1f;
        }

        public virtual Vec3d SeatsToMotion(float dt, ref EntityControls pilotControls)
        {
            double linearMotion = 0;
            double angularMotion = 0;
            double verticalMotion = 0;

            isControlled = false; // Vérification qu'un pilote est quelque part sur la machine

            foreach (var seat in Seats)
            {
                if (seat.Passenger != null && seat.lockYaw)
                {
                    seat.Passenger.BodyYaw = this.SidedPos.Yaw;
                }

                if (seat.Passenger == null || !seat.controllable) continue;

                isControlled = true;

                var controls = seat.controls;
                pilotControls = controls;

                if (controls.Left || controls.Right)
                {
                    float dir = controls.Left ? 1 : -1;
                    angularMotion += dir * dt;
                }

                if (controls.Jump || controls.Sprint)
                {
                    float dir = controls.Jump ? 1 : -1;
                    verticalMotion += dir * dt;
                }

                if (controls.Forward || controls.Backward)
                {
                    float dir = controls.Forward ? 1 : -1;

                    //var yawdist = Math.Abs(GameMath.AngleRadDistance(SidedPos.Yaw, seat.Passenger.SidedPos.Yaw));
                    //bool isLookingBackwards = yawdist > GameMath.PIHALF;

                    //if (isLookingBackwards) dir *= -1;

                    linearMotion += dir * dt;
                }

                // Only the first player can control the boat
                // Reason: Very difficult to properly smoothly synchronize that over the network
                break;
            }

            return new Vec3d(linearMotion, angularMotion, verticalMotion);
        }


        public virtual bool IsMountedBy(Entity entity)
        {
            foreach (var seat in Seats)
            {
                if (seat.Passenger == entity) return true;
            }
            return false;
        }

        public virtual Vec3f GetMountOffset(Entity entity)
        {
            foreach (var seat in Seats)
            {
                if (seat.Passenger == entity)
                {
                    return seat.MountOffset;
                }
            }
            return null;
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
        {
            if (mode != EnumInteractMode.Interact)
            {
                return; // Changer pour supporter Attack pour pousser
            }

            // Try refueling, potentially add repair logic here as well
            if (itemslot != null && itemslot.Itemstack != null)
            {
                if (tryRefuel(byEntity, itemslot)) return;
            }

            // sneak + click to remove boat --- NOTE : rajouter la vérification de l'inventaire vide
            if (byEntity.Controls.CtrlKey && IsEmpty() && vehicleProps.canBePickedUp && itemslot.Empty)
            {
                foreach (var seat in Seats)
                {
                    seat.Passenger?.TryUnmount();
                }

                ItemStack stack = new ItemStack(World.GetItem(Code));
                if (!byEntity.TryGiveItemStack(stack))
                {
                    World.SpawnItemEntity(stack, ServerPos.XYZ);
                }
                Die();
                return;
            }

            if (byEntity.Controls.CtrlKey)
            {
                // Purger le réservoir ?
            }

            if (World.Side == EnumAppSide.Server && !byEntity.Controls.ShiftKey && !byEntity.Controls.CtrlKey)
            {
                foreach (var seat in Seats)
                {
                    if (byEntity.MountedOn == null && seat.Passenger == null)
                    {
                        byEntity.TryMount(seat);
                    }
                }
            }
        }

        public bool tryRefuel(EntityAgent byEntity, ItemSlot itemSlot)
        {
            switch (vehicleProps.engineProps.fuelType)
            {
                case EngineProps.FuelType.none:
                    break;
                case EngineProps.FuelType.steam:
                    if (itemSlot.Itemstack.Collectible is ILiquidSource lqInterface)
                    {
                        if (!lqInterface.AllowHeldLiquidTransfer) return false;

                        ItemStack liquidStack = lqInterface.GetContent(itemSlot.Itemstack);
                        if (liquidStack == null) return false;

                        float waterItemsPerLitre = BlockLiquidContainerBase.GetContainableProps(liquidStack).ItemsPerLitre;
                        int waterAmountItems = (int)(this.WatchedAttributes.GetFloat("waterAmount", 0f) * waterItemsPerLitre);
                        int tankCapacityItems = (int)(vehicleProps.engineProps.tankCapacity * waterItemsPerLitre);

                        int amountToTransfer = (Math.Min(liquidStack.StackSize, tankCapacityItems - waterAmountItems));

                        ItemStack takenContent = lqInterface.TryTakeContent(itemSlot.Itemstack, amountToTransfer);

                        float waterToTankRatio = waterAmountItems / (tankCapacityItems == 0 ? 1 : tankCapacityItems);

                        waterAmountItems += takenContent.StackSize;
                        this.WatchedAttributes.SetFloat("waterAmount", ((float)waterAmountItems) / waterItemsPerLitre);

                        this.WatchedAttributes.SetFloat("boilerTemp", WatchedAttributes.GetFloat("boilerTemp", 0f) * waterToTankRatio);

                        if (takenContent.StackSize > 0)
                        {
                            DoLiquidMovedEffects(byEntity as IPlayer, takenContent, takenContent.StackSize, EnumLiquidDirection.Pour);
                        }

                        return true;
                    }
                    if (itemSlot.Itemstack.Collectible.CombustibleProps != null)
                    {
                        ItemStack coalStack = WatchedAttributes.GetItemstack("fuelStack");
                        if (coalStack?.Collectible == null && coalStack != null) coalStack.ResolveBlockOrItem(Api.World);
                        if (coalStack == null)
                        {
                            coalStack = itemSlot.Itemstack.Clone();
                            itemSlot.Itemstack = null;
                            WatchedAttributes.SetItemstack("fuelStack", coalStack);
                            return true;
                        }
                        if (coalStack.Collectible == itemSlot.Itemstack.Collectible)
                        {
                            int amountToTransfer = coalStack.Collectible.MaxStackSize - coalStack.StackSize;
                            ItemStack outStack = itemSlot.TakeOut(amountToTransfer);
                            coalStack.StackSize += outStack.StackSize;
                            WatchedAttributes.SetItemstack("fuelStack", coalStack);
                            return true;
                        }
                        return false;
                    }
                    break;
                case EngineProps.FuelType.gas:

                    bool singleTake = byEntity.Controls.ShiftKey;
                    bool singlePut = byEntity.Controls.CtrlKey;

                    ItemStack gasStack = WatchedAttributes.GetItemstack("fuelStack");
                    if (gasStack?.Collectible == null && gasStack != null) gasStack.ResolveBlockOrItem(Api.World);

                    if (itemSlot.Itemstack.Collectible is ILiquidSource lqSource && !singleTake)
                    {
                        if (!lqSource.AllowHeldLiquidTransfer) return false;

                        ItemStack sourceContent = lqSource.GetContent(itemSlot.Itemstack);
                        if (sourceContent == null) return false;
                        if (!sourceContent.Collectible.Attributes["ExplosiveFuelProps"].Exists) return false;

                        if (gasStack != null && gasStack?.Collectible != sourceContent.Collectible) return false;

                        float litres = singlePut ? lqSource.TransferSizeLitres : lqSource.CapacityLitres;

                        float litresToPut = Math.Min(Math.Min(lqSource.GetCurrentLitres(itemSlot.Itemstack), litres), vehicleProps.engineProps.tankCapacity - (gasStack == null ? 0f : (gasStack.StackSize / BlockLiquidContainerBase.GetContainableProps(gasStack).ItemsPerLitre)));

                        if (litresToPut > 0f)
                        {
                            int amountToPut = (int)(litresToPut * GetContainableProps(sourceContent).ItemsPerLitre);
                            ItemStack takenStack = lqSource.TryTakeContent(itemSlot.Itemstack, amountToPut);
                            if (gasStack == null)
                            {
                                gasStack = takenStack.Clone();
                            }
                            else
                            {
                                gasStack.StackSize += takenStack.StackSize;
                            }
                            WatchedAttributes.SetItemstack("fuelStack", gasStack);
                            DoLiquidMovedEffects(byEntity as IPlayer, gasStack, amountToPut, EnumLiquidDirection.Pour);
                            return true;
                        }
                    }
                    if (itemSlot.Itemstack.Collectible is ILiquidSink lqSink && !singlePut)
                    {
                        if (!lqSink.AllowHeldLiquidTransfer) return false;

                        ItemStack sourceContent = lqSink.GetContent(itemSlot.Itemstack);
                        // if (sourceContent == null) return false;
                        // if (sourceContent.Collectible.Attributes["ExplosiveFuelProps"] == null) return false;


                        float litres = singleTake ? lqSink.TransferSizeLitres : lqSink.CapacityLitres;

                        int takenItems = lqSink.TryPutLiquid(itemSlot.Itemstack, gasStack, litres);
                        if (takenItems > 0)
                        {
                            if (gasStack != null) gasStack.StackSize -= takenItems;
                            //if (sourceContent != null) sourceContent.StackSize += takenItems;

                            if (gasStack.StackSize <= 0)
                            {
                                gasStack = null;
                            }
                            DoLiquidMovedEffects(byEntity as IPlayer, lqSink.GetContent(itemSlot.Itemstack), takenItems, EnumLiquidDirection.Fill);
                            WatchedAttributes.SetItemstack("fuelStack", gasStack);
                            return true;
                        }
                    }
                    break;
                case EngineProps.FuelType.temporal:
                    if (itemSlot.Itemstack.Collectible.Attributes["TemporalFuelProps"].Exists)
                    {
                        WatchedAttributes.SetFloat("fuelTimer", itemSlot.Itemstack.Collectible.Attributes["TemporalFuelProps"].AsObject<TemporalFuelProps>().burnTimeSeconds * 1000f
                            + WatchedAttributes.GetFloat("fuelTimer", 0));
                        itemSlot.TakeOut(1);
                        return true;
                    }
                    break;
            }
            return false;
        }


        public static Vec3d Vec3dFromYaw(float yawRad)
        {
            return new Vec3d(Math.Cos(yawRad), 0.0, -Math.Sin(yawRad));
        }

        public override bool CanCollect(Entity byEntity)
        {
            return false;
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);

            writer.Write(Seats.Length);

            foreach (var seat in Seats)
            {
                writer.Write(seat.Passenger?.EntityId ?? (long)0);
            }

            byte[] fuelStackData = this.WatchedAttributes.GetItemstack("fuelStack", null)?.ToBytes() ?? new byte[0];
            writer.Write(fuelStackData.Length);
            if (fuelStackData.Length > 0) writer.Write(fuelStackData);
        }

        public override void FromBytes(BinaryReader reader, bool fromServer)
        {
            base.FromBytes(reader, fromServer);

            int numseats = reader.ReadInt32();

            for (int i = 0; i < numseats; i++)
            {
                long entityId = reader.ReadInt64();
                Seats[i].PassengerEntityIdForInit = entityId;
            }

            int fuelStackSize = reader.ReadInt32();
            if (fuelStackSize > 0)
            {
                byte[] itemByteBuffer = reader.ReadBytes(fuelStackSize);
                ItemStack fuelStack = new ItemStack(itemByteBuffer);
                this.WatchedAttributes.SetItemstack("fuelStack", fuelStack);
                WatchedAttributes.MarkPathDirty("fuelStack");
            }
        }

        public virtual bool IsEmpty()
        {
            return !Seats.Any(seat => seat.Passenger != null);
        }

        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
        {
            return base.GetInteractionHelp(world, es, player);
        }

        public override string GetInfoText()
        {
            string text = base.GetInfoText() ?? "";

            switch (vehicleProps.engineProps.fuelType)
            {
                case EngineProps.FuelType.none:
                    
                    break;
                case EngineProps.FuelType.steam:
                    text += "\n<strong>Temperature:</strong> " + this.WatchedAttributes.GetFloat("boilerTemp", 0f).ToString("0") + "°C";
                    text += "\n<strong>Water level:</strong> " + this.WatchedAttributes.GetFloat("waterAmount", 0f).ToString("G")
                        + " L / " + vehicleProps.engineProps.tankCapacity.ToString() + " L";
                    ItemStack burnerStack = this.WatchedAttributes.GetItemstack("fuelStack", null);
                    if (burnerStack?.Collectible == null && burnerStack != null) burnerStack.ResolveBlockOrItem(Api.World);
                    text += "\n<strong>Fuel:</strong> " + ((burnerStack != null) ? Lang.Get("{0}x {1}", burnerStack.StackSize, burnerStack.GetName()) : "None");
                    break;
                case EngineProps.FuelType.gas:
                    ItemStack fuelStack = this.WatchedAttributes.GetItemstack("fuelStack", null);
                    if (fuelStack?.Collectible == null && fuelStack != null) fuelStack.ResolveBlockOrItem(Api.World);
                    /*if (fuelStack.Collectible == null) text += "Null fuel";
                    else text += "Not null fuel";*/
                    if (fuelStack != null && (fuelStack?.StackSize ?? 0) > 0)
                        if (fuelStack.Collectible == null)
                        {
                            text += "null fuel : " + fuelStack.StackSize.ToString();
                        }
                        else
                            text += Lang.Get("\n{0:G} L / {1:G} L of {2}",
                                (((float)fuelStack.StackSize) / GetContainableProps(fuelStack).ItemsPerLitre),
                                vehicleProps.engineProps.tankCapacity,
                                fuelStack.GetName());
                    else text += "\nNo fuel";
                    break;
                case EngineProps.FuelType.temporal:
                    ItemStack temporalStack = this.WatchedAttributes.GetItemstack("fuelStack", null);
                    if (temporalStack?.Collectible == null && temporalStack != null) temporalStack.ResolveBlockOrItem(Api.World);
                    float temporalTimer = this.WatchedAttributes.GetFloat("fuelTimer", 0f);

                    float remainingTime = (temporalTimer / 1000f)
                        + (temporalStack?.Collectible?.Attributes["TemporalFuelProps"]?.AsObject<TemporalFuelProps>().burnTimeSeconds ?? 0f);
                    remainingTime /= vehicleProps.engineProps.consumptionRatio;
                    text += "\n<strong>Remaining time:</strong> " + ((int)Math.Floor(remainingTime/60f)).ToString() + ":" + ((int)(remainingTime % 60)).ToString();

                    break;
            }
            //text += "\nTimer: " + WatchedAttributes.GetFloat("fuelTimer", 0f).ToString();
            //text += "\nEngine speed: " + EngineSpeed.ToString();
            return text;

            // A faire : afficher la quantité de carburant, la température et autres
        }

        

        public void DoLiquidMovedEffects(IPlayer player, ItemStack contentStack, int moved, EnumLiquidDirection dir)
        {
            if (player == null) return;

            WaterTightContainableProps props = GetContainableProps(contentStack);
            float litresMoved = moved / props.ItemsPerLitre;

            (player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            Api.World.PlaySoundAt(dir == EnumLiquidDirection.Fill ? props.FillSound : props.PourSound, player.Entity, player, true, 16, GameMath.Clamp(litresMoved / 5f, 0.35f, 1f));
            Api.World.SpawnCubeParticles(player.Entity.Pos.AheadCopy(0.25).XYZ.Add(0, player.Entity.SelectionBox.Y2 / 2, 0), contentStack, 0.75f, (int)litresMoved * 2, 0.45f);
        }

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging)
        {
            if (vehicleShape == null) vehicleShape = entityShape;

            base.OnTesselation(ref entityShape, shapePathForLogging);
        }

        public void Dispose()
        {
            modsysSounds.Dispose();
        }
    }

    public class VehicleProps
    {
        public bool canBePickedUp = true;

        public float density = 100f; // fait
        public float SwimmingYOffset = 0.45f; // fait

        public float topSpeed = 1f; // fait
        public float acceleration = 1f; // fait
        public float swivelAngle = 0f;
        public float groundMultiplier = 1f; // fait
        public float waterMultiplier = 0f; // fait
        public float turnSpeed = 45f; // fait
        public bool movingTurnOnly = false; // fait
        public bool canStepUp = true; // fait
        public float stepHeight = 1.1f; // fait. Note : peut être bizarre avec une stepheight beaucoup supérieure à 1
        public bool canReverse = true; // fait
        public bool canBrake = true; // fait

        public List<SeatPart> seats = new List<SeatPart>(); // fait
        public List<AnimPart> wheels = new List<AnimPart>(); // fait
        public List<AnimPart> steer = new List<AnimPart>(); // fait
        public List<AnimPart> propellers = new List<AnimPart>(); // fait
        public EngineProps engineProps;
        public float pushStrength = 1f;
        public string idleSound = "";
        public string engineSound = "";
        public bool useGravity = true; // fait
        public bool canFly = false; // fait
        public float climbSpeed = 0f; // fait
        public float fallSpeed = 0f; // fait
        public bool poweredFlightOnly = false; // fait
    }

    public class AnimPart
    {
        public string partName = "";
        public float strength = 1f;
        public bool enabled = true;
    }

    public class SeatPart
    {
        public bool isControllable = false;
        public int angleMode = 1;
        public bool lockBodyYaw = false;
        public Vec3f offset = new Vec3f(0, 0, 0);
        public string suggestedAnimation = "";
        public string parentPart = "";
        public string aimPart = "";
        public float maxAimAngle = 0f;
    }

    public class EngineProps
    {
        public enum FuelType
        {
            none,
            steam,
            gas,
            temporal
        }

        public FuelType fuelType = FuelType.none;
        public float idleConsumption = 0f;
        public float consumptionRatio = 0f;
        public float tankCapacity = 0f;
        public bool alwaysIdle = false;
    }

    public class ExplosiveFuelProps
    {
        public float powerRating = 1f;
        public float energy = 10f;
    }
    public class TemporalFuelProps
    {
        public float burnTimeSeconds = 3600f;
    }
}
