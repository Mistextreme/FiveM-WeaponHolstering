/*
 * 
 * Holstering and Quick Draw
 * Author: Timothy Dexter
 * Release: 0.1.0
 * Date: 01/04/18
 * 
 * 
 * Known Issues
 * - Issue reported where weapon on back is deleted but the handle != 0 and the 
 *   weapon on back != unarmed
 * 
 * Please send any edits/improvements/bugs to this script back to the author. 
 * 
 * Usage 
 * - Unholster or holster a weapon
 * - If on duty, hold (Capslock) while unarmed to enable quick draw 
 *	 While quick draw is enabled, press fire (LMB) to immediately pull a combat 
 *	 pistol from the holster.
 *          
 * History:
 * Revision 0.0.1 2017/11/29 20:35:15 EDT TimothyDexter 
 * - Initial release
 * Revision 0.0.2 2017/11/30 23:33:24 EDT TimothyDexter 
 * - Added checks to keep the default switch blade animation 
 * Revision 0.0.3 2017/12/05 17:34:12 EDT TimothyDexter 
 * - Added EnableControlActions calls to ToggleWeaponSwitchEvent
 * Revision 0.0.4 2017/12/06 16:23:54 EDT TimothyDexter 
 * - Added HandleEnterVehicleHolsterEvent to equip currect weapon when entering a vehicle
 * Revision 0.0.5 2017/12/08 22:54:44 EDT TimothyDexter 
 * - Set current weapon to unarmed on weapon drops, seizures, and storage
 * Revision 0.0.6 2017/12/09 23:31:18 EDT TimothyDexter 
 * - Fixed logic for weapon drop events
 * - A little cleanup of the duplicated method calls
 * - Added methods to disable controls during an extended delay so that weapon cannot be fired during unholstering
 * Revision 0.0.7 2017/12/12 23:01:08 EDT TimothyDexter
 * - Check for null on weapon attach delete
 * - Added logging to track down cause of weapon on back being deleted, but the handle != 0
 * Revision 0.0.8 2017/12/13 11:02:20 EDT TimothyDexter
 * - Only switch weapons in a vehicle when user inputs weapon switch
 * - Added HandleWeaponVehicleStashEventWrapper() for LEO weapon stash
 * Revision 0.0.9 2017/12/22 18:48:48 EDT TimothyDexter
 * - Added call to WeaponStash.HandleLeoEnterVehicleEvent to set the initial weapon stash choice for LEO
 * Revision 0.1.0 2018/01/04 21:25:05 EDT TimothyDexter
 * - No longer removing weapon prop upon entering vehicle
 * - Enter a vehicle with a unholsterable weapon equipped will now equip upon exit
 * 
 */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using Roleplay.Client.Classes.Jobs.Police;
using Roleplay.Client.Classes.Jobs.Police.Vehicle;
using Roleplay.Enums.Character;
using Roleplay.SharedClasses;
using Roleplay.SharedModels;
using Newtonsoft.Json;

namespace Roleplay.Client.Classes.Player
{
	internal static class WeaponUnholsterHandler
	{
		private const WeaponHash BuzzardHeliWeapHash = 0;
		private const WeaponHash LeoHolsteredWeapon = WeaponHash.CombatPistol;
		private const Control QuickDrawControl = Control.FrontendSelect; //Capslock
		private const int VehicleWeaponSwitchDelay = 1500;

		private static bool _currentlyUnholstering;
		private static bool _activeQuickDraw;
		private static bool _activeWeaponDrop;
		private static bool _activeUnstoreableDrop;
		private static bool _activeWeaponSeizure;
		private static bool _activeWeaponStorage;
		private static bool _activeWeaponQuickStash;
		private static bool _hasRaisedGun;
		private static bool _isInVehicle;

		public static WeaponHash CurrentWeapon = WeaponHash.Unarmed;
		private static WeaponHash _previousWeapon = WeaponHash.Unarmed;
		private static WeaponHash _weaponOnBack = WeaponHash.Unarmed;
		private static WeaponHash _unconcealableWeaponInHand = WeaponHash.Unarmed;

		public static HolsterLocations CurrentHolster = HolsterLocations.None;
		private static bool _pistolUnholstered;
		private static bool _drawReady;
		private static int _weaponOnBackHandle;

		private static readonly PlayerList PlayerList = new PlayerList();

		private static readonly Dictionary<HolsterLocations, HolsterModel> RetrieveAnim =
			new Dictionary<HolsterLocations, HolsterModel> {
				{HolsterLocations.None, new HolsterModel( "reaction@intimidation@1h", "intro", 1500, 0.15f, 1000 )},
				{HolsterLocations.Hip, new HolsterModel( "combat@reaction_aim@pistol", "0", 700, 0, 700 )},
				{HolsterLocations.Back, new HolsterModel( "combat@reaction_aim@pistol", "-0", 700, 0, 700 )}
			};

		private static readonly Dictionary<HolsterLocations, HolsterModel> StoreAnim =
			new Dictionary<HolsterLocations, HolsterModel> {
				{HolsterLocations.None, new HolsterModel( "reaction@intimidation@1h", "outro", 1500, 0.15f, 850 )},
				{HolsterLocations.Hip, new HolsterModel( "combat@reaction_aim@pistol", "0", 500, 0, 500 )},
				{HolsterLocations.Back, new HolsterModel( "combat@reaction_aim@pistol", "-0", 500, 0, 500 )}
			};

		private static readonly List<WeaponHash> LeoSideHipUtilityBelt = new List<WeaponHash> {
			WeaponHash.Nightstick,
			WeaponHash.Flare,
			WeaponHash.HeavyPistol,
			WeaponHash.Pistol,
			WeaponHash.Pistol50,
			WeaponHash.PistolMk2,
			WeaponHash.Revolver,
			WeaponHash.SNSPistol,
			WeaponHash.VintagePistol,
			WeaponHash.APPistol
		};

