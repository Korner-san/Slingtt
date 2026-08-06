namespace Slingtt.Sim;

public sealed class Prediction
{
    public List<Vec2> Points { get; } = new(); // sampled positions along the predicted travel
    public List<Vec2> Bounces { get; } = new(); // wall-bounce points, truncated to maxBounces
}

public static class Predict
{
    /// <summary>Aim prediction runs the REAL simulation on a cheap structural clone
    /// with damage disabled — a preview produced by a separate approximation would
    /// diverge from the outcome and destroy trust in the mechanic. Truncating an
    /// honest prediction at N bounces is fine; approximating is not.</summary>
    public static Prediction? Trajectory(
        World world,
        SimConfig cfg,
        LaunchInput input,
        int maxBounces = 2,
        int maxTicks = 600)
    {
        if (world.Phase != Phase.Aiming)
        {
            return null;
        }

        World clone = world.Clone();
        clone.DamageEnabled = false; // no damage, no events consumed downstream, no RNG draws
        if (!BattleSim.TryLaunch(clone, cfg, input))
        {
            return null;
        }

        Actor self = clone.ActiveActor();
        var prediction = new Prediction();
        prediction.Points.Add(self.Pos);

        for (int i = 0; i < maxTicks && clone.Phase == Phase.Travelling; i++)
        {
            BattleSim.Step(clone, cfg);
            prediction.Points.Add(self.Pos);
            foreach (SimEvent ev in clone.Events)
            {
                if (ev.Kind == SimEventKind.WallBounce)
                {
                    prediction.Bounces.Add(ev.Pos);
                }
            }
            clone.Events.Clear();
            if (prediction.Bounces.Count > maxBounces)
            {
                prediction.Bounces.RemoveRange(maxBounces, prediction.Bounces.Count - maxBounces);
                break; // show only the bounces the player has earned
            }
        }
        return prediction;
    }
}
