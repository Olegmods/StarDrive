using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDUtils;
using Ship_Game;
using Ship_Game.Fleets;
using Ship_Game.Ships;
using Ship_Game.Utils;
using Vector2 = SDGraphics.Vector2;

namespace UnitTests.Fleets;

// Fleet focus-fire: the fleet commits to ONE hostile threat cluster (its Focus) so member
// ships converge fire instead of each independently spreading. Doctrine is "closest
// killable" - the nearest cluster the fleet can favourably engage.
[TestClass]
public class FleetFocusFireTests : StarDriveTest
{
    const string COMBAT_SHIP = "Terran-Prototype";
    const string SCOUT = "TEST_Vulcan Scout";

    public FleetFocusFireTests()
    {
        LoadStarterShips(COMBAT_SHIP, SCOUT);
        CreateUniverseAndPlayerEmpire();
        UState.Events.Disabled = true;
    }

    float SpawnClump(Vector2 pos, Empire owner, string shipName, int numShips)
    {
        var random = new SeededRandom(1337);
        float strength = 0f;
        for (int i = 0; i < numShips; ++i)
            strength += SpawnShip(shipName, owner, pos + random.Vector2D(2000)).GetStrength();
        return strength;
    }

    Fleet CreatePlayerFleetAt(Vector2 pos, int numShips)
    {
        var ships = new Array<Ship>();
        for (int i = 0; i < numShips; ++i)
            ships.Add(SpawnShip(COMBAT_SHIP, Player, pos + new Vector2(i * 500, 0)));

        Fleet fleet = Player.CreateFleet(1, null);
        fleet.AddShips(ships);
        return fleet;
    }

    void ScanAndUpdateThreats()
    {
        UState.Objects.Update(new(time: 2.0f));
        Player.Threats.Update(new(time: 2.0f));
    }

    [TestMethod]
    public void FleetCommitsToClosestKillableClump()
    {
        Vector2 near = new(15_000, 0);
        Vector2 far  = new(45_000, 0);

        Fleet fleet = CreatePlayerFleetAt(Vector2.Zero, 4);

        // pickets to guarantee the empire observes BOTH enemy clumps (the fleet only reads
        // empire-wide ThreatMatrix intel, so vision can come from anywhere we own)
        SpawnShip(SCOUT, Player, near);
        SpawnShip(SCOUT, Player, far);

        // two weak (killable) enemy clumps, near and far
        SpawnClump(near, Enemy, SCOUT, 4);
        SpawnClump(far,  Enemy, SCOUT, 4);
        ScanAndUpdateThreats();

        fleet.Update(FixedSimTime.Zero);

        AssertTrue(fleet.Focus != null, "Fleet must commit to a focus clump when hostiles are known");
        AssertTrue(fleet.Focus.Center.Distance(near) < fleet.Focus.Center.Distance(far),
            $"Focus must be the nearer clump. Focus.Center={fleet.Focus.Center}");
    }

    [TestMethod]
    public void FleetSkipsTooStrongClumpForKillableFar()
    {
        Vector2 near = new(15_000, 0);
        Vector2 far  = new(45_000, 0);

        // small fleet so the near clump out-muscles it
        Fleet fleet = CreatePlayerFleetAt(Vector2.Zero, 2);

        SpawnShip(SCOUT, Player, near);
        SpawnShip(SCOUT, Player, far);

        // near clump is a wall of combat ships (NOT killable); far clump is a couple of
        // scouts (killable)
        float nearStr = SpawnClump(near, Enemy, COMBAT_SHIP, 12);
        SpawnClump(far, Enemy, SCOUT, 2);
        ScanAndUpdateThreats();

        fleet.Update(FixedSimTime.Zero);

        // setup sanity: the near clump really is too strong to take
        AssertTrue(nearStr > fleet.GetStrength(),
            $"test setup: near clump ({nearStr}) must out-strength the fleet ({fleet.GetStrength()})");

        AssertTrue(fleet.Focus != null, "Fleet must still commit to a focus clump");
        AssertTrue(fleet.Focus.Center.Distance(far) < fleet.Focus.Center.Distance(near),
            $"Focus must skip the too-strong near clump for the killable far one. Focus.Center={fleet.Focus.Center}");
    }
}