		private static readonly List<WeaponHash> CivilianConcealables = new List<WeaponHash> {
			WeaponHash.APPistol,
			WeaponHash.Ball,
			WeaponHash.BattleAxe,
			WeaponHash.Bottle,
			WeaponHash.BZGas,
			WeaponHash.CombatPistol,
			WeaponHash.CompactRifle,
			WeaponHash.Crowbar,
			WeaponHash.Dagger,
			WeaponHash.Flare,
			WeaponHash.FlareGun,
			WeaponHash.Flashlight,
			WeaponHash.Grenade,
			WeaponHash.Hammer,
			WeaponHash.Hatchet,
			WeaponHash.HeavyPistol,
			WeaponHash.Knife,
			WeaponHash.KnuckleDuster,
			WeaponHash.Machete,
			WeaponHash.MachinePistol,
			WeaponHash.MarksmanPistol,
			WeaponHash.MicroSMG,
			WeaponHash.MiniSMG,
			WeaponHash.Molotov,
			WeaponHash.Nightstick,
			WeaponHash.Pistol,
			WeaponHash.Pistol50,
			WeaponHash.PistolMk2,
			WeaponHash.PipeBomb,
			WeaponHash.ProximityMine,
			WeaponHash.Revolver,
			WeaponHash.SawnOffShotgun,
			WeaponHash.SMGMk2,
			WeaponHash.SmokeGrenade,
			WeaponHash.Snowball,
			WeaponHash.SNSPistol,
			WeaponHash.StunGun,
			WeaponHash.StickyBomb,
			WeaponHash.SwitchBlade,
			WeaponHash.VintagePistol,
			WeaponHash.Wrench
		};

		private static readonly List<WeaponHash> CivilianVehicleConceables = new List<WeaponHash> {
			WeaponHash.APPistol,
			WeaponHash.Ball,
			WeaponHash.BattleAxe,
			WeaponHash.Bottle,
			WeaponHash.BZGas,
			WeaponHash.CombatPistol,
			WeaponHash.CompactRifle,
			WeaponHash.Crowbar,
			WeaponHash.Dagger,
			WeaponHash.Flare,
			WeaponHash.FlareGun,
			WeaponHash.Flashlight,
			WeaponHash.Grenade,
			WeaponHash.Hammer,
			WeaponHash.Hatchet,
			WeaponHash.HeavyPistol,
			WeaponHash.Knife,
			WeaponHash.KnuckleDuster,
			WeaponHash.Machete,
			WeaponHash.MachinePistol,
			WeaponHash.MarksmanPistol,
			WeaponHash.MicroSMG,
			WeaponHash.MiniSMG,
			WeaponHash.Molotov,
			WeaponHash.Nightstick,
			WeaponHash.Pistol,
			WeaponHash.Pistol50,
			WeaponHash.PistolMk2,
			WeaponHash.PipeBomb,
			WeaponHash.ProximityMine,
			WeaponHash.Revolver,
			WeaponHash.SawnOffShotgun,
			WeaponHash.SMGMk2,
			WeaponHash.SmokeGrenade,
			WeaponHash.Snowball,
			WeaponHash.SNSPistol,
			WeaponHash.StunGun,
			WeaponHash.StickyBomb,
			WeaponHash.SwitchBlade,
			WeaponHash.VintagePistol,
			WeaponHash.Wrench
		};

		private static readonly List<WeaponHash> CivilianBikeConceables = new List<WeaponHash> {
			WeaponHash.BattleAxe,
			WeaponHash.CompactGrenadeLauncher,
			WeaponHash.Crowbar,
			WeaponHash.DoubleBarrelShotgun,
			WeaponHash.Flashlight,
			WeaponHash.Hammer,
			WeaponHash.Hatchet,
			WeaponHash.Machete,
			WeaponHash.Nightstick,
			WeaponHash.SawnOffShotgun,
			WeaponHash.SweeperShotgun,
			WeaponHash.Wrench
		};

