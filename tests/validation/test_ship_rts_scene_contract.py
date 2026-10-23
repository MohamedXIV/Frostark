"""Structural evidence for the tracked RTS demo scene, NOT a Unity runtime test."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCENE = ROOT / "Assets/FrostArk/Scenes/ShipRtsDemo.unity"
SCENE_META = ROOT / "Assets/FrostArk/Scenes/ShipRtsDemo.unity.meta"
SCRIPT_META = ROOT / "Assets/FrostArk/Unity/ShipRtsDemoRuntime.cs.meta"
SCRIPT = ROOT / "Assets/FrostArk/Unity/ShipRtsDemoRuntime.cs"
BUILD = ROOT / "ProjectSettings/EditorBuildSettings.asset"


def guid(text):
    match = re.search(r"(?m)^guid: ([0-9a-f]{32})$", text)
    if not match:
        raise AssertionError("Missing Unity GUID")
    return match.group(1)


class ShipRtsDemoSceneContractTests(unittest.TestCase):
    def test_scene_asset_links_the_actual_runtime_script_guid(self):
        scene = SCENE.read_text(encoding="utf-8")
        script = SCRIPT.read_text(encoding="utf-8")
        script_id = guid(SCRIPT_META.read_text(encoding="utf-8"))
        self.assertIn("%YAML 1.1", scene)
        self.assertIn("m_Script: {fileID: 11500000, guid: " + script_id + ", type: 3}", scene)
        self.assertIn("FrostArk.Unity.ShipRtsDemoRuntime", scene)
        self.assertIn("public sealed class ShipRtsDemoRuntime : MonoBehaviour", script)
        self.assertIn("private void Start()", script)
        self.assertIn("try { BuildDemo(); }", script)
        self.assertIn("Debug.LogException(ex, this)", script)

    def test_scene_is_registered_without_overwriting_other_launchers(self):
        build = BUILD.read_text(encoding="utf-8")
        scene_id = guid(SCENE_META.read_text(encoding="utf-8"))
        self.assertIn(
            "path: Assets/FrostArk/Scenes/ShipRtsDemo.unity\n    guid: " + scene_id,
            build,
        )
        for path in [
            "Assets/Scenes/SampleScene.unity",
            "Assets/FrostArk/Scenes/TacticalVerticalSlice.unity",
            "Assets/FrostArk/Scenes/ShipShoreTransitLauncher.unity",
        ]:
            self.assertIn("path: " + path, build)
        self.assertTrue(
            (ROOT / "Assets/FrostArk/Tests/Editor/ShipRtsDemoSceneEditModeTests.cs").is_file()
        )
        self.assertTrue(
            (ROOT / "Assets/FrostArk/Tests/PlayMode/ShipRtsDemoScenePlayModeTests.cs").is_file()
        )


    def test_compile_blockers_from_first_real_unity_smoke_are_fixed(self):
        """Static regression only; Unity 6000.6.0f1 must recompile separately."""
        dispatch = (
            ROOT / "Assets/FrostArk/Tests/Editor/CrewTheaterMoveDispatchEditModeTests.cs"
        ).read_text(encoding="utf-8")
        imported = (
            ROOT / "Assets/FrostArk/Tests/Editor/ShipRtsDemoSceneEditModeTests.cs"
        ).read_text(encoding="utf-8")
        self.assertRegex(
            dispatch,
            r'private static readonly EntityId Engine\s*=\s*EntityId\.Parse\("room:engine"\)',
        )
        self.assertIn("using EntityId = FrostArk.Core.World.EntityId;", imported)

    def test_scene_imgui_overlay_consumes_world_pointer_input(self):
        runtime = SCRIPT.read_text(encoding="utf-8")
        tactical = (ROOT / "Assets/FrostArk/Unity/TacticalCommandController.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("_commander.SetPointerOverlayBlocker(IsPointerOverHud)", runtime)
        self.assertIn("Screen.height - screenPoint.y", runtime)
        self.assertIn("bool pointerOverUi = IsPointerBlocked(point)", tactical)
        self.assertIn("(_isPointerOverSceneOverlay?.Invoke(screenPoint) ?? false)", tactical)


    def test_editmode_selection_visuals_are_bound_without_awake(self):
        """Protect the real Unity smoke failure: 2 selected but proxy rings off."""
        tactical = (ROOT / "Assets/FrostArk/Unity/TacticalCommandController.cs").read_text(
            encoding="utf-8"
        )
        tests = (
            ROOT / "Assets/FrostArk/Tests/Editor/ShipRtsPointerBridgeEditModeTests.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("private void EnsureSelectionBinding()", tactical)
        self.assertIn("_selectionController.SelectionChanged += OnSelectionChanged;", tactical)
        self.assertIn("public void RegisterShipActor(ShipCrewSelectionProxy actor)", tactical)
        self.assertIn(
            "public void EditModeSelectionNotificationsUpdateRegisteredShipVisualsWithoutAwake()",
            tests,
        )
        self.assertIn("scene.Commander.Selection.SelectMultiple(new[] { First, Second })", tests)


    def test_cooperative_movement_wiring_and_import_guids(self):
        """Static G0 wiring proof only; does not claim Unity G2/G3 PASS."""
        root = ROOT / "Assets/FrostArk/Unity"
        traffic = root / "ShipTrafficCoordinator.cs"
        traffic_meta = root / "ShipTrafficCoordinator.cs.meta"
        walker = (root / "ShipGridWalker.cs").read_text(encoding="utf-8")
        controller = (root / "TacticalCommandController.cs").read_text(encoding="utf-8")
        group = (root / "TacticalTheaterSelectionCommands.cs").read_text(
            encoding="utf-8"
        )
        spatial = (ROOT / "Assets/FrostArk/Core/Ship/ShipSpatialMap.cs").read_text(
            encoding="utf-8"
        )
        self.assertTrue(traffic.is_file())
        self.assertRegex(guid(traffic_meta.read_text(encoding="utf-8")), r"^[a-f0-9]{32}$")
        self.assertIn("class ShipTrafficCoordinator : MonoBehaviour", traffic.read_text(encoding="utf-8"))
        self.assertIn("CanEnter(this, _lastReachedCell, nextCell)", walker)
        self.assertIn("CanTraverseStraight", spatial)
        self.assertIn("new[] {", spatial)
        self.assertIn("MoveSelectedAssigned", controller)
        adapters = (root / "TacticalGroupDestinationAdapters.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("TacticalGroupDestinationAdapters.PlanShipGoals", controller)
        self.assertIn("TacticalGroupDestinationAdapters.PlanShoreGoals", controller)
        self.assertIn("TacticalGroupGoalPlanner.Allocate", adapters)
        self.assertIn("public static class TacticalGroupGoalPlanner", group)
        self.assertIn("using EntityId = FrostArk.Core.World.EntityId;", group)
        self.assertIn("using FrostArk.Core.Save;", group)
        self.assertTrue((root / "TacticalGroupDestinationAdapters.cs.meta").is_file())


    def test_crew_agency_policy_and_ship_adapter_contract(self):
        """Static import/connection proof; Unity behavior still requires G2/G3."""
        core = ROOT / "Assets/FrostArk/Core/Crew"
        unity = ROOT / "Assets/FrostArk/Unity"
        policy = (core / "CrewAgencyPolicy.cs").read_text(encoding="utf-8")
        coordinator = (unity / "ShipCrewAgencyCoordinator.cs").read_text(
            encoding="utf-8"
        )
        bootstrap = (unity / "ShipTacticalSceneBootstrap.cs").read_text(
            encoding="utf-8"
        )
        walker = (unity / "ShipGridWalker.cs").read_text(encoding="utf-8")
        for path in [
            core / "CrewAgencyPolicy.cs.meta",
            unity / "ShipCrewAgencyCoordinator.cs.meta",
            ROOT / "Assets/FrostArk/Tests/Editor/ShipCrewAgencyEditModeTests.cs.meta",
        ]:
            self.assertTrue(path.is_file(), str(path))
        self.assertIn("CrewProvisionInteractionDiscovery.Find(world, crewId)", policy)
        self.assertIn("WorldSession", coordinator)
        self.assertIn("UseCrewProvisionInteractionCommand", coordinator)
        self.assertIn("ClaimCrewInteractionCommand", coordinator)
        self.assertIn("ReleaseCrewInteractionCommand", coordinator)
        self.assertIn("RequestStopAtSafeCell()", coordinator)
        self.assertIn("RequestStopAtSafeCell()", walker)
        self.assertIn("agency.Configure(session, commander, _map)", bootstrap)
        self.assertIn("agency.SetMap(_map)", bootstrap)
        self.assertIn("using EntityId = FrostArk.Core.World.EntityId;", coordinator)
        self.assertIn("using FrostArk.Core.Save;", coordinator)


    def test_physical_duty_autonomy_reuses_staffed_role_and_authored_slots(self):
        """Static wiring only; real Unity G2/G3 is a separate gate."""
        core = ROOT / "Assets/FrostArk/Core/Crew"
        unity = ROOT / "Assets/FrostArk/Unity"
        editor = ROOT / "Assets/FrostArk/Tests/Editor"
        state = (core / "CrewState.cs").read_text(encoding="utf-8")
        planner = (core / "CrewDutyActivityPlanner.cs").read_text(
            encoding="utf-8"
        )
        component = (unity / "ShipCrewDutyAutonomy.cs").read_text(
            encoding="utf-8"
        )
        bootstrap = (unity / "ShipTacticalSceneBootstrap.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("StaffedRoleFor", state)
        self.assertIn("world.GetCrewStation(crewId)", planner)
        self.assertIn("world.CrewDuty.StaffedRoleFor(crewId, world.Minute)", planner)
        self.assertIn("CrewInteractionVerb.Rest", planner)
        self.assertIn("CrewInteractionVerb.Work", planner)
        self.assertIn("ClaimCrewInteractionCommand", component)
        self.assertIn("ReleaseCrewInteractionCommand", component)
        self.assertIn("RequestStopAtSafeCell", component)
        self.assertIn("CrewAgencyPolicy.SeekProvisionAt", component)
        self.assertIn("duty.Configure(session, commander, agency, _map)", bootstrap)
        self.assertIn("duty.SetMap(_map)", bootstrap)
        for path in [
            core / "CrewDutyActivityPlanner.cs.meta",
            unity / "ShipCrewDutyAutonomy.cs.meta",
            editor / "ShipCrewDutyAutonomyEditModeTests.cs.meta",
            ROOT / "Assets/FrostArk/Tests/PlayMode/ShipCrewDutyAutonomyPlayModeTests.cs.meta",
        ]:
            self.assertTrue(path.is_file(), str(path))
        self.assertIn("using EntityId = FrostArk.Core.World.EntityId;", component)
        self.assertIn('"WaitingForTraffic"', component)
        self.assertIn("RenewCrewDutySlotCommand", component)
        lease = (ROOT / "Assets/FrostArk/Core/Commands/WorldCommands.cs").read_text(
            encoding="utf-8"
        )
        bus = (ROOT / "Assets/FrostArk/Core/Commands/WorldCommandBus.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("class RenewCrewDutySlotCommand", lease)
        self.assertIn("CrewDutySlotRenewed", bus)
        self.assertTrue((ROOT / "tests/FrostArk.Core.Tests"
                         / "CrewDutySlotLeaseTests.cs.meta").is_file())
        playmode = (ROOT / "Assets/FrostArk/Tests/PlayMode"
                    / "ShipCrewDutyAutonomyPlayModeTests.cs").read_text(
                        encoding="utf-8"
                    )
        self.assertIn("IEnumerator EngineerReturnsToAuthoredStation", playmode)
        galley = (ROOT / "Assets/FrostArk/Tests/PlayMode"
                  / "ShipCrewProvisionPlayModeTests.cs")
        self.assertTrue(galley.is_file())
        self.assertTrue((ROOT / "Assets/FrostArk/Tests/PlayMode"
                         / "ShipCrewProvisionPlayModeTests.cs.meta").is_file())
        galley_source = galley.read_text(encoding="utf-8")
        self.assertIn("IEnumerator ThirstyCrewOnlyConsumesRealWaterAfterReachingAuthoredGalleySlot",
                      galley_source)
        self.assertIn("ShipCrewAgencyCoordinator", galley_source)
        agency = (unity / "ShipCrewAgencyCoordinator.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("foreach (var offer in CrewProvisionInteractionDiscovery.Find(", agency)
        fixture = (editor / "ShipCrewAgencyEditModeTests.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("UnplacedFirstDrinkOfferFallsBackToReachableGalley", fixture)
        self.assertIn("InventoryContainer.ShipHold", galley_source)
        self.assertIn("UnityTest", playmode)


    def test_repair_production_uses_authoritative_attendance_not_unity_clock(self):
        """Only first-class session attendance gates RepairJob progress."""
        core = ROOT / "Assets/FrostArk/Core"
        scene = ROOT / "Assets/FrostArk/Unity/ShipCrewDutyAutonomy.cs"
        editor = ROOT / "Assets/FrostArk/Tests/Editor/ShipCrewDutyAutonomyEditModeTests.cs"
        state = (core / "Crew/CrewState.cs").read_text(encoding="utf-8")
        minute = (core / "Crew/ShipCrewMinuteSystem.cs").read_text(
            encoding="utf-8"
        )
        session = (core / "Application/WorldSession.cs").read_text(
            encoding="utf-8"
        )
        adapter = scene.read_text(encoding="utf-8")
        fixture = editor.read_text(encoding="utf-8")
        self.assertIn("mayProduce(repairer.Id, DutyRole.Repair)", state)
        self.assertIn("MayProduceAtWorldMinute", session)
        self.assertIn("BeginShipWorkObservation", session)
        self.assertIn("ReportShipWorkPresence", session)
        self.assertIn("EndShipWorkObservation", session)
        self.assertIn("_mayProduce(crewId, role, minute)", minute)
        self.assertIn("ReportShipDutyPresence", adapter)
        self.assertIn("ClearShipWorkPresence", adapter)
        self.assertIn("StopObservation()", adapter)
        self.assertIn("RepairProgressRequiresPhysicalWorkArrival", fixture)
        self.assertTrue((ROOT / "tests/FrostArk.Core.Tests"
                         / "CrewRepairAttendanceTests.cs.meta").is_file())


    def test_authored_engineering_thermal_attendance_uses_same_world_clock(self):
        """The existing thermal system reads actual staffed engineer presence."""
        core = ROOT / "Assets/FrostArk/Core"
        crew = (core / "Crew/CrewState.cs").read_text(encoding="utf-8")
        thermal = (core / "Ship/ShipThermalMinuteSystem.cs").read_text(
            encoding="utf-8"
        )
        session = (core / "Application/WorldSession.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("StaffedCrewFor(DutyRole role", crew)
        self.assertIn("StaffedCrewFor(DutyRole.Engineering, minute)", thermal)
        self.assertIn("DutyRole.Engineering, minute)", thermal)
        self.assertIn("MayProduceAtWorldMinute", session)
        self.assertIn("slot.Verb == CrewInteractionVerb.Work", session)
        self.assertTrue((ROOT / "tests/FrostArk.Core.Tests"
                         / "CrewEngineeringAttendanceTests.cs.meta").is_file())


    def test_minute_bound_duty_lease_cannot_inflate_fastforward(self):
        session = (ROOT / "Assets/FrostArk/Core/Application/WorldSession.cs").read_text(
            encoding="utf-8"
        )
        adapter = (ROOT / "Assets/FrostArk/Unity/ShipCrewDutyAutonomy.cs").read_text(
            encoding="utf-8"
        )
        g1 = (ROOT / "tests/FrostArk.Core.Tests"
              / "CrewEngineeringAttendanceTests.cs").read_text(
                  encoding="utf-8"
              )
        g2 = (ROOT / "Assets/FrostArk/Tests/Editor"
              / "ShipCrewDutyAutonomyEditModeTests.cs").read_text(
                  encoding="utf-8"
              )
        self.assertIn("public bool ReportShipDutyPresence", session)
        self.assertIn("RenewFreshObservedDutyLeases()", session)
        self.assertIn("if (minutes == 1) RenewFreshObservedDutyLeases()", session)
        self.assertIn("RenewCrewDutySlotCommand", session)
        self.assertIn("ReportShipDutyPresence", adapter)
        self.assertIn("OneRenderedFrameCannotRenewThroughTwentyMinuteTimeSkip", g1)
        self.assertIn("StationedRestingCrewUsesRealBedAttestation", g2)


    def test_persistent_orders_and_physical_duties_integrate_without_phantom_work(self):
        """Static seam guard; Unity EditMode/PlayMode remains a separate gate."""
        core = ROOT / "Assets/FrostArk/Core/Application/WorldSession.cs"
        agency = ROOT / "Assets/FrostArk/Unity/ShipCrewAgencyCoordinator.cs"
        tests = ROOT / "Assets/FrostArk/Tests/Editor"
        session_source = core.read_text(encoding="utf-8")
        agency_source = agency.read_text(encoding="utf-8")
        reentry_source = (tests / "ShipCrewAgencyEditModeTests.cs").read_text(
            encoding="utf-8"
        )
        duty_source = (tests / "ShipCrewDutyAutonomyEditModeTests.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("PendingCrewMoves", session_source)
        self.assertIn("_workPresence.Remove(crewId)", session_source)
        self.assertIn("AcceptShipCrewMove(id, actor.Walker.OrderedCell)", agency_source)
        self.assertIn("RevalidatePendingPlayerMoves()", agency_source)
        self.assertIn("CrewProvisionInteractionDiscovery.Find(", agency_source)
        self.assertIn("SavedPlayerMoveRestoresBeforeAutonomousRepairResumes", duty_source)
        self.assertIn("SavedPlayerMoveCompletesBeforeThirstyCrewClaims", reentry_source)
        self.assertTrue((ROOT / "Assets/FrostArk/Core/Save"
                         / "SaveCodecPendingCrewMoves.cs.meta").is_file())

    def test_e12_persistent_ship_and_concurrent_theater_contract(self):
        """Static G0 guard for E12 (#127-#130); Unity G2/G3 remains a separate gate."""
        contract = ROOT / "docs/engineering/persistent-ship-world-runtime-contract.md"
        self.assertTrue(contract.is_file())
        session = (ROOT / "Assets/FrostArk/Core/Application/WorldSession.cs").read_text(
            encoding="utf-8"
        )
        router = (
            ROOT / "Assets/FrostArk/Core/Application/CrewTheaterMoveRouter.cs"
        ).read_text(encoding="utf-8")
        dispatcher = (
            ROOT / "Assets/FrostArk/Unity/CrewTheaterMoveDispatcher.cs"
        ).read_text(encoding="utf-8")
        bootstrap = (
            ROOT / "Assets/FrostArk/Unity/ShipTacticalSceneBootstrap.cs"
        ).read_text(encoding="utf-8")
        transit = (
            ROOT / "Assets/FrostArk/Unity/ShoreSceneTransitCoordinator.cs"
        ).read_text(encoding="utf-8")
        walker = (ROOT / "Assets/FrostArk/Unity/ShipGridWalker.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("ResidentShipSceneId", session)
        self.assertIn("IsResidentShipScene", session)
        self.assertIn("requestedTheater == PhysicalTheater.Shore", router)
        self.assertIn("MatchesExpectedScene", dispatcher)
        self.assertIn("SyncCrewPresenceWithSession", bootstrap)
        self.assertIn("SuspendForShoreDeparture", walker)
        self.assertIn("ResumeFromShoreAt", walker)
        self.assertIn("IsResidentShipScene", transit)
        self.assertIn("TryFindLoadedShipScene", transit)


if __name__ == "__main__":
    unittest.main()
