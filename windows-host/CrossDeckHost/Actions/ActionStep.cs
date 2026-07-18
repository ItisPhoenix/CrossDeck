namespace CrossDeckHost.Actions;

/// <summary>
/// One row in the visual step-list editor. Delay lives on the step itself (not a parallel
/// array indexed by position) so reordering or removing a step can never desync its delay
/// from the wrong action — the bug class the old text-line format allowed.
/// </summary>
public class ActionStep
{
    public ProfileStore.ActionModel Action { get; set; } = new();
    public int DelayAfterMs { get; set; }
}