		public static readonly Dictionary<WeaponHash, WeaponPropModel> WeaponsStowedOnBack =
			new Dictionary<WeaponHash, WeaponPropModel> {
				{
					WeaponHash.AdvancedRifle, new WeaponPropModel( "w_ar_advancedrifle", new Vector3( -0.05f, -0.13f, -0.03f ), 145f )
				},
				{WeaponHash.AssaultRifle, new WeaponPropModel( "w_ar_assaultrifle", new Vector3( 0.055f, -0.13f, 0.06f ), 145f )}, {
					WeaponHash.AssaultRifleMk2,
					new WeaponPropModel( "w_ar_assaultriflemk2", new Vector3( 0.075f, -0.13f, 0.06f ), 145f )
				}, {
					WeaponHash.AssaultShotgun, new WeaponPropModel( "w_sg_assaultshotgun", new Vector3( 0.125f, -0.13f, 0.05f ), 145f )
				},
				{WeaponHash.AssaultSMG, new WeaponPropModel( "w_sb_assaultsmg", new Vector3( 0.010f, -0.13f, -0.05f ), 145f )}, {
					WeaponHash.BullpupRifle, new WeaponPropModel( "w_ar_bullpuprifle", new Vector3( 0.055f, -0.13f, 0.025f ), 145f )
				}, {
					WeaponHash.BullpupShotgun, new WeaponPropModel( "w_sg_bullpupshotgun", new Vector3( 0.100f, -0.13f, 0.03f ), 145f )
				},
				{WeaponHash.CarbineRifle, new WeaponPropModel( "w_ar_carbinerifle", new Vector3( 0.075f, -0.13f, 0.01f ), 145f )}, {
					WeaponHash.CarbineRifleMk2,
					new WeaponPropModel( "w_ar_carbineriflemk2", new Vector3( 0.075f, -0.13f, 0.01f ), 145f )
				},
				{WeaponHash.CombatMG, new WeaponPropModel( "w_mg_combatmg", new Vector3( 0.095f, -0.13f, 0.06f ), 145f )},
				{WeaponHash.CombatMGMk2, new WeaponPropModel( "w_mg_combatmgmk2", new Vector3( 0.095f, -0.13f, 0.06f ), 145f )},
				{WeaponHash.CombatPDW, new WeaponPropModel( "w_sb_pdw", new Vector3( 0.110f, -0.13f, 0.05f ), 145f )}, {
					WeaponHash.DoubleBarrelShotgun,
					new WeaponPropModel( "w_sg_doublebarrel", new Vector3( -0.15f, -0.125f, 0.08f ), 145f )
				},
				{WeaponHash.Firework, new WeaponPropModel( "w_lr_firework", new Vector3( 0.250f, -0.13f, 0.1f ), 325f )}, {
					WeaponHash.GrenadeLauncher,
					new WeaponPropModel( "w_lr_grenadelauncher", new Vector3( 0.075f, -0.13f, 0.05f ), 145f )
				}, {
					WeaponHash.GrenadeLauncherSmoke,
					new WeaponPropModel( "w_lr_grenadelauncher", new Vector3( 0.075f, -0.13f, 0.05f ), 145f )
				},
				{WeaponHash.Gusenberg, new WeaponPropModel( "w_sb_gusenberg", new Vector3( 0.065f, -0.13f, -0.03f ), 145f )},
				{WeaponHash.HeavyShotgun, new WeaponPropModel( "w_sg_heavyshotgun", new Vector3( 0.075f, -0.13f, 0.05f ), 145f )},
				{WeaponHash.HeavySniper, new WeaponPropModel( "w_sr_heavysniper", new Vector3( 0.175f, -0.13f, 0.13f ), 150f )}, {
					WeaponHash.HeavySniperMk2, new WeaponPropModel( "w_sr_heavysnipermk2", new Vector3( 0.100f, -0.13f, 0.04f ), 150f )
				},
				{WeaponHash.HomingLauncher, new WeaponPropModel( "w_lr_homing", new Vector3( 0.250f, -0.13f, 0.1f ), 325f )},
				{WeaponHash.MarksmanRifle, new WeaponPropModel( "w_sr_marksmanrifle", new Vector3( 0.075f, -0.13f, 0.03f ), 145f )},
				{WeaponHash.MG, new WeaponPropModel( "w_mg_mg", new Vector3( 0.125f, -0.13f, 0.05f ), 145f )},
				{WeaponHash.Musket, new WeaponPropModel( "w_ar_musket", new Vector3( 0.195f, -0.13f, 0.1f ), 145f )},
				{WeaponHash.PumpShotgun, new WeaponPropModel( "w_sg_pumpshotgun", new Vector3( 0.095f, -0.13f, 0.05f ), 145f )},
				{WeaponHash.RPG, new WeaponPropModel( "w_lr_rpg", new Vector3( 0.250f, -0.13f, 0.1f ), 325f )},
				{WeaponHash.SMG, new WeaponPropModel( "w_sb_smg", new Vector3( 0.095f, -0.13f, 0.05f ), 145f )},
				{WeaponHash.SniperRifle, new WeaponPropModel( "w_sr_sniperrifle", new Vector3( 0.195f, -0.13f, 0.1f ), 145f )}, {
					WeaponHash.SpecialCarbine,
					new WeaponPropModel( "w_ar_specialcarbine", new Vector3( 0.055f, -0.13f, -0.01f ), 145f )
				},
				{WeaponHash.SweeperShotgun, new WeaponPropModel( "w_sg_sweeper", new Vector3( -0.25f, -0.145f, -0.01f ), 145f )}
			};

		public static void Init() {
			Client.ActiveInstance.RegisterTickHandler( OnTick );
			Client.ActiveInstance.RegisterTickHandler( CancelActionMode );
			Client.ActiveInstance.RegisterTickHandler( CopHolster );

			Client.ActiveInstance.PointEventHandlers["Weapons.ManipulationEvent"] = ReceivedWeaponEvent;
			Client.ActiveInstance.RegisterEventHandler( "Weapons.RemoveFromBack",
				new Action<bool>( ReceivedRemoveFromBackEvent ) );
		}

		private static void ReceivedRemoveFromBackEvent( bool isBackUnarmed ) {
			DeleteWeaponOnBack( isBackUnarmed );
		}

		/// <summary>
		///     Holster On Tick
		/// </summary>
		private static async Task OnTick() {
			try {
				if( !Session.HasJoinedRP ) {
					await BaseScript.Delay( 4096 );
					return;
				}

				if( !_currentlyUnholstering ) {
					CurrentWeapon = Game.PlayerPed.Weapons.Current.Hash;

					bool weaponHasChanged = CurrentWeapon != _previousWeapon &&
					                        CurrentWeapon != BuzzardHeliWeapHash &&
					                        CurrentWeapon != (WeaponHash)966099553; /* WeaponHash for WEAPON_OBJECT; not in enum */

					if( Cache.IsPlayerInVehicle || Game.PlayerPed.IsGettingIntoAVehicle ) {
						if( !_isInVehicle ) {
							if( IsLawEnforcementOfficer() ) {
								WeaponStash.HandleLeoEnterVehicleEvent( CurrentWeapon );
								_previousWeapon = CurrentWeapon;
								_isInVehicle = true;
								return;
							}

							await HandleEnterVehicleHolsterEvent();
						}

						if( weaponHasChanged && (IsLawEnforcementOfficer() || Game.IsControlJustPressed( 2, Control.SelectWeapon ) ||
						                         Game.IsControlPressed( 2, Control.SelectWeapon ) ||
						                         Game.IsControlJustReleased( 2, Control.SelectWeapon )) ) {
							SendWeaponEvent( "manipulate" );
							ToggleWeaponSwitchEvent( true );
							await HandleVehicleOrSwimWeaponSwitchEvent();
							ToggleWeaponSwitchEvent( false );
							_previousWeapon = CurrentWeapon;
						}

						_isInVehicle = true;
						await Task.FromResult( 0 );
						return;
					}

					//Check if previously in vehicle, now getting out
					if( _isInVehicle ) {
						//Getting out of vehicle
						if( _unconcealableWeaponInHand != WeaponHash.Unarmed ) {
							Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, _unconcealableWeaponInHand, true );
							_previousWeapon = _unconcealableWeaponInHand;
						}

						_unconcealableWeaponInHand = WeaponHash.Unarmed;
						_isInVehicle = false;
						return;
					}

					if( weaponHasChanged ) {
						SendWeaponEvent( "manipulate" );

						if( _activeUnstoreableDrop ) {
							_activeUnstoreableDrop = false;
							_activeWeaponDrop = false;
						}
						else if( _activeWeaponQuickStash ) {
							_previousWeapon = CurrentWeapon;
							_activeWeaponQuickStash = false;
							return;
						}
						else if( _activeQuickDraw || _activeWeaponDrop || _activeWeaponSeizure || _activeWeaponStorage ) {
							_previousWeapon = _activeQuickDraw ? LeoHolsteredWeapon : WeaponHash.Unarmed;
							if( _activeWeaponSeizure ) DeleteWeaponOnBack( true );
							if( !_activeQuickDraw ) {
								Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Unarmed, true );
							}

							_activeQuickDraw = false;
							_activeWeaponDrop = false;
							_activeWeaponSeizure = false;
							_activeWeaponStorage = false;

							return;
						}

						//Unholstering weapon
						ToggleWeaponSwitchEvent( true );
						if( Game.PlayerPed.IsSwimming ) {
							await HandleVehicleOrSwimWeaponSwitchEvent();
						}
						else {
							//We're switching weapons while on foot
							if( _previousWeapon == WeaponHash.Unarmed ) {
								//Previous weapon = unarmed
								if( IsWeaponConceable( CurrentWeapon ) )
									if( CurrentWeapon != WeaponHash.SwitchBlade  && CurrentWeapon != WeaponHash.Snowball ) {
										Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Unarmed, true ); //Dis a must
										await RetrieveConcealableWeapon();
									}
									else {
										await BaseScript.Delay( 1000 );
										Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );
									}
								else if( _weaponOnBack == CurrentWeapon ) {
									await RetrieveWeaponFromBack();
								}
							}
							else if( IsWeaponConceable( _previousWeapon ) ) {
								//Previous weapon = stowable
								if( CurrentWeapon == WeaponHash.Unarmed ) {
									await StoreConcealableWeapon();
								}
								else if( IsWeaponConceable( CurrentWeapon ) ) {
									await ExchangeConcealableWeapon();
								} //Stowable Armed -> Stowable weapon
								else if( CurrentWeapon == _weaponOnBack ) {
									await StoreConcealableWeapon();
									await RetrieveWeaponFromBack();
								} //Stowable Armed -> Weapon on back
								else {
									await StoreConcealableWeapon();
								} //Stowable Armed -> non-stowable weapon OR weapon must be stored on back but currently isn't
							}
							else if( CanStoreWeaponOnBack( _previousWeapon, out WeaponPropModel prevWeaponPropModel ) &&
							         prevWeaponPropModel != null ) {
								//Previous weapon = back weapon
								if( CurrentWeapon == WeaponHash.Unarmed ) {
									//Switching to concealable weapon
									if( _weaponOnBack == WeaponHash.Unarmed ) await StoreWeaponOnBack( _previousWeapon, prevWeaponPropModel );
									else await DropWeapon();
								}
								else if( IsWeaponConceable( CurrentWeapon ) ) {
									//Switching to concealable weapon
									if( _weaponOnBack == WeaponHash.Unarmed ) await StoreWeaponOnBack( _previousWeapon, prevWeaponPropModel );
									else await DropWeapon();
									await RetrieveConcealableWeapon();
								} //Back Weapon Armed -> Stowable Weapon
								else if( _weaponOnBack == CurrentWeapon ) {
									//Switching to weapon currently on back
									await ExchangeWeaponOnBack( CurrentWeapon, _previousWeapon, prevWeaponPropModel );
								} //Back Weapon Armed -> Weapon on back
								else {
									//Switching to non-stowable weapon OR weapon that must be stowed on back
									if( _weaponOnBack == WeaponHash.Unarmed ) await StoreWeaponOnBack( _previousWeapon, prevWeaponPropModel );
									else await DropWeapon();
									Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );
								} //Back Weapon Armed -> Non-stowable weapon || weapon not on back, that must be stowed on back
							}
							else {
								//Previous weapon = non-stowable, need to drop
								await DropWeapon();

								if( IsWeaponConceable( CurrentWeapon ) ) await RetrieveConcealableWeapon();
								else if( _weaponOnBack == CurrentWeapon ) await RetrieveWeaponFromBack();
							}
						}

						ToggleWeaponSwitchEvent( false );
						_previousWeapon = CurrentWeapon;
					}

					if( !_hasRaisedGun && Game.Player.IsAiming ) {
						_hasRaisedGun = true;
						SendWeaponEvent( "raise" );
					}
					else if( _hasRaisedGun && !Game.Player.IsAiming ) {
						await BaseScript.Delay( 100 );
						if( Game.Player.IsAiming ) return;
						_hasRaisedGun = false;
						SendWeaponEvent( "lower" );
					}
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}

			await Task.FromResult( 0 );
		}

		private static async Task CancelActionMode() {
			try {
				if( !Session.HasJoinedRP ) {
					await BaseScript.Delay( 3025 );
					return;
				}

				if( API.IsPedUsingActionMode( Cache.PlayerHandle ) ) {
					API.SetPedUsingActionMode( Cache.PlayerHandle, false, -1, "0" );
				}

				await BaseScript.Delay( 250 );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Cop Holster Tick
		/// </summary>
		private static async Task CopHolster() {
			try {
				if( !Session.HasJoinedRP ) {
					await BaseScript.Delay( 8192 );
					return;
				}

				if( !IsLawEnforcementOfficer() ) {
					await BaseScript.Delay( 16384 );
					return;
				}

				if( Stance.IsPlayerProne() || Cache.IsPlayerInVehicle || Game.PlayerPed.IsGettingIntoAVehicle ||
				    Game.PlayerPed.IsSwimming ) {
					await BaseScript.Delay( 500 );
					return;
				}

				const string quickDrawAimDict = "reaction@intimidation@cop@unarmed";
				if( CurrentWeapon == WeaponHash.Unarmed && !ControlHelper.IsControlPressed( Control.Sprint ) &&
				    !Game.Player.IsTargettingAnything ) {
					if( ControlHelper.IsControlPressed( QuickDrawControl ) && !_pistolUnholstered ) {
						if( !API.IsEntityPlayingAnim( Cache.PlayerHandle, quickDrawAimDict, "intro", 3 ) )
							await Game.PlayerPed.Task.PlayAnimation( quickDrawAimDict, "intro", 8f, -8f, -1,
								(AnimationFlags)50, 0 );

						ToggleQuickDraw( true );
					}
					else if( ControlHelper.IsControlJustReleased( QuickDrawControl ) && !_pistolUnholstered ) {
						Game.PlayerPed.Task.ClearAnimation( quickDrawAimDict, "intro" );
						ToggleQuickDraw( false );
					}
					else {
						Game.PlayerPed.Task.ClearAnimation( quickDrawAimDict, "intro" );
						_pistolUnholstered = false;
						_drawReady = false;
					}

					if( ControlHelper.IsControlJustPressed( Control.VehicleDriveLook ) && _drawReady ) {
						await HandleQuickDraw();
						ToggleQuickDraw( false );
					}
				}
				else {
					if( !API.IsEntityPlayingAnim( Cache.PlayerHandle, quickDrawAimDict, "intro", 3 ) )
						Game.PlayerPed.Task.ClearAnimation( quickDrawAimDict, "intro" );
					ToggleQuickDraw( false );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}

			await Task.FromResult( 0 );
		}

		/// <summary>
		///     Send server weapon event
		/// </summary>
		/// <param name="weaponEvent"></param>
		private static void SendWeaponEvent( string weaponEvent ) {
			const float weaponEventAoe = 25f;
			PointEvent pointEvent = new PointEvent( "Weapons.ManipulationEvent", Cache.PlayerPos.ToArray(),
				weaponEventAoe,
				weaponEvent, Game.Player.ServerId, false );
			BaseScript.TriggerServerEvent( "TriggerEventNearPoint", JsonConvert.SerializeObject( pointEvent ) );
		}

		/// <summary>
		///     Receive weapon event
		/// </summary>
		/// <param name="pointEvent"></param>
		private static Task ReceivedWeaponEvent( PointEvent pointEvent ) {
			try {
				if( Function.Call<bool>( Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY_IN_FRONT, Cache.PlayerHandle,
					PlayerList[pointEvent.SourceServerId].Character.Handle ) )
					if( pointEvent.SerializedArguments == "lower"
					) // May serialize these later from an enum or something just to make everything look nicer, but this is not that bad
					{
					}
					else if( pointEvent.SerializedArguments == "raise" ) {
					}
					else if( pointEvent.SerializedArguments == "manipulate" ) {
					}
			}
			catch( Exception ex ) {
				Log.Error( $"ReceivedWeaponEvent: {ex.Message}" );
			}

			return Task.FromResult( 0 );
		}

		/// <summary>
		///     Toggle a weapon switch event: disable controls while unholstering
		/// </summary>
		/// <param name="isSwitchingWeapons"></param>
		private static void ToggleWeaponSwitchEvent( bool isSwitchingWeapons ) {
			try {
				//Unholstering weapon
				Game.PlayerPed.CanSwitchWeapons = !isSwitchingWeapons;
				_currentlyUnholstering = isSwitchingWeapons;
				DisableControls();
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Disables the player from shooting until the gun is unholster and ready
		/// </summary>
		private static async void DisableControls() {
			try {
				if( !_currentlyUnholstering || _activeQuickDraw ) return;

				while( _currentlyUnholstering ) {
					API.DisableControlAction( 2, (int)Control.Attack, true );
					API.DisableControlAction( 2, (int)Control.Attack2, true );
					await BaseScript.Delay( 0 );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle civilians changing weapons in vehicles, allow them to exit car with weapon they have equipped in vehicle
		/// </summary>
		private static async Task HandleEnterVehicleHolsterEvent() {
			try {
				if( IsWeaponConceable( CurrentWeapon ) || CurrentWeapon == WeaponHash.DoubleBarrelShotgun ) {
					var vehicle = Game.PlayerPed.VehicleTryingToEnter ?? Cache.CurrentVehicle;
					await BaseScript.Delay( 1200 );
					if( vehicle != null ) {
						const string motorCycleClass = "VEH_CLASS_8";
						var isBike = vehicle.ClassDisplayName == motorCycleClass;
						if( isBike ) {
							if( CivilianVehicleConceables.Contains( CurrentWeapon ) ||
							    CivilianBikeConceables.Contains( CurrentWeapon ) ) {
								Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );
								return;
							}
						}
						else if( CivilianVehicleConceables.Contains( CurrentWeapon ) ) {
							Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );
							return;
						}
					}
				}

				if( CurrentWeapon != WeaponHash.Unarmed ) {
					_unconcealableWeaponInHand = CurrentWeapon;
				}

				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Unarmed, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Ignore holster animations when driving or swimming
		/// </summary>
		private static async Task HandleVehicleOrSwimWeaponSwitchEvent() {
			try {
				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Unarmed, true );
				await BaseScript.Delay( VehicleWeaponSwitchDelay );
				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Play the animation matching the weapon being equipped
		/// </summary>
		/// <param name="isUnholstering"></param>
		private static async Task HandleLeoHolster( bool isUnholstering ) {
			try {
				//Pull holster weapon from right hip, stun gun from front hip
				if( CurrentWeapon == LeoHolsteredWeapon && isUnholstering ||
				    _previousWeapon == LeoHolsteredWeapon && !isUnholstering ) {
					string animName = "outro";
					if( isUnholstering ) animName = "intro";

					await Game.PlayerPed.Task.PlayAnimation( "reaction@intimidation@cop@unarmed", animName, 3f, -3f, 500,
						(AnimationFlags)48, 0 );

					await BaseScript.Delay( 500 );

					DrawLeoHolster( !isUnholstering );
				}
				else if( CurrentWeapon != WeaponHash.StunGun ) {
					const string leoUnholsterFromBackAnim = "-0";
					const string leoUnholsterFromHipAnim = "0";

					WeaponHash weaponToCheckForHolsterPosition = _previousWeapon;
					if( isUnholstering ) weaponToCheckForHolsterPosition = CurrentWeapon;

					string leoUnholsterAnim = leoUnholsterFromBackAnim;
					if( LeoSideHipUtilityBelt.Contains( weaponToCheckForHolsterPosition ) ) leoUnholsterAnim = leoUnholsterFromHipAnim;

					await Game.PlayerPed.Task.PlayAnimation( "combat@reaction_aim@pistol", leoUnholsterAnim, 8f, -8f, 500,
						(AnimationFlags)48, 0 );
					await BaseScript.Delay( 300 );
				}

				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Store Concealable Weapon
		/// </summary>
		private static async Task StoreConcealableWeapon() {
			try {
				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, _previousWeapon, true ); //Dis a must

				if( _previousWeapon == WeaponHash.SwitchBlade || _previousWeapon == WeaponHash.Snowball ) {
					await BaseScript.Delay( 1000 );
				}
				else {
					if( IsLawEnforcementOfficer() ) {
						//Put stun gun back in front utility belt
						if( _previousWeapon != WeaponHash.StunGun ) await HandleLeoHolster( false );
					}
					else {
						await PlayHolsterAnim( HolsterActions.Holster );

						if( !StoreAnim.TryGetValue( CurrentHolster, out var currentHolster ) ) await BaseScript.Delay(850);
						else await BaseScript.Delay( currentHolster.NoFireDelay );
					}
				}

				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Play the animation matching the weapon being equipped
		/// </summary>
		private static async Task RetrieveConcealableWeapon() {
			try {
				if( IsLawEnforcementOfficer() ) {
					await HandleLeoHolster( true );
				}
				else {
					await PlayHolsterAnim( HolsterActions.Unholster );

					if( CurrentHolster == HolsterLocations.None ) await BaseScript.Delay( 625 );
					else await BaseScript.Delay( 500 );

					Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );

					if( !RetrieveAnim.TryGetValue( CurrentHolster, out var currentHolster ) ) return;

					await BaseScript.Delay( currentHolster.NoFireDelay );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Exchange the concealable weapon
		/// </summary>
		private static async Task ExchangeConcealableWeapon() {
			try {
				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, _previousWeapon, true ); //Dis a must

				if( _previousWeapon == WeaponHash.SwitchBlade || _previousWeapon == WeaponHash.Snowball) {
					await BaseScript.Delay( 1000 );
					Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Unarmed, true );
				}

				if( IsLawEnforcementOfficer() ) {
					if( LeoSideHipUtilityBelt.Contains( _previousWeapon ) ||
					    CurrentWeapon == LeoHolsteredWeapon && _previousWeapon != WeaponHash.Unarmed ||
					    _previousWeapon == LeoHolsteredWeapon ) {
						await HandleLeoHolster( false );
						await BaseScript.Delay( 250 );
					}

					await HandleLeoHolster( true );
				}
				else {
					await PlayHolsterAnim( HolsterActions.Unholster );
					await BaseScript.Delay( 625 );
				}

				//Stow old weapon in back wasteband
				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Unarmed, true );

				if( CurrentWeapon == WeaponHash.SwitchBlade ) {
					await BaseScript.Delay( 750 );
					Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.SwitchBlade, false );
					await BaseScript.Delay( 1000 );
				}
				else if( CurrentWeapon == WeaponHash.Snowball ) {
					Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Snowball, false );
				}
				else {
					Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Play the current holster animation
		/// </summary>
		/// <param name="action">holster or unholster action</param>
		private static async Task PlayHolsterAnim(HolsterActions action) {
			try {
				HolsterModel currentHolster;

				if( action == HolsterActions.Holster ) {
					if( !StoreAnim.TryGetValue( CurrentHolster, out currentHolster ) ) return;
				}
				else {
					if( !RetrieveAnim.TryGetValue( CurrentHolster, out currentHolster ) ) return;
				}

				await Game.PlayerPed.Task.PlayAnimation( currentHolster.HolsterDictionary, currentHolster.HolsterAnim, 8f, -8f, currentHolster.Duration,
					(AnimationFlags)48, currentHolster.PlaybackRate );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}
	

		/// <summary>
		///     Store current weapon in hands on the player back
		/// </summary>
		/// <param name="weaponHash">hash of weapon to store</param>
		/// <param name="weaponPropModel">name of model to store on back and offset position and rotation</param>
		private static async Task StoreWeaponOnBack( WeaponHash weaponHash, WeaponPropModel weaponPropModel ) {
			try {
				await Game.PlayerPed.Task.PlayAnimation( "mp_parachute_outro@male@lose", "lose", 8f, -8f, 500,
					(AnimationFlags)48, 0.2f );

				await BaseScript.Delay( 100 );

				DeleteWeaponOnBack( true );

				await BaseScript.Delay( 200 );

				var weaponPropHandle = await CreateWeaponProp( weaponHash );
				AttachWeaponPropToBack( weaponPropHandle, weaponHash, weaponPropModel );

				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Unarmed, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Retrieve weapon currently stored on back
		/// </summary>
		private static async Task RetrieveWeaponFromBack() {
			try {
				await Game.PlayerPed.Task.PlayAnimation( "mp_parachute_outro@male@lose", "lose", 8f, -8f, 1250,
					(AnimationFlags)48, 0.2f );

				await BaseScript.Delay( 100 );

				DeleteWeaponOnBack( true );

				while( API.IsEntityPlayingAnim( Cache.PlayerHandle, "mp_parachute_outro@male@lose", "lose", 3 ) )
					await BaseScript.Delay( 25 );

				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, CurrentWeapon, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Exchange current weapon in hands with the one on the player back
		/// </summary>
		/// <param name="currentWeapon"></param>
		/// <param name="previousWeapon"></param>
		/// <param name="prevWeaponPropModel">name of model to store on back and offset position and rotation</param>
		private static async Task ExchangeWeaponOnBack( WeaponHash currentWeapon, WeaponHash previousWeapon,
			WeaponPropModel prevWeaponPropModel ) {
			try {
				await Game.PlayerPed.Task.PlayAnimation( "mp_parachute_outro@male@lose", "lose", 8f, -8f, 1250,
					(AnimationFlags)48, 0.2f );

				await BaseScript.Delay( 100 );

				DeleteWeaponOnBack( true );
				//Retrieving current weapon
				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, currentWeapon, true );

				await BaseScript.Delay( 200 );
				//Add old weapon to back
				int weaponPropHandle = await CreateWeaponProp( previousWeapon );
				AttachWeaponPropToBack( weaponPropHandle, previousWeapon, prevWeaponPropModel );

				const int exchangeBackWeapondelay = 1750;
				await BaseScript.Delay( exchangeBackWeapondelay );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Delete weapon prop from players back
		/// </summary>
		public static void DeleteWeaponOnBack( bool isBackUnarmed ) {
			try {
				if( _weaponOnBackHandle <= 0 ) return;
				Log.Verbose( $"Deleting weaponPropHandle={_weaponOnBackHandle}" );
				Entity weaponToDelete = Entity.FromHandle( _weaponOnBackHandle );
				if( weaponToDelete == null || !weaponToDelete.Exists() ) {
					Log.Error( $"WeaponUnholsterError: WeaponOnBackHandle={_weaponOnBackHandle}" );
					_weaponOnBack = WeaponHash.Unarmed;
					_weaponOnBackHandle = 0;
					return;
				}
				weaponToDelete.Delete();
				weaponToDelete.MarkAsNoLongerNeeded();

				_weaponOnBackHandle = 0;
				//Stored previous weapon on back
				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Unarmed, true );
				if( isBackUnarmed ) _weaponOnBack = WeaponHash.Unarmed;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Drop the current weapon in hands
		/// </summary>
		public static async Task DropWeapon() {
			try {
				_activeUnstoreableDrop = true;
				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, _previousWeapon, true );
				DropItem.HandleDropWeaponWrapper();
				Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, WeaponHash.Unarmed, true );
				await BaseScript.Delay( 250 );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Drop the current weapon in hands
		/// </summary>
		/// <param name="weaponHash">hash of weapon to store</param>
		private static async Task<int> CreateWeaponProp( WeaponHash weaponHash ) {
			try {
				if( !WeaponsStowedOnBack.TryGetValue( weaponHash, out WeaponPropModel weaponPropModel ) ) return -1;
				if( weaponPropModel == null ) return -1;

				Model model = new Model( weaponPropModel.PropName );
				await model.Request( 250 );

				if( !model.IsInCdImage || !model.IsValid ) return -1;

				while( !model.IsLoaded ) await BaseScript.Delay( 10 );

				Vector3 offsetPosition = weaponPropModel.OffsetPosition;
				Vector3 attachPosition = API.GetPedBoneCoords( Cache.PlayerHandle, 24818, offsetPosition.X, offsetPosition.Y,
					offsetPosition.Z );

				Prop prop = await World.CreateProp( model, attachPosition, new Vector3( 0, weaponPropModel.StowedRotation, 0 ),
					false, false );
				model.MarkAsNoLongerNeeded();
				return prop.Handle;
			}
			catch( Exception ex ) {
				Log.Error( ex );
				return -1;
			}
		}

		/// <summary>
		///     Attach weapon prop to back of ped
		/// </summary>
		/// <param name="weaponPropHandle"></param>
		/// <param name="weaponHash">hash of weapon to attach to back</param>
		/// <param name="weaponPropModel"></param>
		private static void AttachWeaponPropToBack( int weaponPropHandle, WeaponHash weaponHash,
			WeaponPropModel weaponPropModel ) {
			try {
				Entity entity = Entity.FromHandle( weaponPropHandle );

				if( entity == null || !entity.Exists() ) {
					Log.Error( "Error: Could not attach prop to back, entity doesn't exist." );
					return;
				}
				Log.Verbose($"Attaching weaponPropHandle={weaponPropHandle}");
				_weaponOnBackHandle = weaponPropHandle;
				_weaponOnBack = weaponHash;

				int multiplier = Game.GameTime % 2 == 1 ? 1 : -1;
				float rotationOfffset = multiplier * (Game.GameTime % 501 / 100f);
				API.AttachEntityToEntity( weaponPropHandle, Cache.PlayerHandle,
					API.GetPedBoneIndex( Cache.PlayerHandle, 24818 ), weaponPropModel.OffsetPosition.X,
					weaponPropModel.OffsetPosition.Y, weaponPropModel.OffsetPosition.Z, 0,
					weaponPropModel.StowedRotation + rotationOfffset, 0,
					true,
					true, false, true, 1, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Check if current weapon is conceable
		/// </summary>
		/// <param name="weapon"></param>
		public static bool IsWeaponConceable( WeaponHash weapon ) {
			try {
				return CivilianConcealables.Contains( weapon );
			}
			catch( Exception ex ) {
				Log.Error( ex );
				return false;
			}
		}

		/// <summary>
		///     Check if player can store current weapon on back
		/// </summary>
		/// <param name="weapon"></param>
		/// <param name="weaponPropModel"></param>
		public static bool CanStoreWeaponOnBack( WeaponHash weapon, out WeaponPropModel weaponPropModel ) {
			try {
				return WeaponsStowedOnBack.TryGetValue( weapon, out weaponPropModel );
			}
			catch( Exception ex ) {
				Log.Error( ex );
				weaponPropModel = new WeaponPropModel();
				return false;
			}
		}

		/// <summary>
		///     Check if player is on duty as a LEO
		/// </summary>
		private static bool IsLawEnforcementOfficer() {
			try {
				return DutyManager.IsPlayerOnDuty( CurrentPlayer.NetID, Duty.Police );
			}
			catch( Exception ex ) {
				Log.Error( ex );
				return false;
			}
		}


		/// <summary>
		///     Draw weapon in LEO holster
		/// </summary>
		public static void DrawLeoHolster( bool gunInHolster ) {
			try {
				//Don't change components on the freemode models
				var entityModel = (uint)API.GetEntityModel( Cache.PlayerHandle );
				if( entityModel == (uint)PedHash.FreemodeFemale01 || entityModel == (uint)PedHash.FreemodeMale01 )
					return;

				int gunDrawId = gunInHolster ? 1 : 0;
				API.SetPedComponentVariation( Cache.PlayerHandle, 9, gunDrawId, 0, 0 );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle LEO pistol quickdraw
		/// </summary>
		private static async Task HandleQuickDraw() {
			try {
				const string quickDrawAimDict = "reaction@intimidation@cop@unarmed";

				bool hasPistolInSlot = API.GetPedWeapontypeInSlot( Cache.PlayerHandle, unchecked((uint)-1155528315) ) != 0;
				if( hasPistolInSlot ) {
					await Game.PlayerPed.Task.PlayAnimation( quickDrawAimDict, "intro", 3f, -3f, 500,
						(AnimationFlags)48, 0 );

					while( API.IsEntityPlayingAnim( Cache.PlayerHandle, quickDrawAimDict, "intro", 3 ) )
						await BaseScript.Delay( 10 );

					await BaseScript.Delay( 125 );

					Game.PlayerPed.Task.ClearAnimation( quickDrawAimDict, "intro" );

					Function.Call( Hash.SET_CURRENT_PED_WEAPON, Cache.PlayerHandle, LeoHolsteredWeapon, true );

					if( Game.PlayerPed.Weapons.Current == LeoHolsteredWeapon ) {
						_pistolUnholstered = true;
						_activeQuickDraw = true;
						DrawLeoHolster( false );
					}
				}
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Toggle quick draw status
		/// </summary>
		/// <param name="isActive">is holster quick draw active</param>
		private static void ToggleQuickDraw( bool isActive ) {
			try {
				Game.PlayerPed.CanSwitchWeapons = !isActive;
				_drawReady = isActive;
				//I don't understand why this works as it isn't done per frame
				//Shouldn't work but it does what its supposed to.  Using a while 
				//loop works as well but causes massive FPS drops.
				ToggleAttackControls( isActive );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Toggle attack controls
		/// </summary>
		/// <param name="areDisabled">are attack controls disabled</param>
		private static void ToggleAttackControls( bool areDisabled ) {
			try {
				API.DisableControlAction( 2, (int)Control.Attack, areDisabled );
				API.DisableControlAction( 2, (int)Control.Attack2, areDisabled );
				API.DisableControlAction( 2, (int)Control.VehicleDriveLook, areDisabled );
				API.DisableControlAction( 2, (int)Control.MeleeAttack1, areDisabled );
				API.DisableControlAction( 2, (int)Control.MeleeAttack2, areDisabled );
				API.DisableControlAction( 2, (int)Control.MeleeAttackAlternate, areDisabled );
				API.DisableControlAction( 2, (int)Control.MeleeAttackHeavy, areDisabled );
				API.DisableControlAction( 2, (int)Control.MeleeAttackLight, areDisabled );

				if( areDisabled ) return;

				API.EnableControlAction( 2, (int)Control.Attack, true );
				API.EnableControlAction( 2, (int)Control.Attack2, true );
				API.EnableControlAction( 2, (int)Control.VehicleDriveLook, true );
				API.EnableControlAction( 2, (int)Control.MeleeAttack1, true );
				API.EnableControlAction( 2, (int)Control.MeleeAttack2, true );
				API.EnableControlAction( 2, (int)Control.MeleeAttackAlternate, true );
				API.EnableControlAction( 2, (int)Control.MeleeAttackHeavy, true );
				API.EnableControlAction( 2, (int)Control.MeleeAttackLight, true );
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle weapon drop event to ignore holster animations
		/// </summary>
		public static void HandleWeaponDroppedEventWrapper() {
			try {
				_activeWeaponDrop = true;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle weapon drop event to ignore holster animations
		/// </summary>
		public static void HandleWeaponStorageEventWrapper() {
			try {
				_activeWeaponStorage = true;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle weapon seized event to ignore holster animations
		/// </summary>
		public static void HandleWeaponSeizedEventWrapper() {
			try {
				_activeWeaponSeizure = true;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		/// <summary>
		///     Handle weapon LEO stashed event 
		/// </summary>
		public static void HandleWeaponVehicleWeaponStashEventWrapper() {
			try {
				_activeWeaponQuickStash = true;
			}
			catch( Exception ex ) {
				Log.Error( ex );
			}
		}

		public class WeaponPropModel
		{
			public WeaponPropModel( string propName, Vector3 attachPosition, float stowedRotation ) {
				PropName = propName;
				StowedRotation = stowedRotation;
				OffsetPosition = attachPosition;
			}

			public WeaponPropModel() {
			}

			public string PropName { get; }
			public Vector3 OffsetPosition { get; }
			public float StowedRotation { get; }
		}

		public class HolsterModel
		{
			public string HolsterDictionary;
			public string HolsterAnim;
			public int Duration;
			public float PlaybackRate;
			public int NoFireDelay;

			public HolsterModel( string holsterDictionary, string holsterAnim, int duration, float playbackRate,
				int noFireDelay ) {
				HolsterDictionary = holsterDictionary;
				HolsterAnim = holsterAnim;
				Duration = duration;
				PlaybackRate = playbackRate;
				NoFireDelay = noFireDelay;
			}
		}

		public enum HolsterActions
		{
			Unholster,
			Holster
		}

		public enum HolsterLocations
		{
			None,
			Hip,
			Back
		}
	}
}